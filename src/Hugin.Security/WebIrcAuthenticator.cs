using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Hugin.Security;

/// <summary>
/// Handles WEBIRC authentication for web-based IRC clients.
/// WEBIRC allows trusted gateways to pass through the real client IP.
/// Syntax: WEBIRC password gateway hostname ip [:options]
/// </summary>
public sealed class WebIrcAuthenticator : IWebIrcAuthenticator
{
    private readonly ILogger<WebIrcAuthenticator> _logger;
    private readonly ConcurrentDictionary<string, WebIrcGateway> _gateways = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new WebIRC authenticator.
    /// </summary>
    /// <param name="logger">Logger for WebIRC operations.</param>
    public WebIrcAuthenticator(ILogger<WebIrcAuthenticator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Adds a trusted WebIRC gateway.
    /// </summary>
    /// <param name="name">Gateway name/identifier.</param>
    /// <param name="password">Password for authentication.</param>
    /// <param name="allowedHosts">List of allowed source hostnames/IPs.</param>
    /// <param name="hashType">Password hash type (plain, sha256, sha512).</param>
    /// <param name="options">Additional options.</param>
    public void AddGateway(
        string name,
        string password,
        IReadOnlyList<string> allowedHosts,
        WebIrcHashType hashType = WebIrcHashType.Plain,
        WebIrcGatewayOptions? options = null)
    {
        _gateways[name] = new WebIrcGateway
        {
            Name = name,
            Password = password,
            HashType = hashType,
            AllowedHosts = allowedHosts.ToList(),
            Options = options ?? new WebIrcGatewayOptions()
        };

        _logger.LogInformation("Added WebIRC gateway: {Name} (hosts: {Hosts})",
            name, string.Join(", ", allowedHosts));
    }

    /// <summary>
    /// Removes a WebIRC gateway.
    /// </summary>
    /// <param name="name">Gateway name to remove.</param>
    /// <returns>True if removed.</returns>
    public bool RemoveGateway(string name)
    {
        var removed = _gateways.TryRemove(name, out _);
        if (removed)
        {
            _logger.LogInformation("Removed WebIRC gateway: {Name}", name);
        }
        return removed;
    }

    /// <summary>
    /// Gets all configured gateways.
    /// </summary>
    public IReadOnlyList<WebIrcGateway> GetGateways()
    {
        return _gateways.Values.ToList();
    }

    /// <inheritdoc />
    public WebIrcResult Authenticate(WebIrcRequest request, IPAddress sourceAddress)
    {
        // Find matching gateway
        WebIrcGateway? matchedGateway = null;

        foreach (var gateway in _gateways.Values)
        {
            if (IsHostAllowed(gateway, sourceAddress))
            {
                matchedGateway = gateway;
                break;
            }
        }

        if (matchedGateway is null)
        {
            _logger.LogWarning("WebIRC attempt from untrusted host: {Source}", sourceAddress);
            return WebIrcResult.Failure("Connection from untrusted host");
        }

        // Verify password
        if (!VerifyPassword(request.Password, matchedGateway.Password, matchedGateway.HashType))
        {
            _logger.LogWarning("WebIRC authentication failed for gateway {Gateway} from {Source}",
                matchedGateway.Name, sourceAddress);
            return WebIrcResult.Failure("Invalid gateway password");
        }

        // Validate the spoofed IP
        if (!IPAddress.TryParse(request.IpAddress, out var clientIp))
        {
            _logger.LogWarning("WebIRC invalid IP format: {Ip}", request.IpAddress);
            return WebIrcResult.Failure("Invalid IP address format");
        }

        // Check if the gateway is allowed to spoof this IP type
        if (!matchedGateway.Options.AllowIPv6 && clientIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return WebIrcResult.Failure("IPv6 spoofing not allowed for this gateway");
        }

        _logger.LogInformation("WebIRC authenticated: {Gateway} spoofing {Host} ({Ip})",
            matchedGateway.Name, request.Hostname, request.IpAddress);

        return new WebIrcResult
        {
            Success = true,
            GatewayName = matchedGateway.Name,
            RealIpAddress = clientIp,
            RealHostname = request.Hostname,
            Secure = request.Options?.Contains("secure") ?? false,
            TlsCertFingerprint = ExtractOption(request.Options, "certfp"),
            Account = ExtractOption(request.Options, "account")
        };
    }

    /// <summary>
    /// Checks if a source address is allowed for a gateway.
    /// </summary>
    private static bool IsHostAllowed(WebIrcGateway gateway, IPAddress source)
    {
        foreach (var allowed in gateway.AllowedHosts)
        {
            // Check for CIDR notation
            if (allowed.Contains('/'))
            {
                if (IsInCidr(source, allowed))
                {
                    return true;
                }
                continue;
            }

            // Check for wildcard
            if (allowed.Contains('*'))
            {
                var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(allowed)
                    .Replace("\\*", ".*") + "$";
                if (System.Text.RegularExpressions.Regex.IsMatch(source.ToString(), pattern))
                {
                    return true;
                }
                continue;
            }

            // Exact match
            if (IPAddress.TryParse(allowed, out var allowedIp))
            {
                if (source.Equals(allowedIp))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if an IP is in a CIDR range.
    /// </summary>
    private static bool IsInCidr(IPAddress address, string cidr)
    {
        try
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2)
            {
                return false;
            }

            if (!IPAddress.TryParse(parts[0], out var network) ||
                !int.TryParse(parts[1], out var prefixLength))
            {
                return false;
            }

            if (address.AddressFamily != network.AddressFamily)
            {
                return false;
            }

            var networkBytes = network.GetAddressBytes();
            var addressBytes = address.GetAddressBytes();

            var byteCount = prefixLength / 8;
            var bitCount = prefixLength % 8;

            for (int i = 0; i < byteCount; i++)
            {
                if (networkBytes[i] != addressBytes[i])
                {
                    return false;
                }
            }

            if (bitCount > 0 && byteCount < networkBytes.Length)
            {
                var mask = (byte)(0xFF << (8 - bitCount));
                if ((networkBytes[byteCount] & mask) != (addressBytes[byteCount] & mask))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verifies a password against the stored hash.
    /// </summary>
    private static bool VerifyPassword(string provided, string stored, WebIrcHashType hashType)
    {
        return hashType switch
        {
            WebIrcHashType.Plain => provided == stored,
            WebIrcHashType.Sha256 => string.Equals(HashSha256(provided), stored, StringComparison.OrdinalIgnoreCase),
            WebIrcHashType.Sha512 => string.Equals(HashSha512(provided), stored, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string HashSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string HashSha512(string input)
    {
        var bytes = SHA512.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Extracts an option value from the options string.
    /// </summary>
    private static string? ExtractOption(string? options, string key)
    {
        if (string.IsNullOrEmpty(options))
        {
            return null;
        }

        var prefix = $"{key}=";
        var parts = options.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return part[prefix.Length..];
            }
        }

        return null;
    }
}

/// <summary>
/// Interface for WebIRC authentication.
/// </summary>
public interface IWebIrcAuthenticator
{
    /// <summary>
    /// Authenticates a WebIRC request.
    /// </summary>
    /// <param name="request">The WebIRC request.</param>
    /// <param name="sourceAddress">The actual source IP of the connection.</param>
    /// <returns>The authentication result.</returns>
    WebIrcResult Authenticate(WebIrcRequest request, IPAddress sourceAddress);
}

/// <summary>
/// A WebIRC authentication request.
/// </summary>
public sealed class WebIrcRequest
{
    /// <summary>Gateway password.</summary>
    public required string Password { get; init; }

    /// <summary>Gateway name.</summary>
    public required string Gateway { get; init; }

    /// <summary>Client's real hostname.</summary>
    public required string Hostname { get; init; }

    /// <summary>Client's real IP address.</summary>
    public required string IpAddress { get; init; }

    /// <summary>Additional options (IRCv3 style).</summary>
    public string? Options { get; init; }
}

/// <summary>
/// Result of WebIRC authentication.
/// </summary>
public sealed class WebIrcResult
{
    /// <summary>Creates a failure result.</summary>
    public static WebIrcResult Failure(string reason) => new()
    {
        Success = false,
        FailureReason = reason
    };

    /// <summary>Whether authentication succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Failure reason if not successful.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Name of the authenticated gateway.</summary>
    public string? GatewayName { get; init; }

    /// <summary>The real IP address of the client.</summary>
    public IPAddress? RealIpAddress { get; init; }

    /// <summary>The real hostname of the client.</summary>
    public string? RealHostname { get; init; }

    /// <summary>Whether the client connection is secure.</summary>
    public bool Secure { get; init; }

    /// <summary>TLS certificate fingerprint if provided.</summary>
    public string? TlsCertFingerprint { get; init; }

    /// <summary>Account name if provided.</summary>
    public string? Account { get; init; }
}

/// <summary>
/// Configuration for a WebIRC gateway.
/// </summary>
public sealed class WebIrcGateway
{
    /// <summary>Gateway name/identifier.</summary>
    public required string Name { get; init; }

    /// <summary>Authentication password (or hash).</summary>
    public required string Password { get; init; }

    /// <summary>Hash type used for password.</summary>
    public WebIrcHashType HashType { get; init; }

    /// <summary>Allowed source hosts/IPs.</summary>
    public required List<string> AllowedHosts { get; init; }

    /// <summary>Additional options.</summary>
    public WebIrcGatewayOptions Options { get; init; } = new();
}

/// <summary>
/// Options for a WebIRC gateway.
/// </summary>
public sealed class WebIrcGatewayOptions
{
    /// <summary>Allow IPv6 address spoofing.</summary>
    public bool AllowIPv6 { get; init; } = true;

    /// <summary>Trust certificate fingerprints from this gateway.</summary>
    public bool TrustCertFingerprint { get; init; } = true;

    /// <summary>Trust account information from this gateway.</summary>
    public bool TrustAccount { get; init; }

    /// <summary>Mark users from this gateway with a special mode.</summary>
    public bool MarkUsers { get; init; }
}

/// <summary>
/// Password hash type for WebIRC.
/// </summary>
public enum WebIrcHashType
{
    /// <summary>Plaintext password.</summary>
    Plain,

    /// <summary>SHA-256 hash.</summary>
    Sha256,

    /// <summary>SHA-512 hash.</summary>
    Sha512
}
