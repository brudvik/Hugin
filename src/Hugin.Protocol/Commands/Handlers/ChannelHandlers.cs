using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the JOIN command.
/// </summary>
public sealed class JoinHandler : CommandHandlerBase
{
    public override string Command => "JOIN";
    public override int MinimumParameters => 1;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var channels = context.Message.Parameters[0].Split(',');
        var keys = context.Message.Parameters.Count > 1
            ? context.Message.Parameters[1].Split(',')
            : Array.Empty<string>();

        for (int i = 0; i < channels.Length; i++)
        {
            var channelStr = channels[i].Trim();
            var key = i < keys.Length ? keys[i] : null;

            await JoinChannelAsync(context, channelStr, key, cancellationToken);
        }
    }

    private static async ValueTask JoinChannelAsync(CommandContext context, string channelStr, string? key, CancellationToken cancellationToken)
    {
        if (!ChannelName.TryCreate(channelStr, out var channelName, out _))
        {
            await context.ReplyAsync(IrcNumerics.BadChannelMask(context.ServerName, context.User.Nickname.Value, channelStr), cancellationToken);
            return;
        }

        // Check if already on channel
        if (context.User.Channels.ContainsKey(channelName))
        {
            return;
        }

        var channel = context.Channels.GetByName(channelName);
        bool isNewChannel = channel is null;

        if (isNewChannel)
        {
            // Create new channel
            channel = context.Channels.Create(channelName);
        }
        else
        {
            // Check channel limits and restrictions

            // +i (invite only)
            if (channel!.Modes.HasFlag(ChannelMode.InviteOnly))
            {
                bool canJoin = channel.IsInvited(context.User.ConnectionId) ||
                               channel.HasInviteException(context.User.Hostmask);
                if (!canJoin)
                {
                    await context.ReplyAsync(
                        IrcNumerics.InviteOnlyChan(context.ServerName, context.User.Nickname.Value, channelStr), cancellationToken);
                    return;
                }
            }

            // +k (key)
            if (channel.Modes.HasFlag(ChannelMode.Key))
            {
                if (key != channel.Key)
                {
                    await context.ReplyAsync(
                        IrcNumerics.BadChannelKey(context.ServerName, context.User.Nickname.Value, channelStr), cancellationToken);
                    return;
                }
            }

            // +b (ban)
            if (channel.IsBanned(context.User.Hostmask))
            {
                await context.ReplyAsync(
                    IrcNumerics.BannedFromChan(context.ServerName, context.User.Nickname.Value, channelStr), cancellationToken);
                return;
            }

            // +l (limit)
            if (channel.Modes.HasFlag(ChannelMode.Limit) && channel.UserLimit.HasValue)
            {
                if (channel.MemberCount >= channel.UserLimit.Value)
                {
                    await context.ReplyAsync(
                        IrcNumerics.ChannelIsFull(context.ServerName, context.User.Nickname.Value, channelStr), cancellationToken);
                    return;
                }
            }

            // +R (registered users only)
            if (channel.Modes.HasFlag(ChannelMode.RegisteredOnly) && string.IsNullOrEmpty(context.User.Account))
            {
                // Use ERR_NEEDREGGEDNICK (477) - channel requires registered nickname
                await context.ReplyAsync(
                    IrcNumerics.NeedReggedNick(context.ServerName, context.User.Nickname.Value, channelStr), cancellationToken);
                return;
            }
        }

        // Add user to channel
        var mode = isNewChannel ? ChannelMemberMode.Op : ChannelMemberMode.None;
        channel!.AddMember(context.User, mode);
        context.User.JoinChannel(channelName, mode);

        // Build JOIN message
        var joinMsg = BuildJoinMessage(context);

        // Send JOIN to all channel members
        await context.Broker.SendToChannelAsync(channelStr, joinMsg.ToString(), null, cancellationToken);

        // Send topic
        if (!string.IsNullOrEmpty(channel.Topic))
        {
            await context.ReplyAsync(
                IrcNumerics.Topic(context.ServerName, context.User.Nickname.Value, channelStr, channel.Topic), cancellationToken);
            if (channel.TopicSetAt.HasValue)
            {
                await context.ReplyAsync(
                    IrcNumerics.TopicWhoTime(context.ServerName, context.User.Nickname.Value, channelStr,
                        channel.TopicSetBy!, channel.TopicSetAt.Value.ToUnixTimeSeconds()), cancellationToken);
            }
        }

        // Send NAMES list
        await SendNamesAsync(context, channel, cancellationToken);
    }

    private static IrcMessage BuildJoinMessage(CommandContext context)
    {
        var tags = new Dictionary<string, string?>();
        if (context.Capabilities.HasServerTime)
        {
            tags["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
        }
        if (context.Capabilities.HasAccountTag && context.User.Account is not null)
        {
            tags["account"] = context.User.Account;
        }

        // Extended JOIN format: JOIN #channel accountname :Real Name
        if (context.Capabilities.HasExtendedJoin)
        {
            var account = context.User.Account ?? "*";
            return IrcMessage.CreateFull(tags, context.User.Hostmask.ToString(), "JOIN",
                context.Message.Parameters[0], account, context.User.RealName);
        }

        return IrcMessage.CreateFull(tags, context.User.Hostmask.ToString(), "JOIN", context.Message.Parameters[0]);
    }

    private static async ValueTask SendNamesAsync(CommandContext context, Core.Entities.Channel channel, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        foreach (var member in channel.Members.Values)
        {
            string prefix = GetMemberPrefix(member.Modes, context.Capabilities.HasMultiPrefix);
            string nick;

            if (context.Capabilities.HasUserhostInNames)
            {
                var user = context.Users.GetByConnectionId(member.ConnectionId);
                nick = user is not null
                    ? $"{prefix}{user.Hostmask}"
                    : $"{prefix}{member.Nickname.Value}";
            }
            else
            {
                nick = $"{prefix}{member.Nickname.Value}";
            }
            names.Add(nick);
        }

        // Split names into 400-byte chunks to stay within message limits
        var namesLine = string.Join(" ", names);
        string channelType = channel.Modes.HasFlag(ChannelMode.Secret) ? "@" : "=";

        await context.ReplyAsync(
            IrcNumerics.NamReply(context.ServerName, context.User.Nickname.Value, channelType, channel.Name.Value, namesLine), cancellationToken);
        await context.ReplyAsync(
            IrcNumerics.EndOfNames(context.ServerName, context.User.Nickname.Value, channel.Name.Value), cancellationToken);
    }

    private static string GetMemberPrefix(ChannelMemberMode mode, bool multiPrefix)
    {
        if (multiPrefix)
        {
            var prefixes = new List<char>();
            if (mode.HasFlag(ChannelMemberMode.Owner)) prefixes.Add('~');
            if (mode.HasFlag(ChannelMemberMode.Admin)) prefixes.Add('&');
            if (mode.HasFlag(ChannelMemberMode.Op)) prefixes.Add('@');
            if (mode.HasFlag(ChannelMemberMode.HalfOp)) prefixes.Add('%');
            if (mode.HasFlag(ChannelMemberMode.Voice)) prefixes.Add('+');
            return new string(prefixes.ToArray());
        }
        else
        {
            // Return highest prefix only
            if (mode.HasFlag(ChannelMemberMode.Owner)) return "~";
            if (mode.HasFlag(ChannelMemberMode.Admin)) return "&";
            if (mode.HasFlag(ChannelMemberMode.Op)) return "@";
            if (mode.HasFlag(ChannelMemberMode.HalfOp)) return "%";
            if (mode.HasFlag(ChannelMemberMode.Voice)) return "+";
            return "";
        }
    }
}

