using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the TOPIC command.
/// </summary>
public sealed class TopicHandler : CommandHandlerBase
{
    public override string Command => "TOPIC";
    public override int MinimumParameters => 1;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var channelStr = context.Message.Parameters[0];

        if (!ChannelName.TryCreate(channelStr, out var channelName, out _))
        {
            await SendNoSuchChannelAsync(context, channelStr, cancellationToken);
            return;
        }

        var channel = context.Channels.GetByName(channelName);
        if (channel is null)
        {
            await SendNoSuchChannelAsync(context, channelStr, cancellationToken);
            return;
        }

        var nick = context.User.Nickname.Value;

        // Query topic (no second parameter)
        if (context.Message.Parameters.Count == 1)
        {
            if (string.IsNullOrEmpty(channel.Topic))
            {
                await context.ReplyAsync(
                    IrcNumerics.NoTopic(context.ServerName, nick, channelStr),
                    cancellationToken);
            }
            else
            {
                await context.ReplyAsync(
                    IrcNumerics.Topic(context.ServerName, nick, channelStr, channel.Topic),
                    cancellationToken);

                if (channel.TopicSetAt.HasValue && channel.TopicSetBy is not null)
                {
                    await context.ReplyAsync(
                        IrcNumerics.TopicWhoTime(context.ServerName, nick, channelStr,
                            channel.TopicSetBy, channel.TopicSetAt.Value.ToUnixTimeSeconds()),
                        cancellationToken);
                }
            }
            return;
        }

        // Setting topic
        var newTopic = context.Message.Parameters[1];

        // Check if user is on channel
        var member = channel.GetMember(context.User.ConnectionId);
        if (member is null)
        {
            await SendNotOnChannelAsync(context, channelStr, cancellationToken);
            return;
        }

        // Check if channel has +t (topic protected) mode
        if (channel.Modes.HasFlag(ChannelMode.TopicProtected))
        {
            if (!member.IsHalfOpOrHigher && !context.User.IsOperator)
            {
                await SendChanOpPrivsNeededAsync(context, channelStr, cancellationToken);
                return;
            }
        }

        // Set the topic
        channel.SetTopic(newTopic, context.User.Hostmask.ToString());

