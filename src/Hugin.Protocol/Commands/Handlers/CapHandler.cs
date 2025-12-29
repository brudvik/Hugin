using Hugin.Core.Enums;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the CAP command for IRCv3 capability negotiation.
/// </summary>
public sealed class CapHandler : CommandHandlerBase
{
    public override string Command => "CAP";
    public override int MinimumParameters => 1;
    public override bool RequiresRegistration => false;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var subcommand = context.Message.Parameters[0].ToUpperInvariant();
        var nick = context.User.Nickname?.Value ?? "*";

        switch (subcommand)
        {
            case "LS":
                await HandleLsAsync(context, nick, cancellationToken);
                break;

            case "LIST":
                await HandleListAsync(context, nick, cancellationToken);
                break;

            case "REQ":
                await HandleReqAsync(context, nick, cancellationToken);
                break;

            case "END":
                await HandleEndAsync(context, cancellationToken);
                break;

            default:
                await context.ReplyAsync(
                    IrcNumerics.InvalidCapCmd(context.ServerName, nick, subcommand), cancellationToken);
                break;
        }
    }

    private static async ValueTask HandleLsAsync(CommandContext context, string nick, CancellationToken cancellationToken)
    {
        context.Capabilities.IsNegotiating = true;

        if (context.User.RegistrationState == RegistrationState.None)
        {
            context.User.SetRegistrationState(RegistrationState.CapNegotiating);
        }

        // Check for CAP 302 (version)
        bool is302 = context.Message.Parameters.Count > 1 && context.Message.Parameters[1] == "302";

        var capList = CapabilityManager.GetCapabilityList(is302);

        // If the list is too long, we need to use multiline
        // For now, assume it fits in one message
        await context.ReplyAsync(
            IrcMessage.CreateWithSource(context.ServerName, "CAP", nick, "LS", capList), cancellationToken);
    }

    private static async ValueTask HandleListAsync(CommandContext context, string nick, CancellationToken cancellationToken)
    {
        var enabledCaps = string.Join(" ", context.Capabilities.EnabledCapabilities);
        await context.ReplyAsync(
            IrcMessage.CreateWithSource(context.ServerName, "CAP", nick, "LIST", enabledCaps), cancellationToken);
    }

    private static async ValueTask HandleReqAsync(CommandContext context, string nick, CancellationToken cancellationToken)
    {
        if (context.Message.Parameters.Count < 2)
        {
            await SendNeedMoreParamsAsync(context, cancellationToken);
            return;
        }

        var requested = context.Message.Parameters[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var acked = new List<string>();
        var naked = new List<string>();

        foreach (var cap in requested)
        {
            bool removing = cap.StartsWith('-');
            var capName = removing ? cap[1..] : cap;

            if (removing)
            {
                if (context.Capabilities.Disable(capName))
                {
                    acked.Add(cap);
                }
                else
                {
                    naked.Add(cap);
                }
            }
            else
            {
                if (context.Capabilities.Enable(capName))
                {
                    acked.Add(capName);
                }
                else
                {
                    naked.Add(capName);
                }
            }
        }

        if (naked.Count > 0)
        {
            // NAK the entire request
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "CAP", nick, "NAK", string.Join(" ", requested)), cancellationToken);
        }
        else
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "CAP", nick, "ACK", string.Join(" ", acked)), cancellationToken);
        }
    }

    private static ValueTask HandleEndAsync(CommandContext context, CancellationToken cancellationToken)
    {
        context.Capabilities.IsNegotiating = false;

        // Check if registration can complete
        if (context.User.RegistrationState == RegistrationState.CapNegotiating)
        {
            // Need to wait for NICK and USER
            context.User.SetRegistrationState(RegistrationState.None);
        }

        return ValueTask.CompletedTask;
    }
}