/// <summary>
/// Handles the PART command.
/// </summary>
public sealed class PartHandler : CommandHandlerBase
{
    public override string Command => "PART";
    public override int MinimumParameters => 1;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var channels = context.Message.Parameters[0].Split(',');
        var reason = context.Message.Parameters.Count > 1 ? context.Message.Parameters[1] : null;

        foreach (var channelStr in channels)
        {
            await PartChannelAsync(context, channelStr.Trim(), reason, cancellationToken);
        }
    }

    private static async ValueTask PartChannelAsync(CommandContext context, string channelStr, string? reason, CancellationToken cancellationToken)
    {
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

        if (!channel.HasMember(context.User.ConnectionId))
        {
            await SendNotOnChannelAsync(context, channelStr, cancellationToken);
            return;
        }

        // Build PART message
        var partMsg = reason is not null
            ? IrcMessage.Create(context.User.Hostmask.ToString(), "PART", channelStr, reason)
            : IrcMessage.Create(context.User.Hostmask.ToString(), "PART", channelStr);

        if (context.Capabilities.HasServerTime)
        {
            partMsg = partMsg.WithTags(new Dictionary<string, string?>
            {
                ["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        // Send PART to all channel members (including the user parting)
        await context.Broker.SendToChannelAsync(channelStr, partMsg.ToString(), null, cancellationToken);

        // Remove user from channel
        channel.RemoveMember(context.User.ConnectionId);
        context.User.PartChannel(channelName);

        // Remove empty channel
        if (channel.IsEmpty)
        {
            context.Channels.Remove(channelName);
        }
    }
}

/// <summary>
/// Handles the KICK command.
/// </summary>
public sealed class KickHandler : CommandHandlerBase
{
    public override string Command => "KICK";
    public override int MinimumParameters => 2;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var channelStr = context.Message.Parameters[0];
        var targetNick = context.Message.Parameters[1];
        var reason = context.Message.Parameters.Count > 2 ? context.Message.Parameters[2] : context.User.Nickname.Value;

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

        // Check if kicker is on channel
        var kicker = channel.GetMember(context.User.ConnectionId);
        if (kicker is null)
        {
            await SendNotOnChannelAsync(context, channelStr, cancellationToken);
            return;
        }

        // Check if kicker has privileges
        if (!kicker.IsHalfOpOrHigher && !context.User.IsOperator)
        {
            await SendChanOpPrivsNeededAsync(context, channelStr, cancellationToken);
            return;
        }

        // Find target user
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

        var targetMember = channel.GetMember(targetUser.ConnectionId);
        if (targetMember is null)
        {
            await context.ReplyAsync(
                IrcNumerics.UserNotInChannel(context.ServerName, context.User.Nickname.Value, targetNick, channelStr), cancellationToken);
            return;
        }

        // Check if target has higher privileges
        if (targetMember.Modes > kicker.Modes && !context.User.IsOperator)
        {
            await SendChanOpPrivsNeededAsync(context, channelStr, cancellationToken);
            return;
        }

        // Build KICK message
        var kickMsg = IrcMessage.Create(context.User.Hostmask.ToString(), "KICK", channelStr, targetNick, reason);
        if (context.Capabilities.HasServerTime)
        {
            kickMsg = kickMsg.WithTags(new Dictionary<string, string?>
            {
                ["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        // Send KICK to all channel members
        await context.Broker.SendToChannelAsync(channelStr, kickMsg.ToString(), null, cancellationToken);

        // Remove target from channel
        channel.RemoveMember(targetUser.ConnectionId);
        targetUser.PartChannel(channelName);
    }
}
