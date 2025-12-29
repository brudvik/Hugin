using System.Net;
using Hugin.Core.Interfaces;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the WEBIRC command for trusted gateway spoofing.
/// </summary>
/// <remarks>
/// <para>
/// WEBIRC is used by trusted gateways (web chat clients, bouncers)
/// to pass along the real user's IP address and hostname.
/// </para>
/// <para>
/// Syntax: WEBIRC password gateway hostname ip [:options]
/// </para>
/// <list type="bullet">
///   <item><description>password - The pre-shared secret configured on the server</description></item>
///   <item><description>gateway - The name of the gateway software (e.g., "cgiirc", "kiwiirc")</description></item>
///   <item><description>hostname - The real hostname of the end user</description></item>
///   <item><description>ip - The real IP address of the end user</description></item>
///   <item><description>options - Optional flags like "secure" for TLS connections</description></item>
/// </list>
/// <para>
/// This command must be sent BEFORE NICK/USER during connection registration.
/// It can only be used once per connection.
/// </para>
/// </remarks>
public sealed class WebircHandler : CommandHandlerBase
{
    /// <inheritdoc/>
    public override string Command => "WEBIRC";

    /// <inheritdoc/>
    public override int MinimumParameters => 4;

    /// <inheritdoc/>
    public override bool RequiresRegistration => false;

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        // WEBIRC can only be used before registration completes
        if (context.User.IsRegistered)
        {
            // Silently ignore - don't reveal WEBIRC capability to registered users
            return;
        }

        // WEBIRC can only be applied once per connection
        if (context.User.HasWebircApplied)
        {
            // Silently ignore duplicate WEBIRC
            return;
        }

        var password = context.Message.Parameters[0];
        var gateway = context.Message.Parameters[1];
        var hostname = context.Message.Parameters[2];
        var ipString = context.Message.Parameters[3];

        // Parse options if present (e.g., "secure" flag)
        var options = context.Message.Parameters.Count > 4 ? context.Message.Parameters[4] : null;
        bool claimsSecure = options?.Contains("secure", StringComparison.OrdinalIgnoreCase) == true;

        // Get the WEBIRC validator from DI
        var validator = context.ServiceProvider(typeof(IWebircValidator)) as IWebircValidator;
        if (validator is null)
        {
            // WEBIRC not configured - silently ignore
            return;
        }

        // Get the gateway's IP address (the proxy connecting to us)
        var gatewayIp = context.User.IpAddress.ToString();

        // Validate the WEBIRC block
        var block = validator.ValidateWebirc(password, gatewayIp);
        if (block is null)
        {
            // Invalid password or untrusted gateway - disconnect for security
            var errorMsg = IrcMessage.Create("ERROR", $"WEBIRC not authorized from {gatewayIp}");
            await context.ReplyAsync(errorMsg, cancellationToken);
            await context.Connection.CloseAsync(cancellationToken);
            return;
        }

        // Parse and validate the real IP address
        if (!IPAddress.TryParse(ipString, out var realIp))
        {
            var errorMsg = IrcMessage.Create("ERROR", "Invalid IP address in WEBIRC");
            await context.ReplyAsync(errorMsg, cancellationToken);
            await context.Connection.CloseAsync(cancellationToken);
            return;
        }

        // Validate hostname (basic sanity check)
        if (string.IsNullOrWhiteSpace(hostname) || hostname.Length > 255)
        {
            hostname = realIp.ToString();
        }

        // Apply the WEBIRC information
        if (!context.User.ApplyWebirc(realIp, hostname))
        {
            // This shouldn't happen given our earlier checks, but handle it
            return;
        }

        // Log the WEBIRC application (operators might want to audit this)
        // The actual logging would be done by the caller if needed

        // Note: Host cloaking will be applied later during registration completion
        // if the block.ApplyCloaking is true (handled elsewhere in registration flow)
    }
}
