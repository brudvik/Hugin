using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the MODE command for both user and channel modes.
/// </summary>
public sealed class ModeHandler : CommandHandlerBase
{
    public override string Command => "MODE";
    public override int MinimumParameters => 1;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var target = context.Message.Parameters[0];

        // Check if it's a channel or user mode
        if (target.StartsWith('#') || target.StartsWith('&'))
        {
            await HandleChannelModeAsync(context, target, cancellationToken);
        }
        else
        {
            await HandleUserModeAsync(context, target, cancellationToken);
        }
    }

    private static async ValueTask HandleUserModeAsync(CommandContext context, string targetNick, CancellationToken cancellationToken)
    {
        var nick = context.User.Nickname.Value;

        // Users can only query/change their own modes (unless they're opers)
        if (!targetNick.Equals(nick, StringComparison.OrdinalIgnoreCase) && !context.User.IsOperator)
        {
            await context.ReplyAsync(
                IrcNumerics.UsersDontMatch(context.ServerName, nick),
                cancellationToken);
            return;
        }

        // Query mode
        if (context.Message.Parameters.Count == 1)
        {
            await context.ReplyAsync(
                IrcNumerics.UModeIs(context.ServerName, nick, context.User.Modes.ToModeString()),
                cancellationToken);
            return;
        }

        // Set mode
        var modeString = context.Message.Parameters[1];
        await ApplyUserModesAsync(context, modeString, cancellationToken);
    }

    private static async ValueTask ApplyUserModesAsync(CommandContext context, string modeString, CancellationToken cancellationToken)
    {
        var nick = context.User.Nickname.Value;
        bool adding = true;
        var addedModes = new List<char>();
        var removedModes = new List<char>();

        foreach (char c in modeString)
        {
            if (c == '+')
            {
                adding = true;
                continue;
            }
            if (c == '-')
            {
                adding = false;
                continue;
            }

            var mode = UserModeExtensions.FromChar(c);
            if (mode is null)
            {
                await context.ReplyAsync(
                    IrcNumerics.UModeUnknownFlag(context.ServerName, nick),
                    cancellationToken);
                continue;
            }

            // Certain modes cannot be set by users
            if (mode == UserMode.Operator && adding)
            {
                // Can't give yourself oper status via MODE
                continue;
            }

            if (mode == UserMode.Registered || mode == UserMode.Secure)
            {
                // These are set by the server, not the user
                continue;
            }

            if (adding)
            {
                context.User.AddMode(mode.Value);
                addedModes.Add(c);
            }
            else
            {
                context.User.RemoveMode(mode.Value);
                removedModes.Add(c);
            }
        }

        // Send confirmation
        var confirmModeString = "";
        if (addedModes.Count > 0)
        {
            confirmModeString += "+" + new string(addedModes.ToArray());
        }
        if (removedModes.Count > 0)
        {
            confirmModeString += "-" + new string(removedModes.ToArray());
        }

        if (!string.IsNullOrEmpty(confirmModeString))
        {
            var modeMsg = IrcMessage.CreateWithSource(context.User.Hostmask.ToString(), "MODE", nick, confirmModeString);
            await context.ReplyAsync(modeMsg, cancellationToken);
        }
    }

    private static async ValueTask HandleChannelModeAsync(CommandContext context, string channelStr, CancellationToken cancellationToken)
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

        var nick = context.User.Nickname.Value;

        // Query mode
        if (context.Message.Parameters.Count == 1)
        {
            await context.ReplyAsync(
                IrcNumerics.ChannelModeIs(context.ServerName, nick, channelStr, channel.GetModeString()),
                cancellationToken);
            await context.ReplyAsync(
                IrcNumerics.CreationTime(context.ServerName, nick, channelStr, channel.CreatedAt.ToUnixTimeSeconds()),
                cancellationToken);
            return;
        }

        // Setting modes - check privileges
        var member = channel.GetMember(context.User.ConnectionId);
        bool hasPrivilege = (member?.IsHalfOpOrHigher ?? false) || context.User.IsOperator;

        // Parse and apply modes
        var modeString = context.Message.Parameters[1];
        await ApplyChannelModesAsync(context, channel, channelStr, modeString, hasPrivilege, cancellationToken);
    }

    private static async ValueTask ApplyChannelModesAsync(
        CommandContext context,
        Core.Entities.Channel channel,
        string channelStr,
        string modeString,
        bool hasPrivilege,
        CancellationToken cancellationToken)
    {
        var nick = context.User.Nickname.Value;
        bool adding = true;
        int paramIndex = 2;

        var appliedModes = new List<(bool adding, char mode, string? param)>();

        foreach (char c in modeString)
        {
            if (c == '+')
            {
                adding = true;
                continue;
            }
            if (c == '-')
            {
                adding = false;
                continue;
            }

            string? param = null;

            // Determine the type of mode and handle accordingly
            switch (c)
            {
                // Type A modes (list modes with parameters)
                case 'b': // ban
                    if (!hasPrivilege && adding)
                    {
                        await SendChanOpPrivsNeededAsync(context, channelStr, cancellationToken);
                        return;
                    }
                    if (paramIndex < context.Message.Parameters.Count)
                    {
                        param = context.Message.Parameters[paramIndex++];
                        if (adding)
                        {
                            channel.AddBan(param, context.User.Hostmask.ToString());
                        }
                        else
                        {
                            channel.RemoveBan(param);
                        }
                        appliedModes.Add((adding, c, param));
                    }
                    else if (!adding)
                    {
                        // List bans
                        foreach (var ban in channel.Bans)
                        {
                            await context.ReplyAsync(
                                IrcNumerics.BanList(context.ServerName, nick, channelStr, ban.Mask, ban.SetBy, ban.SetAt.ToUnixTimeSeconds()),
                                cancellationToken);
                        }
                        await context.ReplyAsync(
                            IrcNumerics.EndOfBanList(context.ServerName, nick, channelStr),
                            cancellationToken);
                    }
                    break;

                // Type B modes (always require parameter)
                case 'k': // key
                    if (!hasPrivilege)
                    {
                        await SendChanOpPrivsNeededAsync(context, channelStr, cancellationToken);
                        return;
                    }
                    if (adding && paramIndex < context.Message.Parameters.Count)
                    {
                        param = context.Message.Parameters[paramIndex++];
                        channel.SetKey(param);
                        appliedModes.Add((adding, c, param));
                    }
                    else if (!adding)
                    {
                        // Key removal requires the current key as parameter
                        if (paramIndex < context.Message.Parameters.Count)
                        {
                            param = context.Message.Parameters[paramIndex++];
                        }
                        channel.RemoveKey();
                        appliedModes.Add((adding, c, "*"));
                    }
                    break;

                // Type B - Member modes (always require nickname parameter)
                case 'o': // op
                case 'v': // voice
                case 'h': // halfop
                case 'a': // admin
                case 'q': // owner
                    if (!hasPrivilege)
                    {
                        await SendChanOpPrivsNeededAsync(context, channelStr, cancellationToken);
                        return;
                    }
                    if (paramIndex < context.Message.Parameters.Count)
                    {
                        param = context.Message.Parameters[paramIndex++];
                        await ApplyMemberModeAsync(context, channel, channelStr, c, param, adding, appliedModes, cancellationToken);
                    }
                    break;

                // Type C modes (parameter when setting only)
                case 'l': // limit
                    if (!hasPrivilege)
                    {
                        await SendChanOpPrivsNeededAsync(context, channelStr, cancellationToken);
                        return;
                    }
                    if (adding && paramIndex < context.Message.Parameters.Count)
                    {
                        param = context.Message.Parameters[paramIndex++];
                        if (int.TryParse(param, out int limit) && limit > 0)
                        {
                            channel.SetLimit(limit);
                            appliedModes.Add((adding, c, param));
                        }
                    }
                    else if (!adding)
                    {
                        channel.RemoveLimit();
                        appliedModes.Add((adding, c, null));
                    }
                    break;

                // Type D modes (no parameter)
                case 'i': // invite-only
                    if (!hasPrivilege)
                    {
                        await SendChanOpPrivsNeededAsync(context, channelStr, cancellationToken);
                        return;
                    }
                    if (adding)
                    {
                        channel.AddMode(ChannelMode.InviteOnly);
                    }
                    else
                    {
                        channel.RemoveMode(ChannelMode.InviteOnly);
                    }
                    appliedModes.Add((adding, c, null));
                    break;

                case 'm': // moderated
                    if (!hasPrivilege)
                    {
                        await SendChanOpPrivsNeededAsync(context, channelStr, cancellationToken);
                        return;
                    }
                    if (adding)
                    {
                        channel.AddMode(ChannelMode.Moderated);
                    }
                    else
                    {
                        channel.RemoveMode(ChannelMode.Moderated);
                    }
                    appliedModes.Add((adding, c, null));
                    break;

                case 'n': // no external messages
                    if (!hasPrivilege)
                    {
                        await SendChanOpPrivsNeededAsync(context, channelStr, cancellationToken);
                        return;
                    }
                    if (adding)
                    {
                        channel.AddMode(ChannelMode.NoExternalMessages);
                    }
                    else
                    {
                        channel.RemoveMode(ChannelMode.NoExternalMessages);
                    }
                    appliedModes.Add((adding, c, null));
                    break;

                case 's': // secret
                    if (!hasPrivilege)
                    {
                        await SendChanOpPrivsNeededAsync(context, channelStr, cancellationToken);
                        return;
                    }
                    if (adding)
                    {
                        channel.AddMode(ChannelMode.Secret);
                    }
                    else
                    {
                        channel.RemoveMode(ChannelMode.Secret);
                    }
                    appliedModes.Add((adding, c, null));
                    break;

                case 't': // topic protected
                    if (!hasPrivilege)
                    {
                        await SendChanOpPrivsNeededAsync(context, channelStr, cancellationToken);
                        return;
                    }
                    if (adding)
                    {
                        channel.AddMode(ChannelMode.TopicProtected);
                    }
                    else
                    {
                        channel.RemoveMode(ChannelMode.TopicProtected);
                    }
                    appliedModes.Add((adding, c, null));
                    break;

                case 'C': // no CTCP
                    if (!hasPrivilege)
                    {
                        await SendChanOpPrivsNeededAsync(context, channelStr, cancellationToken);
                        return;
                    }
                    if (adding)
                    {
                        channel.AddMode(ChannelMode.NoCTCP);
                    }
                    else
                    {
                        channel.RemoveMode(ChannelMode.NoCTCP);
                    }
                    appliedModes.Add((adding, c, null));
                    break;

                case 'S': // strip colors
                    if (!hasPrivilege)
                    {
                        await SendChanOpPrivsNeededAsync(context, channelStr, cancellationToken);
                        return;
                    }
                    if (adding)
                    {
                        channel.AddMode(ChannelMode.StripColors);
                    }
                    else
                    {
                        channel.RemoveMode(ChannelMode.StripColors);
                    }
                    appliedModes.Add((adding, c, null));
                    break;

                case 'c': // no colors
                    if (!hasPrivilege)
                    {
                        await SendChanOpPrivsNeededAsync(context, channelStr, cancellationToken);
                        return;
                    }
                    if (adding)
                    {
                        channel.AddMode(ChannelMode.NoColors);
                    }
                    else
                    {
                        channel.RemoveMode(ChannelMode.NoColors);
                    }
                    appliedModes.Add((adding, c, null));
                    break;

                case 'R': // registered users only
                    if (!hasPrivilege)
                    {
                        await SendChanOpPrivsNeededAsync(context, channelStr, cancellationToken);
                        return;
                    }
                    if (adding)
                    {
                        channel.AddMode(ChannelMode.RegisteredOnly);
                    }
                    else
                    {
                        channel.RemoveMode(ChannelMode.RegisteredOnly);
                    }
                    appliedModes.Add((adding, c, null));
                    break;

                default:
                    await context.ReplyAsync(
                        IrcNumerics.UnknownMode(context.ServerName, nick, c),
                        cancellationToken);
                    break;
            }
        }

        // Broadcast applied modes to channel
        if (appliedModes.Count > 0)
        {
            await BroadcastModeChangeAsync(context, channelStr, appliedModes, cancellationToken);
        }
    }

    private static async ValueTask ApplyMemberModeAsync(
        CommandContext context,
        Core.Entities.Channel channel,
        string channelStr,
        char modeChar,
        string targetNick,
        bool adding,
        List<(bool adding, char mode, string? param)> appliedModes,
        CancellationToken cancellationToken)
    {
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
                IrcNumerics.UserNotInChannel(context.ServerName, context.User.Nickname.Value, targetNick, channelStr),
                cancellationToken);
            return;
        }

        var memberMode = ChannelModeExtensions.FromModeChar(modeChar);
        if (memberMode is null) return;

        if (adding)
        {
            channel.AddMemberMode(targetUser.ConnectionId, memberMode.Value);
            targetUser.AddChannelMode(channel.Name, memberMode.Value);
        }
        else
        {
            channel.RemoveMemberMode(targetUser.ConnectionId, memberMode.Value);
            targetUser.RemoveChannelMode(channel.Name, memberMode.Value);
        }

        appliedModes.Add((adding, modeChar, targetNick));
    }

    private static async ValueTask BroadcastModeChangeAsync(
        CommandContext context,
        string channelStr,
        List<(bool adding, char mode, string? param)> appliedModes,
        CancellationToken cancellationToken)
    {
        // Build mode string
        var modeStringBuilder = new System.Text.StringBuilder();
        var parameters = new List<string>();
        bool? lastAdding = null;

        foreach (var (adding, mode, param) in appliedModes)
        {
            if (lastAdding != adding)
            {
                modeStringBuilder.Append(adding ? '+' : '-');
                lastAdding = adding;
            }
            modeStringBuilder.Append(mode);

            if (param is not null)
            {
                parameters.Add(param);
            }
        }

        var modeStr = modeStringBuilder.ToString();
        var allParams = new List<string> { channelStr, modeStr };
        allParams.AddRange(parameters);

        var modeMsg = IrcMessage.CreateWithSource(context.User.Hostmask.ToString(), "MODE", allParams.ToArray());

        if (context.Capabilities.HasServerTime)
        {
            modeMsg = modeMsg.WithTags(new Dictionary<string, string?>
            {
                ["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        await context.Broker.SendToChannelAsync(channelStr, modeMsg.ToString(), null, cancellationToken);
    }
}
