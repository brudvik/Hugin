namespace Hugin.Core.Interfaces;

/// <summary>
/// Represents a trusted WEBIRC block configuration.
/// WEBIRC allows trusted proxies (web gateways, bouncers) to relay
/// the real user IP address to the IRC server.
/// </summary>
public sealed class WebircBlock
{
    /// <summary>
    /// Gets or sets the name/identifier for this WEBIRC block.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the password that the gateway must provide.
    /// </summary>
    public required string Password { get; set; }

    /// <summary>
    /// Gets or sets the list of allowed IP addresses or CIDR ranges
    /// that can use this WEBIRC block.
    /// </summary>
    public List<string> AllowedHosts { get; set; } = new();

    /// <summary>
    /// Gets or sets whether to apply host cloaking after WEBIRC.
    /// </summary>
    public bool ApplyCloaking { get; set; } = true;
}

/// <summary>
/// Service for validating WEBIRC requests.
/// </summary>
public interface IWebircValidator
{
    /// <summary>
    /// Validates a WEBIRC request and returns the matching block if valid.
    /// </summary>
    /// <param name="password">The password provided by the gateway.</param>
    /// <param name="gatewayIp">The IP address of the gateway connection.</param>
    /// <returns>The matching WEBIRC block if valid; null otherwise.</returns>
    WebircBlock? ValidateWebirc(string password, string gatewayIp);
}
