using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Hugin.Security;

/// <summary>
/// Checks client IP addresses against DNS-based blacklists (DNSBL).
/// Used to prevent connections from known spam sources, open proxies, etc.
/// </summary>
public sealed class DnsBlacklistChecker : IDnsBlacklistChecker
{
    private readonly ILogger<DnsBlacklistChecker> _logger;
    private readonly List<DnsBlacklist> _blacklists = new();
    private readonly ConcurrentDictionary<string, DnsBlCheckResult> _cache = new();
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(15);
    private readonly object _configLock = new();

    /// <summary>
    /// Creates a new DNSBL checker.
    /// </summary>
    /// <param name="logger">Logger for DNSBL operations.</param>
    public DnsBlacklistChecker(ILogger<DnsBlacklistChecker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Adds a DNSBL to check against.
    /// </summary>
    /// <param name="zone">The DNSBL zone (e.g., "dnsbl.example.com").</param>
    /// <param name="action">Action to take if listed.</param>
    /// <param name="reason">Reason to show user if listed.</param>
    /// <param name="replyMask">Expected reply mask (null for any reply).</param>
    public void AddBlacklist(string zone, DnsBlAction action, string reason, string? replyMask = null)
    {
        lock (_configLock)
        {
            _blacklists.Add(new DnsBlacklist
            {
                Zone = zone,
                Action = action,
                Reason = reason,
                ReplyMask = replyMask
            });
        }

        _logger.LogInformation("Added DNSBL: {Zone} (action: {Action})", zone, action);
    }

    /// <summary>
    /// Removes a DNSBL.
    /// </summary>
    /// <param name="zone">The DNSBL zone to remove.</param>
    /// <returns>True if removed.</returns>
    public bool RemoveBlacklist(string zone)
    {
        lock (_configLock)
        {
            var removed = _blacklists.RemoveAll(b => b.Zone.Equals(zone, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                _logger.LogInformation("Removed DNSBL: {Zone}", zone);
            }
            return removed;
        }
    }

    /// <summary>
    /// Gets all configured blacklists.
    /// </summary>
    public IReadOnlyList<DnsBlacklist> GetBlacklists()
    {
        lock (_configLock)
        {
            return _blacklists.ToList();
        }
    }

    /// <inheritdoc />
    public async Task<DnsBlCheckResult> CheckAsync(IPAddress address, CancellationToken cancellationToken = default)
    {
        // Skip private/local addresses
        if (IsPrivateOrLocal(address))
        {
            return DnsBlCheckResult.NotListed;
        }

        // Check cache
        var cacheKey = address.ToString();
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            if (cached.CheckedAt + _cacheDuration > DateTimeOffset.UtcNow)
            {
                return cached;
            }
            _cache.TryRemove(cacheKey, out _);
        }

        List<DnsBlacklist> listsToCheck;
        lock (_configLock)
        {
            listsToCheck = _blacklists.ToList();
        }

        if (listsToCheck.Count == 0)
        {
            return DnsBlCheckResult.NotListed;
        }

        // Reverse the IP for DNSBL lookup
        var reversedIp = ReverseIp(address);

        foreach (var blacklist in listsToCheck)
        {
            try
            {
                var result = await CheckSingleListAsync(reversedIp, blacklist, cancellationToken);
                if (result.IsListed)
                {
                    // Cache and return on first match
                    _cache[cacheKey] = result;
                    _logger.LogWarning("IP {Address} listed in {Zone}: {Reason}",
                        address, blacklist.Zone, result.Reason);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error checking {Zone} for {Address}", blacklist.Zone, address);
                // Continue checking other lists
            }
        }

        var notListed = DnsBlCheckResult.NotListed;
        _cache[cacheKey] = notListed;
        return notListed;
    }

    /// <summary>
    /// Clears the result cache.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
        _logger.LogDebug("DNSBL cache cleared");
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    public (int CacheSize, int ListedCount) GetCacheStats()
    {
        var listed = _cache.Values.Count(v => v.IsListed);
        return (_cache.Count, listed);
    }

    /// <summary>
    /// Checks a single blacklist.
    /// </summary>
    private static async Task<DnsBlCheckResult> CheckSingleListAsync(
        string reversedIp,
        DnsBlacklist blacklist,
        CancellationToken cancellationToken)
    {
        var lookupHost = $"{reversedIp}.{blacklist.Zone}";

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(lookupHost, cancellationToken);

            if (addresses.Length == 0)
            {
                return DnsBlCheckResult.NotListed;
            }

            // Check if any response matches the expected mask
            foreach (var addr in addresses)
            {
                if (addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    continue;
                }

                var addrBytes = addr.GetAddressBytes();

                // If no mask specified, any response means listed
                if (string.IsNullOrEmpty(blacklist.ReplyMask))
                {
                    return new DnsBlCheckResult
                    {
                        IsListed = true,
                        Zone = blacklist.Zone,
                        Action = blacklist.Action,
                        Reason = blacklist.Reason,
                        ReplyAddress = addr.ToString(),
                        CheckedAt = DateTimeOffset.UtcNow
                    };
                }

                // Check against mask (e.g., "127.0.0.*" or "127.0.0.2")
                if (MatchesMask(addr, blacklist.ReplyMask))
                {
                    return new DnsBlCheckResult
                    {
                        IsListed = true,
                        Zone = blacklist.Zone,
                        Action = blacklist.Action,
                        Reason = blacklist.Reason,
                        ReplyAddress = addr.ToString(),
                        CheckedAt = DateTimeOffset.UtcNow
                    };
                }
            }

            return DnsBlCheckResult.NotListed;
        }
        catch (System.Net.Sockets.SocketException)
        {
            // NXDOMAIN - not listed
            return DnsBlCheckResult.NotListed;
        }
    }

    /// <summary>
    /// Reverses an IP address for DNSBL lookup.
    /// </summary>
    private static string ReverseIp(IPAddress address)
    {
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return $"{bytes[3]}.{bytes[2]}.{bytes[1]}.{bytes[0]}";
        }

        // For IPv6, expand and reverse nibbles
        var ipv6Bytes = address.GetAddressBytes();
        var nibbles = new List<string>();
        foreach (var b in ipv6Bytes)
        {
            nibbles.Add((b >> 4).ToString("x", CultureInfo.InvariantCulture));
            nibbles.Add((b & 0xF).ToString("x", CultureInfo.InvariantCulture));
        }
        nibbles.Reverse();
        return string.Join(".", nibbles);
    }

