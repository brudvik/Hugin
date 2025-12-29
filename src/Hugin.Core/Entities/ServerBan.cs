using Hugin.Core.ValueObjects;

namespace Hugin.Core.Entities;

/// <summary>
/// Represents a server ban (K-Line, G-Line, Z-Line, etc.).
/// </summary>
public sealed class ServerBan
{
    /// <summary>
    /// Gets the unique identifier for this ban.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the type of ban.
    /// </summary>
    public BanType Type { get; }

    /// <summary>
    /// Gets the hostmask pattern to match (user@host for K-Line, IP/CIDR for Z-Line).
    /// </summary>
    public string Pattern { get; }

    /// <summary>
    /// Gets the mask (alias for Pattern for compatibility).
    /// </summary>
    public string Mask => Pattern;

    /// <summary>
    /// Gets the reason for the ban.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets when the ban was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Gets when the ban expires (null for permanent).
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; }

    /// <summary>
    /// Gets who set the ban.
    /// </summary>
    public string SetBy { get; }

    /// <summary>
    /// Gets whether this ban has expired.
    /// </summary>
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value <= DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets whether this ban is permanent.
    /// </summary>
    public bool IsPermanent => !ExpiresAt.HasValue;

    /// <summary>
    /// Creates a new server ban.
    /// </summary>
    public ServerBan(
        BanType type,
        string pattern,
        string reason,
        string setBy,
        TimeSpan? duration = null)
    {
        Id = Guid.NewGuid();
        Type = type;
        Pattern = pattern;
        Reason = reason;
        SetBy = setBy;
        CreatedAt = DateTimeOffset.UtcNow;
        ExpiresAt = duration.HasValue ? CreatedAt + duration.Value : null;
    }

    /// <summary>
    /// Creates a new server ban with explicit timestamps.
    /// </summary>
    public ServerBan(
        BanType type,
        string pattern,
        string reason,
        string setBy,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
    {
        Id = Guid.NewGuid();
        Type = type;
        Pattern = pattern;
        Reason = reason;
        SetBy = setBy;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Creates a server ban from stored data.
    /// </summary>
    public ServerBan(
        Guid id,
        BanType type,
        string pattern,
        string reason,
        string setBy,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
    {
        Id = id;
        Type = type;
        Pattern = pattern;
        Reason = reason;
        SetBy = setBy;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Checks if a user@host matches this ban pattern.
    /// </summary>
    public bool Matches(string userHost)
    {
        if (Type == BanType.ZLine)
        {
            // Z-Line matches IP addresses
            return MatchesIp(userHost);
        }

        // K-Line and G-Line match user@host patterns
        return Hostmask.WildcardMatch(userHost, Pattern);
    }

    /// <summary>
    /// Checks if an IP address matches this ban pattern.
    /// </summary>
    public bool MatchesIp(string ipAddress)
    {
        if (Pattern.Contains('/'))
        {
            // CIDR notation - simplified check
            var cidrParts = Pattern.Split('/');
            if (cidrParts.Length == 2 && 
                System.Net.IPAddress.TryParse(cidrParts[0], out var network) &&
                System.Net.IPAddress.TryParse(ipAddress, out var ip))
            {
                // For simplicity, just check prefix match for now
                // A full implementation would use proper CIDR matching
                return ipAddress.StartsWith(cidrParts[0].Split('.')[0] + ".", StringComparison.Ordinal);
            }
        }

        // Exact match or wildcard
        return Hostmask.WildcardMatch(ipAddress, Pattern);
    }
}

/// <summary>
/// Types of server bans.
/// </summary>
public enum BanType
{
    /// <summary>
    /// K-Line: Local server ban by user@host pattern.
    /// </summary>
    KLine,

    /// <summary>
    /// G-Line: Network-wide ban by user@host pattern.
    /// </summary>
    GLine,

    /// <summary>
    /// Z-Line: Ban by IP address or CIDR range.
    /// </summary>
    ZLine,

    /// <summary>
    /// Jupe: Blocks a server name from linking.
    /// </summary>
    Jupe
}
