using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the NICK command.
/// </summary>
public sealed class NickHandler : CommandHandlerBase
{
    public override string Command => "NICK";
    public override int MinimumParameters => 1;
    public override bool RequiresRegistration => false;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var newNick = context.Message.Parameters[0];

        if (!Nickname.TryCreate(newNick, out var nickname, out _))
        {
            await context.ReplyAsync(
                IrcNumerics.ErroneusNickname(context.ServerName,
                    context.User.Nickname?.Value ?? "*", newNick), cancellationToken);
            return;
        }

        // Check if nickname is in use
        if (context.Users.IsNicknameInUse(nickname))
        {
            var existingUser = context.Users.GetByNickname(nickname);
            if (existingUser?.ConnectionId != context.User.ConnectionId)
            {
                await context.ReplyAsync(
                    IrcNumerics.NicknameInUse(context.ServerName,
                        context.User.Nickname?.Value ?? "*", newNick), cancellationToken);
                return;
            }
        }

        var oldNick = context.User.Nickname?.Value;
        var oldHostmask = context.User.Nickname is not null ? context.User.Hostmask.ToString() : null;

        // Update nickname
        context.User.SetNickname(nickname);

        // Update registration state if needed
        if (context.User.RegistrationState == RegistrationState.None)
        {
            context.User.SetRegistrationState(RegistrationState.NickReceived);
        }
        else if (context.User.RegistrationState == RegistrationState.UserReceived)
        {
            context.User.SetRegistrationState(RegistrationState.NickAndUserReceived);
        }

        // If already registered, notify all relevant users
        if (context.User.IsRegistered && oldHostmask is not null)
        {
            var nickMsg = IrcMessage.CreateWithSource(oldHostmask, "NICK", newNick);
            if (context.Capabilities.HasServerTime)
            {
                nickMsg = nickMsg.WithTags(new Dictionary<string, string?>
                {
                    ["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture)
                });
            }

            // Send to all channels the user is in
            var channelNames = context.User.Channels.Keys.Select(c => c.Value).ToList();
            if (channelNames.Count > 0)
            {
                await context.Broker.SendToChannelsAsync(channelNames, nickMsg.ToString(), null, cancellationToken);
            }
            else
            {
                // Just send to the user themselves
                await context.ReplyAsync(nickMsg, cancellationToken);
            }

            // Update nickname in all channels
            foreach (var channelName in context.User.Channels.Keys)
            {
                var channel = context.Channels.GetByName(channelName);
                channel?.UpdateMemberNickname(context.User.ConnectionId, nickname);
            }

            // Broadcast nick change event to admin clients
            var userEventNotifier = context.ServiceProvider(typeof(Core.Interfaces.IUserEventNotifier)) as Core.Interfaces.IUserEventNotifier;
            if (userEventNotifier is not null && oldNick is not null)
            {
                await userEventNotifier.OnNickChangeAsync(oldNick, newNick, context.User.DisplayedHostname, context.User.ConnectionId.ToString(), cancellationToken);
            }
        }
    }
}

/// <summary>
/// Handles the USER command.
/// </summary>
public sealed class UserHandler : CommandHandlerBase
{
    public override string Command => "USER";
    public override int MinimumParameters => 4;
    public override bool RequiresRegistration => false;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        if (context.User.IsRegistered)
        {
            await context.ReplyAsync(
                IrcNumerics.AlreadyRegistered(context.ServerName, context.User.Nickname?.Value ?? "*"), cancellationToken);
            return;
        }

        var username = context.Message.Parameters[0];
        // Parameters[1] is mode (unused in modern IRC)
        // Parameters[2] is unused
        var realname = context.Message.Parameters[3];

        // Sanitize username (ident)
        if (username.Length > 10)
        {
            username = username[..10];
        }
        username = new string(username.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
        if (string.IsNullOrEmpty(username))
        {
            username = "unknown";
        }

        context.User.SetUserInfo(username, realname);

        // Update registration state
        if (context.User.RegistrationState == RegistrationState.None)
        {
            context.User.SetRegistrationState(RegistrationState.UserReceived);
        }
        else if (context.User.RegistrationState == RegistrationState.NickReceived)
        {
            context.User.SetRegistrationState(RegistrationState.NickAndUserReceived);
        }
        else if (context.User.RegistrationState == RegistrationState.CapNegotiating)
        {
            // Still negotiating CAP, just record the info
        }
    }
}

/// <summary>
/// Handles the QUIT command.
/// </summary>
public sealed class QuitHandler : CommandHandlerBase
{
    public override string Command => "QUIT";
    public override bool RequiresRegistration => false;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var reason = context.Message.Parameters.Count > 0
            ? context.Message.Parameters[0]
            : "Client quit";

        var quitMsg = IrcMessage.Create(context.User.Hostmask.ToString(), "QUIT", reason);
        if (context.Capabilities.HasServerTime)
        {
            quitMsg = quitMsg.WithTags(new Dictionary<string, string?>
            {
                ["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        // Notify all channels
        var channelNames = context.User.Channels.Keys.Select(c => c.Value).ToList();
        if (channelNames.Count > 0)
        {
            await context.Broker.SendToChannelsAsync(channelNames, quitMsg.ToString(), context.User.ConnectionId, cancellationToken);
        }

        // Remove user from all channels
        foreach (var channelName in context.User.Channels.Keys.ToList())
        {
            var channel = context.Channels.GetByName(channelName);
            if (channel is not null)
            {
                channel.RemoveMember(context.User.ConnectionId);
                if (channel.IsEmpty)
                {
                    context.Channels.Remove(channelName);
                }
            }
        }

        // Remove user
        context.Users.Remove(context.User.ConnectionId);

        // Broadcast disconnect event to admin clients
        var userEventNotifier = context.ServiceProvider(typeof(Core.Interfaces.IUserEventNotifier)) as Core.Interfaces.IUserEventNotifier;
        if (userEventNotifier is not null)
        {
            await userEventNotifier.OnUserDisconnectedAsync(
                context.User.Nickname?.Value ?? "unknown", 
                context.User.DisplayedHostname, 
                reason, 
                context.User.ConnectionId.ToString(), 
                cancellationToken);
        }

        // Close connection
        await context.Connection.CloseAsync(cancellationToken);
    }
}

/// <summary>
/// Handles the PING command.
/// </summary>
public sealed class PingHandler : CommandHandlerBase
{
    public override string Command => "PING";
    public override int MinimumParameters => 1;
    public override bool RequiresRegistration => false;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var token = context.Message.Parameters[0];
        await context.ReplyAsync(IrcMessage.CreateWithSource(context.ServerName, "PONG", context.ServerName, token), cancellationToken);
    }
}

/// <summary>
/// Handles the PONG command.
/// </summary>
public sealed class PongHandler : CommandHandlerBase
{
    public override string Command => "PONG";
    public override bool RequiresRegistration => false;

    public override ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        // Just update last activity
        context.User.UpdateLastActivity();
        return ValueTask.CompletedTask;
    }
}