    /// <summary>
    /// Checks if an IP address is private or local.
    /// </summary>
    private static bool IsPrivateOrLocal(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            // 10.0.0.0/8
            if (bytes[0] == 10)
            {
                return true;
            }

            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            // 127.0.0.0/8
            if (bytes[0] == 127)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if an address matches a mask pattern.
    /// </summary>
    private static bool MatchesMask(IPAddress address, string mask)
    {
        if (mask.Contains('*'))
        {
            // Wildcard match
            var addrStr = address.ToString();
            var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(mask)
                .Replace("\\*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(addrStr, pattern);
        }

        // Exact match
        return address.ToString() == mask;
    }
}

/// <summary>
/// Interface for DNSBL checking.
/// </summary>
public interface IDnsBlacklistChecker
{
    /// <summary>
    /// Checks if an IP address is listed in any configured DNSBL.
    /// </summary>
    /// <param name="address">The IP address to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The check result.</returns>
    Task<DnsBlCheckResult> CheckAsync(IPAddress address, CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for a DNSBL.
/// </summary>
public sealed class DnsBlacklist
{
    /// <summary>
    /// The DNSBL zone (e.g., "dnsbl.example.com").
    /// </summary>
    public required string Zone { get; init; }

    /// <summary>
    /// Action to take if listed.
    /// </summary>
    public DnsBlAction Action { get; init; } = DnsBlAction.Reject;

    /// <summary>
    /// Reason to show user if listed.
    /// </summary>
    public string Reason { get; init; } = "Your IP address is listed in a DNS blacklist";

    /// <summary>
    /// Expected reply mask (null for any reply).
    /// </summary>
    public string? ReplyMask { get; init; }
}

/// <summary>
/// Action to take when a client is DNSBL-listed.
/// </summary>
public enum DnsBlAction
{
    /// <summary>Reject the connection.</summary>
    Reject,

    /// <summary>Apply a warning/mark but allow.</summary>
    Mark,

    /// <summary>Require additional authentication (e.g., CAPTCHA, SASL).</summary>
    RequireAuth,

    /// <summary>Log only, no action.</summary>
    LogOnly
}

/// <summary>
/// Result of a DNSBL check.
/// </summary>
public readonly struct DnsBlCheckResult
{
    /// <summary>
    /// Result for addresses not listed.
    /// </summary>
    public static readonly DnsBlCheckResult NotListed = new()
    {
        IsListed = false,
        CheckedAt = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Whether the IP is listed.
    /// </summary>
    public bool IsListed { get; init; }

    /// <summary>
    /// The DNSBL zone that listed the IP.
    /// </summary>
    public string? Zone { get; init; }

    /// <summary>
    /// The action to take.
    /// </summary>
    public DnsBlAction Action { get; init; }

    /// <summary>
    /// The reason for the listing.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// The reply address from the DNSBL.
    /// </summary>
    public string? ReplyAddress { get; init; }

    /// <summary>
    /// When the check was performed.
    /// </summary>
    public DateTimeOffset CheckedAt { get; init; }
}
