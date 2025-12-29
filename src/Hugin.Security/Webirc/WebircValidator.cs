using System.Net;
using Hugin.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace Hugin.Security.Webirc;

/// <summary>
/// Configuration options for WEBIRC support.
/// </summary>
public sealed class WebircOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "Hugin:Webirc";

    /// <summary>
    /// Gets or sets whether WEBIRC is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the list of trusted WEBIRC blocks.
    /// </summary>
    public List<WebircBlock> Blocks { get; set; } = new();
}

/// <summary>
/// Default implementation of <see cref="IWebircValidator"/> that validates
/// WEBIRC requests against configured trusted blocks.
/// </summary>
public sealed class WebircValidator : IWebircValidator
{
    private readonly WebircOptions _options;

    /// <summary>
    /// Creates a new WEBIRC validator.
    /// </summary>
    /// <param name="options">The WEBIRC configuration options.</param>
    public WebircValidator(IOptions<WebircOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc/>
    public WebircBlock? ValidateWebirc(string password, string gatewayIp)
    {
        if (!_options.Enabled || string.IsNullOrEmpty(password))
        {
            return null;
        }

        foreach (var block in _options.Blocks)
        {
            // Check password match (constant-time comparison for security)
            if (!ConstantTimeEquals(password, block.Password))
            {
                continue;
            }

            // Check if gateway IP is in allowed list
            if (IsIpAllowed(gatewayIp, block.AllowedHosts))
            {
                return block;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if an IP address matches any of the allowed host patterns.
    /// </summary>
    private static bool IsIpAllowed(string ipAddress, List<string> allowedHosts)
    {
        if (allowedHosts.Count == 0)
        {
            // If no hosts specified, allow any (for development/testing)
            return true;
        }

        if (!IPAddress.TryParse(ipAddress, out var ip))
        {
            return false;
        }

        foreach (var pattern in allowedHosts)
        {
            // Check for CIDR notation (e.g., 192.168.1.0/24)
            if (pattern.Contains('/'))
            {
                if (IsInCidrRange(ip, pattern))
                {
                    return true;
                }
            }
            else if (IPAddress.TryParse(pattern, out var allowedIp))
            {
                // Direct IP match
                if (ip.Equals(allowedIp))
                {
                    return true;
                }
            }
            else
            {
                // Wildcard hostname pattern (e.g., *.gateway.example.com)
                // For now, just exact match
                if (pattern.Equals(ipAddress, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if an IP is within a CIDR range.
    /// </summary>
    private static bool IsInCidrRange(IPAddress ip, string cidr)
    {
        try
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var networkAddress))
            {
                return false;
            }

            if (!int.TryParse(parts[1], out var prefixLength))
            {
                return false;
            }

            var networkBytes = networkAddress.GetAddressBytes();
            var ipBytes = ip.GetAddressBytes();

            // Must be same address family
            if (networkBytes.Length != ipBytes.Length)
            {
                return false;
            }

            // Check each bit
            int fullBytes = prefixLength / 8;
            int remainingBits = prefixLength % 8;

            // Check full bytes
            for (int i = 0; i < fullBytes && i < networkBytes.Length; i++)
            {
                if (networkBytes[i] != ipBytes[i])
                {
                    return false;
                }
            }

            // Check remaining bits
            if (remainingBits > 0 && fullBytes < networkBytes.Length)
            {
                byte mask = (byte)(0xFF << (8 - remainingBits));
                if ((networkBytes[fullBytes] & mask) != (ipBytes[fullBytes] & mask))
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
    /// Constant-time string comparison to prevent timing attacks.
    /// </summary>
    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            // Still do the comparison to keep timing somewhat constant
            int dummy = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dummy |= a[i] ^ (i < b.Length ? b[i] : 0);
            }
            return false;
        }

        int result = 0;
        for (int i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }
        return result == 0;
    }
}
