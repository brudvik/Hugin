using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the TAGMSG command for sending messages with only tags (no text).
/// IRCv3 specification: https://ircv3.net/specs/extensions/message-tags
/// Used for client-to-client features like typing indicators and reactions.
/// </summary>
public sealed class TagmsgHandler : CommandHandlerBase
{
    public override string Command => "TAGMSG";
    public override int MinimumParameters => 1;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        // TAGMSG requires message-tags capability
        if (!context.Capabilities.HasMessageTags)
        {
            // Silently ignore if client doesn't have message-tags capability
            return;
        }

        var target = context.Message.Parameters[0];

        // Must have at least one client tag to send
        var clientTags = context.Message.Tags
            .Where(t => t.Key.StartsWith('+'))
            .ToDictionary(t => t.Key, t => t.Value);

        if (clientTags.Count == 0)
        {
            // No client tags to send, silently ignore
            return;
        }

        // Build the message to send with server-time if enabled
        var tags = new Dictionary<string, string?>(clientTags);
        if (context.Capabilities.HasServerTime)
        {
            tags["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", 
                System.Globalization.CultureInfo.InvariantCulture);
        }

        var outMessage = IrcMessage.CreateFull(tags, context.User.Hostmask.ToString(), "TAGMSG", target);

        // Channel message
        if (target.StartsWith('#') || target.StartsWith('&'))
        {
            if (!ChannelName.TryCreate(target, out var channelName, out _))
            {
                await SendNoSuchChannelAsync(context, target, cancellationToken);
                return;
            }

            var channel = context.Channels.GetByName(channelName);
            if (channel is null)
            {
                await SendNoSuchChannelAsync(context, target, cancellationToken);
                return;
            }

            // Check if user is on channel (for +n mode)
            var isOnChannel = channel.HasMember(context.User.ConnectionId);
            if (channel.Modes.HasFlag(ChannelMode.NoExternalMessages) && !isOnChannel)
            {
                await context.ReplyAsync(
                    IrcNumerics.CannotSendToChannel(context.ServerName, context.User.Nickname.Value, target), 
                    cancellationToken);
                return;
            }

            // Check for moderated channel
            if (channel.Modes.HasFlag(ChannelMode.Moderated))
            {
                var member = channel.GetMember(context.User.ConnectionId);
                if (member is null || !member.CanSpeak)
                {
                    await context.ReplyAsync(
                        IrcNumerics.CannotSendToChannel(context.ServerName, context.User.Nickname.Value, target), 
                        cancellationToken);
                    return;
                }
            }

            // Check for ban
            if (channel.IsBanned(context.User.Hostmask) && !isOnChannel)
            {
                await context.ReplyAsync(
                    IrcNumerics.CannotSendToChannel(context.ServerName, context.User.Nickname.Value, target), 
                    cancellationToken);
                return;
            }

            // Send to channel members that have message-tags capability (except sender unless echo-message)
            var exceptId = context.Capabilities.HasEchoMessage ? null : (Guid?)context.User.ConnectionId;
            await context.Broker.SendToChannelAsync(target, outMessage.ToString(), exceptId, cancellationToken);
        }
        // Private message
        else
        {
            if (!Nickname.TryCreate(target, out var nickname, out _))
            {
                await SendNoSuchNickAsync(context, target, cancellationToken);
                return;
            }

            var targetUser = context.Users.GetByNickname(nickname);
            if (targetUser is null)
            {
                await SendNoSuchNickAsync(context, target, cancellationToken);
                return;
            }

            // Send to target
            await context.Broker.SendToConnectionAsync(targetUser.ConnectionId, outMessage.ToString(), cancellationToken);

            // Echo back if enabled
            if (context.Capabilities.HasEchoMessage)
            {
                await context.ReplyAsync(outMessage, cancellationToken);
            }
        }
    }
}