        // Broadcast TOPIC message to channel
        var topicMsg = IrcMessage.Create(context.User.Hostmask.ToString(), "TOPIC", channelStr, newTopic);
        if (context.Capabilities.HasServerTime)
        {
            topicMsg = topicMsg.WithTags(new Dictionary<string, string?>
            {
                ["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        await context.Broker.SendToChannelAsync(channelStr, topicMsg.ToString(), null, cancellationToken);
    }
}

/// <summary>
/// Handles the AWAY command.
/// </summary>
public sealed class AwayHandler : CommandHandlerBase
{
    public override string Command => "AWAY";

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User.Nickname.Value;

        if (context.Message.Parameters.Count == 0 || string.IsNullOrEmpty(context.Message.Parameters[0]))
        {
            // Unset away
            context.User.SetBack();
            await context.ReplyAsync(
                IrcNumerics.UnAway(context.ServerName, nick),
                cancellationToken);

            // Send away-notify to users in shared channels
            if (context.Capabilities.HasAwayNotify)
            {
                await BroadcastAwayNotifyAsync(context, null, cancellationToken);
            }
        }
        else
        {
            // Set away with message
            var awayMessage = context.Message.Parameters[0];
            context.User.SetAway(awayMessage);
            await context.ReplyAsync(
                IrcNumerics.NowAway(context.ServerName, nick),
                cancellationToken);

            // Send away-notify to users in shared channels
            if (context.Capabilities.HasAwayNotify)
            {
                await BroadcastAwayNotifyAsync(context, awayMessage, cancellationToken);
            }
        }
    }

    private static async ValueTask BroadcastAwayNotifyAsync(CommandContext context, string? awayMessage, CancellationToken cancellationToken)
    {
        // Broadcast AWAY message to all channels the user is in
        var channelNames = context.User.Channels.Keys.Select(c => c.Value).ToList();
        if (channelNames.Count == 0) return;

        var awayMsg = awayMessage is null
            ? IrcMessage.Create(context.User.Hostmask.ToString(), "AWAY")
            : IrcMessage.Create(context.User.Hostmask.ToString(), "AWAY", awayMessage);

        if (context.Capabilities.HasServerTime)
        {
            awayMsg = awayMsg.WithTags(new Dictionary<string, string?>
            {
                ["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        // Send to all channels (except self)
        await context.Broker.SendToChannelsAsync(channelNames, awayMsg.ToString(), context.User.ConnectionId, cancellationToken);
    }
}

/// <summary>
/// Handles the INVITE command.
/// </summary>
public sealed class InviteHandler : CommandHandlerBase
{
    public override string Command => "INVITE";
    public override int MinimumParameters => 2;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var targetNick = context.Message.Parameters[0];
        var channelStr = context.Message.Parameters[1];

        if (!ChannelName.TryCreate(channelStr, out var channelName, out _))
        {
            await SendNoSuchChannelAsync(context, channelStr, cancellationToken);
            return;
        }

        if (!Nickname.TryCreate(targetNick, out var nickname, out _))
        {
            await SendNoSuchNickAsync(context, targetNick, cancellationToken);
            return;
        }

        var targetUser = context.Users.GetByNickname(nickname);
        if (targetUser is null)
        {
            await SendNoSuchNickAsync(context, targetNick, cancellationToken);
            return;
        }

        var channel = context.Channels.GetByName(channelName);
        var nick = context.User.Nickname.Value;

        // Check if inviter is on channel
        if (channel is not null)
        {
            var member = channel.GetMember(context.User.ConnectionId);
            if (member is null)
            {
                await SendNotOnChannelAsync(context, channelStr, cancellationToken);
                return;
            }

            // Check if channel is invite-only and inviter has privileges
            if (channel.Modes.HasFlag(ChannelMode.InviteOnly))
            {
                if (!member.IsHalfOpOrHigher && !context.User.IsOperator)
                {
                    await SendChanOpPrivsNeededAsync(context, channelStr, cancellationToken);
                    return;
                }
            }

            // Check if target is already on channel
            if (channel.HasMember(targetUser.ConnectionId))
            {
                await context.ReplyAsync(
                    IrcNumerics.UserOnChannel(context.ServerName, nick, targetNick, channelStr),
                    cancellationToken);
                return;
            }

            // Add invitation
            channel.AddInvitation(targetUser.ConnectionId);
        }

        // Send RPL_INVITING to inviter
        await context.ReplyAsync(
            IrcNumerics.Inviting(context.ServerName, nick, targetNick, channelStr),
            cancellationToken);

        // Send INVITE to target
        var inviteMsg = IrcMessage.Create(context.User.Hostmask.ToString(), "INVITE", targetNick, channelStr);
        if (context.Capabilities.HasServerTime)
        {
            inviteMsg = inviteMsg.WithTags(new Dictionary<string, string?>
            {
                ["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture)
            });
        }
        await context.Broker.SendToConnectionAsync(targetUser.ConnectionId, inviteMsg.ToString(), cancellationToken);

        // Send invite-notify to channel members with the capability
        if (channel is not null && context.Capabilities.HasInviteNotify)
        {
            // The invite is already sent to the target, now broadcast to channel
            await context.Broker.SendToChannelAsync(channelStr, inviteMsg.ToString(), targetUser.ConnectionId, cancellationToken);
        }

        // Check if target is away
        if (targetUser.IsAway)
        {
            await context.ReplyAsync(
                IrcNumerics.Away(context.ServerName, nick, targetNick, targetUser.AwayMessage!),
                cancellationToken);
        }
    }
}

/// <summary>
/// Handles the MOTD command.
/// </summary>
public sealed class MotdHandler : CommandHandlerBase
{
    public override string Command => "MOTD";

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User.Nickname.Value;

        // Get MOTD from configuration (for now, use a default)
        var motdLines = GetMotdLines(context);

        if (motdLines.Count == 0)
        {
            await context.ReplyAsync(
                IrcNumerics.NoMotd(context.ServerName, nick),
                cancellationToken);
            return;
        }

        await context.ReplyAsync(
            IrcNumerics.MotdStart(context.ServerName, nick, context.ServerName),
            cancellationToken);

        foreach (var line in motdLines)
        {
            await context.ReplyAsync(
                IrcNumerics.Motd(context.ServerName, nick, line),
                cancellationToken);
        }

        await context.ReplyAsync(
            IrcNumerics.EndOfMotd(context.ServerName, nick),
            cancellationToken);
    }

    private static List<string> GetMotdLines(CommandContext context)
    {
        // Default MOTD - could be loaded from configuration
        return new List<string>
        {
            $"Welcome to {context.ServerName}",
            "",
            "Hugin IRC Server - A modern, secure IRC server",
            "https://github.com/your-username/hugin",
            "",
            "Please behave responsibly and follow the network rules.",
            "",
            "Enjoy your stay!"
        };
    }
}

/// <summary>
/// Handles the LUSERS command.
/// </summary>
public sealed class LusersHandler : CommandHandlerBase
{
    public override string Command => "LUSERS";

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User.Nickname.Value;

        // Gather statistics
        int totalUsers = 0;
        int invisibleUsers = 0;
        int operators = 0;
        int channels = 0;

        foreach (var user in context.Users.GetAll())
        {
            totalUsers++;
            if (user.Modes.HasFlag(UserMode.Invisible))
            {
                invisibleUsers++;
            }
            if (user.IsOperator)
            {
                operators++;
            }
        }

        channels = context.Channels.GetAll().Count();

        int visibleUsers = totalUsers - invisibleUsers;

        // 251 RPL_LUSERCLIENT
        await context.ReplyAsync(
            IrcNumerics.LuserClient(context.ServerName, nick, visibleUsers, invisibleUsers, 1),
            cancellationToken);

        // 252 RPL_LUSEROP (only if there are operators)
        if (operators > 0)
        {
            await context.ReplyAsync(
                IrcNumerics.LuserOp(context.ServerName, nick, operators),
                cancellationToken);
        }

        // 254 RPL_LUSERCHANNELS
        await context.ReplyAsync(
            IrcNumerics.LuserChannels(context.ServerName, nick, channels),
            cancellationToken);

        // 255 RPL_LUSERME
        await context.ReplyAsync(
            IrcNumerics.LuserMe(context.ServerName, nick, totalUsers, 0),
            cancellationToken);

        // 265 RPL_LOCALUSERS
        await context.ReplyAsync(
            IrcNumerics.LocalUsers(context.ServerName, nick, totalUsers, totalUsers),
            cancellationToken);

        // 266 RPL_GLOBALUSERS
        await context.ReplyAsync(
            IrcNumerics.GlobalUsers(context.ServerName, nick, totalUsers, totalUsers),
            cancellationToken);
    }
}

/// <summary>
/// Handles the PASS command.
/// </summary>
public sealed class PassHandler : CommandHandlerBase
{
    public override string Command => "PASS";
    public override int MinimumParameters => 1;
    public override bool RequiresRegistration => false;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        // PASS must be sent before NICK and USER
        if (context.User.RegistrationState != RegistrationState.None)
        {
            await context.ReplyAsync(
                IrcNumerics.AlreadyRegistered(context.ServerName, context.User.Nickname?.Value ?? "*"),
                cancellationToken);
            return;
        }

        // Store the password for later verification during registration
        // In a real implementation, this would be stored on the connection/user object
        // and verified when completing registration
        // For now, we just acknowledge receipt - the server config would need to have
        // a server password configured for this to be meaningful

        // The password is in Parameters[0]
        // Could store on User or a session object for later verification
        context.User.UpdateLastActivity();
    }
}
