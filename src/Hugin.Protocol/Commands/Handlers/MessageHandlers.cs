using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the PRIVMSG command.
/// </summary>
public sealed class PrivmsgHandler : CommandHandlerBase
{
    public override string Command => "PRIVMSG";
    public override int MinimumParameters => 2;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var target = context.Message.Parameters[0];
        var text = context.Message.Parameters[1];

        if (string.IsNullOrEmpty(text))
        {
            await context.ReplyAsync(IrcNumerics.NoTextToSend(context.ServerName, context.User.Nickname.Value), cancellationToken);
            return;
        }

        // Build the message to send
        var tags = new Dictionary<string, string?>();
        if (context.Capabilities.HasServerTime)
        {
            tags["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Forward client tags if message-tags is enabled
        if (context.Capabilities.HasMessageTags)
        {
            foreach (var (key, value) in context.Message.Tags)
            {
                // Only forward client-to-client tags (starting with +)
                if (key.StartsWith('+'))
                {
                    tags[key] = value;
                }
            }
        }

        var outMessage = IrcMessage.CreateFull(tags, context.User.Hostmask.ToString(), "PRIVMSG", target, text);

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
            bool isOnChannel = channel.HasMember(context.User.ConnectionId);
            if (channel.Modes.HasFlag(ChannelMode.NoExternalMessages) && !isOnChannel)
            {
                await context.ReplyAsync(IrcNumerics.CannotSendToChannel(context.ServerName, context.User.Nickname.Value, target), cancellationToken);
                return;
            }

            // Check for moderated channel
            if (channel.Modes.HasFlag(ChannelMode.Moderated))
            {
                var member = channel.GetMember(context.User.ConnectionId);
                if (member is null || !member.CanSpeak)
                {
                    await context.ReplyAsync(IrcNumerics.CannotSendToChannel(context.ServerName, context.User.Nickname.Value, target), cancellationToken);
                    return;
                }
            }

            // Check for ban
            if (channel.IsBanned(context.User.Hostmask) && !isOnChannel)
            {
                await context.ReplyAsync(IrcNumerics.CannotSendToChannel(context.ServerName, context.User.Nickname.Value, target), cancellationToken);
                return;
            }

            // Check for +R (registered users only)
            if (channel.Modes.HasFlag(ChannelMode.RegisteredOnly) && string.IsNullOrEmpty(context.User.Account))
            {
                await context.ReplyAsync(IrcNumerics.CannotSendToChannel(context.ServerName, context.User.Nickname.Value, target), cancellationToken);
                return;
            }

            // Check for +C (no CTCP) - allow ACTION
            if (channel.Modes.HasFlag(ChannelMode.NoCTCP) && MessageFilter.IsCtcp(text) && !MessageFilter.IsCtcpAction(text))
            {
                await context.ReplyAsync(IrcNumerics.CannotSendToChannel(context.ServerName, context.User.Nickname.Value, target), cancellationToken);
                return;
            }

            // Check for +c (no colors)
            if (channel.Modes.HasFlag(ChannelMode.NoColors) && MessageFilter.ContainsColorCodes(text))
            {
                await context.ReplyAsync(IrcNumerics.CannotSendToChannel(context.ServerName, context.User.Nickname.Value, target), cancellationToken);
                return;
            }

            // Apply +S (strip colors) if enabled
            var messageText = text;
            if (channel.Modes.HasFlag(ChannelMode.StripColors))
            {
                messageText = MessageFilter.StripColorCodes(text);
            }

            // Rebuild message with potentially stripped text
            if (messageText != text)
            {
                outMessage = IrcMessage.CreateFull(tags, context.User.Hostmask.ToString(), "PRIVMSG", target, messageText);
            }

            // Send to channel (except sender, unless echo-message is enabled)
            var exceptId = context.Capabilities.HasEchoMessage ? null : (Guid?)context.User.ConnectionId;
            await context.Broker.SendToChannelAsync(target, outMessage.ToString(), exceptId, cancellationToken);

            // Echo back to sender if echo-message is enabled
            if (context.Capabilities.HasEchoMessage)
            {
                // Message already sent to channel including sender
            }
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

            // Check if target is away
            if (targetUser.IsAway)
            {
                await context.ReplyAsync(IrcNumerics.Away(context.ServerName, context.User.Nickname.Value, targetUser.Nickname.Value, targetUser.AwayMessage!), cancellationToken);
            }

            // Send to target
            await context.Broker.SendToConnectionAsync(targetUser.ConnectionId, outMessage.ToString(), cancellationToken);

            // Echo back if enabled
            if (context.Capabilities.HasEchoMessage)
            {
                await context.ReplyAsync(outMessage, cancellationToken);
            }
        }

        context.User.UpdateLastActivity();
    }
}

/// <summary>
/// Handles the NOTICE command.
/// </summary>
public sealed class NoticeHandler : CommandHandlerBase
{
    public override string Command => "NOTICE";
    public override int MinimumParameters => 2;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var target = context.Message.Parameters[0];
        var text = context.Message.Parameters[1];

        // NOTICE should not generate automatic replies per RFC
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var tags = new Dictionary<string, string?>();
        if (context.Capabilities.HasServerTime)
        {
            tags["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
        }

        var outMessage = IrcMessage.CreateFull(tags, context.User.Hostmask.ToString(), "NOTICE", target, text);

        // Channel notice
        if (target.StartsWith('#') || target.StartsWith('&'))
        {
            if (ChannelName.TryCreate(target, out var channelName, out _))
            {
                var channel = context.Channels.GetByName(channelName);
                if (channel is not null)
                {
                    // Check for +R (registered users only)
                    if (channel.Modes.HasFlag(ChannelMode.RegisteredOnly) && string.IsNullOrEmpty(context.User.Account))
                    {
                        return; // NOTICE should not generate error replies
                    }

                    // Check for +c (no colors)
                    if (channel.Modes.HasFlag(ChannelMode.NoColors) && MessageFilter.ContainsColorCodes(text))
                    {
                        return;
                    }

                    // Apply +S (strip colors) if enabled
                    var messageText = text;
                    if (channel.Modes.HasFlag(ChannelMode.StripColors))
                    {
                        messageText = MessageFilter.StripColorCodes(text);
                        outMessage = IrcMessage.CreateFull(tags, context.User.Hostmask.ToString(), "NOTICE", target, messageText);
                    }

                    var exceptId = context.Capabilities.HasEchoMessage ? null : (Guid?)context.User.ConnectionId;
                    await context.Broker.SendToChannelAsync(target, outMessage.ToString(), exceptId, cancellationToken);
                }
            }
        }
        // Private notice
        else
        {
            if (Nickname.TryCreate(target, out var nickname, out _))
            {
                var targetUser = context.Users.GetByNickname(nickname);
                if (targetUser is not null)
                {
                    await context.Broker.SendToConnectionAsync(targetUser.ConnectionId, outMessage.ToString(), cancellationToken);

                    if (context.Capabilities.HasEchoMessage)
                    {
                        await context.ReplyAsync(outMessage, cancellationToken);
                    }
                }
            }
        }

        context.User.UpdateLastActivity();
    }
}
