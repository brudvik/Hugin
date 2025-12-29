using System.Security.Cryptography;
using System.Text;

namespace Hugin.Security;

/// <summary>
/// Provides hostname cloaking for user privacy.
/// </summary>
public sealed class HostCloaker
{
    private readonly byte[] _secret;
    private readonly string _cloakSuffix;

    /// <summary>
    /// Creates a new host cloaker.
    /// </summary>
    /// <param name="secret">The secret key used for hashing.</param>
    /// <param name="cloakSuffix">The suffix to append to cloaked hostnames.</param>
    public HostCloaker(string secret, string cloakSuffix = "hugin.cloak")
    {
        _secret = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        _cloakSuffix = cloakSuffix;
    }

    /// <summary>
    /// Cloaks a hostname.
    /// </summary>
    public string Cloak(string hostname, string? ipAddress = null)
    {
        // For IP addresses, use a different format
        if (IsIpAddress(hostname))
        {
            return CloakIpAddress(hostname);
        }

        // For hostnames, preserve some structure
        return CloakHostname(hostname);
    }

    /// <summary>
    /// Cloaks an IP address.
    /// </summary>
    public string CloakIpAddress(string ip)
    {
        var hash = ComputeHash(ip);
        var parts = hash[..12]; // Use first 12 chars

        // Format: hash.cloak
        return $"{parts[..4]}.{parts[4..8]}.{parts[8..12]}.{_cloakSuffix}";
    }

    /// <summary>
    /// Cloaks a hostname while preserving structure.
    /// </summary>
    public string CloakHostname(string hostname)
    {
        var parts = hostname.Split('.');
        if (parts.Length <= 2)
        {
            // Short hostname, fully cloak
            var shortHash = ComputeHash(hostname);
            return $"{shortHash[..8]}.{_cloakSuffix}";
        }

        // Preserve the last two parts (TLD and domain)
        var hostPart = string.Join(".", parts[..^2]);
        var domainPart = string.Join(".", parts[^2..]);

        var hostHash = ComputeHash(hostPart);
        return $"{hostHash[..8]}.{domainPart}";
    }

    /// <summary>
    /// Generates an account-based cloak.
    /// </summary>
    public string CloakAccount(string accountName)
    {
        return $"{accountName}.{_cloakSuffix}";
    }

    private string ComputeHash(string input)
    {
        // Use HMAC-SHA256 instead of prefix-construction to prevent length-extension attacks
        using var hmac = new HMACSHA256(_secret);
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hash = hmac.ComputeHash(inputBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsIpAddress(string value)
    {
        return System.Net.IPAddress.TryParse(value, out _);
    }
}
