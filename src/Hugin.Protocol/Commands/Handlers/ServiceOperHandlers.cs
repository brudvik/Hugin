using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the SAJOIN command - forces a user to join a channel.
/// Requires IRC operator privileges.
/// </summary>
/// <remarks>
/// Syntax: SAJOIN &lt;target&gt; &lt;channel&gt;
/// This command allows operators to force a user to join a channel,
/// bypassing all channel modes (+i, +k, +l, +b, +R).
/// </remarks>
public sealed class SajoinHandler : CommandHandlerBase
{
    /// <inheritdoc/>
    public override string Command => "SAJOIN";

    /// <inheritdoc/>
    public override int MinimumParameters => 2;

    /// <inheritdoc/>
    public override bool RequiresOperator => true;

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var targetNick = context.Message.Parameters[0];
        var channelStr = context.Message.Parameters[1];

        // Find target user
        if (!Nickname.TryCreate(targetNick, out var nickname, out _))
        {
            await context.ReplyAsync(
                IrcNumerics.NoSuchNick(context.ServerName, context.User.Nickname.Value, targetNick),
                cancellationToken);
            return;
        }

        var targetUser = context.Users.GetByNickname(nickname);
        if (targetUser is null)
        {
            await context.ReplyAsync(
                IrcNumerics.NoSuchNick(context.ServerName, context.User.Nickname.Value, targetNick),
                cancellationToken);
            return;
        }

        // Validate channel name
        if (!ChannelName.TryCreate(channelStr, out var channelName, out _))
        {
            await context.ReplyAsync(
                IrcNumerics.BadChannelMask(context.ServerName, context.User.Nickname.Value, channelStr),
                cancellationToken);
            return;
        }

        // Check if user is already on channel
        if (targetUser.Channels.ContainsKey(channelName))
        {
            // Already on channel, nothing to do
            return;
        }

        // Get or create channel
        var channel = context.Channels.GetByName(channelName);
        bool isNewChannel = channel is null;

        if (isNewChannel)
        {
            channel = context.Channels.Create(channelName);
        }

        // Add user to channel (bypass all restrictions)
        var memberMode = isNewChannel ? ChannelMemberMode.Op : ChannelMemberMode.None;
        channel!.AddMember(targetUser, memberMode);
        targetUser.JoinChannel(channelName, memberMode);

        // Build JOIN message from target user
        var joinMsg = IrcMessage.CreateWithSource(targetUser.Hostmask.ToString(), "JOIN", channelStr);
        if (context.Capabilities.HasServerTime)
        {
            joinMsg = joinMsg.WithTags(new Dictionary<string, string?>
            {
                ["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ",
                    System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        // Send JOIN to all channel members
        await context.Broker.SendToChannelAsync(channelStr, joinMsg.ToString(), null, cancellationToken);

        // Send channel info to the target user
        await SendChannelInfoAsync(context, targetUser, channel, channelName, cancellationToken);
    }

    /// <summary>
    /// Sends channel topic and names list to the user.
    /// </summary>
    private static async ValueTask SendChannelInfoAsync(
        CommandContext context,
        Core.Entities.User targetUser,
        Core.Entities.Channel channel,
        ChannelName channelName,
        CancellationToken cancellationToken)
    {
        // Send topic if set
        if (!string.IsNullOrEmpty(channel.Topic))
        {
            var topicMsg = IrcNumerics.Topic(context.ServerName, targetUser.Nickname.Value, channelName.Value, channel.Topic);
            await context.Broker.SendToConnectionAsync(targetUser.ConnectionId, topicMsg.ToString(), cancellationToken);

            if (channel.TopicSetBy is not null && channel.TopicSetAt.HasValue)
            {
                var topicTimeMsg = IrcNumerics.TopicWhoTime(
                    context.ServerName, targetUser.Nickname.Value, channelName.Value,
                    channel.TopicSetBy, channel.TopicSetAt.Value.ToUnixTimeSeconds());
                await context.Broker.SendToConnectionAsync(targetUser.ConnectionId, topicTimeMsg.ToString(), cancellationToken);
            }
        }

        // Send names list
        var names = new List<string>();
        foreach (var member in channel.Members.Values)
        {
            var prefix = GetHighestPrefix(member.Modes);
            names.Add($"{prefix}{member.Nickname.Value}");
        }

        // Determine channel type prefix
        var typePrefix = channel.Modes.HasFlag(ChannelMode.Secret) ? "@" :
                        channel.Modes.HasFlag(ChannelMode.Private) ? "*" : "=";

        var namReplyMsg = IrcNumerics.NamReply(context.ServerName, targetUser.Nickname.Value, typePrefix, channelName.Value, string.Join(" ", names));
        await context.Broker.SendToConnectionAsync(targetUser.ConnectionId, namReplyMsg.ToString(), cancellationToken);

        var endOfNamesMsg = IrcNumerics.EndOfNames(context.ServerName, targetUser.Nickname.Value, channelName.Value);
        await context.Broker.SendToConnectionAsync(targetUser.ConnectionId, endOfNamesMsg.ToString(), cancellationToken);
    }

    private static string GetHighestPrefix(ChannelMemberMode mode)
    {
        if (mode.HasFlag(ChannelMemberMode.Owner)) return "~";
        if (mode.HasFlag(ChannelMemberMode.Admin)) return "&";
        if (mode.HasFlag(ChannelMemberMode.Op)) return "@";
        if (mode.HasFlag(ChannelMemberMode.HalfOp)) return "%";
        if (mode.HasFlag(ChannelMemberMode.Voice)) return "+";
        return "";
    }
}

/// <summary>
/// Handles the SAPART command - forces a user to leave a channel.
/// Requires IRC operator privileges.
/// </summary>
/// <remarks>
/// Syntax: SAPART &lt;target&gt; &lt;channel&gt; [:reason]
/// This command allows operators to force a user to leave a channel.
/// </remarks>
public sealed class SapartHandler : CommandHandlerBase
{
    /// <inheritdoc/>
    public override string Command => "SAPART";

    /// <inheritdoc/>
    public override int MinimumParameters => 2;

    /// <inheritdoc/>
    public override bool RequiresOperator => true;

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var targetNick = context.Message.Parameters[0];
        var channelStr = context.Message.Parameters[1];
        var reason = context.Message.Parameters.Count > 2 ? context.Message.Parameters[2] : null;

        // Find target user
        if (!Nickname.TryCreate(targetNick, out var nickname, out _))
        {
            await context.ReplyAsync(
                IrcNumerics.NoSuchNick(context.ServerName, context.User.Nickname.Value, targetNick),
                cancellationToken);
            return;
        }

        var targetUser = context.Users.GetByNickname(nickname);
        if (targetUser is null)
        {
            await context.ReplyAsync(
                IrcNumerics.NoSuchNick(context.ServerName, context.User.Nickname.Value, targetNick),
                cancellationToken);
            return;
        }

        // Validate channel name
        if (!ChannelName.TryCreate(channelStr, out var channelName, out _))
        {
            await context.ReplyAsync(
                IrcNumerics.NoSuchChannel(context.ServerName, context.User.Nickname.Value, channelStr),
                cancellationToken);
            return;
        }

        // Check channel exists
        var channel = context.Channels.GetByName(channelName);
        if (channel is null)
        {
            await context.ReplyAsync(
                IrcNumerics.NoSuchChannel(context.ServerName, context.User.Nickname.Value, channelStr),
                cancellationToken);
            return;
        }

        // Check if user is on channel
        if (!channel.HasMember(targetUser.ConnectionId))
        {
            await context.ReplyAsync(
                IrcNumerics.UserNotInChannel(context.ServerName, context.User.Nickname.Value, targetNick, channelStr),
                cancellationToken);
            return;
        }

        // Build PART message from target user
        var partMsg = reason is not null
            ? IrcMessage.CreateWithSource(targetUser.Hostmask.ToString(), "PART", channelStr, reason)
            : IrcMessage.CreateWithSource(targetUser.Hostmask.ToString(), "PART", channelStr);

        if (context.Capabilities.HasServerTime)
        {
            partMsg = partMsg.WithTags(new Dictionary<string, string?>
            {
                ["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ",
                    System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        // Send PART to all channel members
        await context.Broker.SendToChannelAsync(channelStr, partMsg.ToString(), null, cancellationToken);

        // Remove user from channel
        channel.RemoveMember(targetUser.ConnectionId);
        targetUser.PartChannel(channelName);

        // Remove empty channel
        if (channel.IsEmpty)
        {
            context.Channels.Remove(channelName);
        }
    }
}

/// <summary>
/// Handles the SANICK command - forces a user to change their nickname.
/// Requires IRC operator privileges.
/// </summary>
/// <remarks>
/// Syntax: SANICK &lt;target&gt; &lt;newnick&gt;
/// This command allows operators to force a user to change their nickname.
/// </remarks>
public sealed class SanickHandler : CommandHandlerBase
{
    /// <inheritdoc/>
    public override string Command => "SANICK";

    /// <inheritdoc/>
    public override int MinimumParameters => 2;

    /// <inheritdoc/>
    public override bool RequiresOperator => true;

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var targetNick = context.Message.Parameters[0];
        var newNickStr = context.Message.Parameters[1];

        // Find target user
        if (!Nickname.TryCreate(targetNick, out var nickname, out _))
        {
            await context.ReplyAsync(
                IrcNumerics.NoSuchNick(context.ServerName, context.User.Nickname.Value, targetNick),
                cancellationToken);
            return;
        }

        var targetUser = context.Users.GetByNickname(nickname);
        if (targetUser is null)
        {
            await context.ReplyAsync(
                IrcNumerics.NoSuchNick(context.ServerName, context.User.Nickname.Value, targetNick),
                cancellationToken);
            return;
        }

        // Validate new nickname
        if (!Nickname.TryCreate(newNickStr, out var newNickname, out _))
        {
            await context.ReplyAsync(
                IrcNumerics.ErroneusNickname(context.ServerName, context.User.Nickname.Value, newNickStr),
                cancellationToken);
            return;
        }

        // Check if new nickname is already in use
        if (context.Users.IsNicknameInUse(newNickname))
        {
            var existingUser = context.Users.GetByNickname(newNickname);
            if (existingUser?.ConnectionId != targetUser.ConnectionId)
            {
                await context.ReplyAsync(
                    IrcNumerics.NicknameInUse(context.ServerName, context.User.Nickname.Value, newNickStr),
                    cancellationToken);
                return;
            }
        }

        var oldHostmask = targetUser.Hostmask.ToString();

        // Update the target user's nickname
        targetUser.SetNickname(newNickname);

        // Build NICK message
        var nickMsg = IrcMessage.CreateWithSource(oldHostmask, "NICK", newNickStr);
        if (context.Capabilities.HasServerTime)
        {
            nickMsg = nickMsg.WithTags(new Dictionary<string, string?>
            {
                ["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ",
                    System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        // Notify all channels the user is in
        var channelNames = targetUser.Channels.Keys.Select(c => c.Value).ToList();
        if (channelNames.Count > 0)
        {
            await context.Broker.SendToChannelsAsync(channelNames, nickMsg.ToString(), null, cancellationToken);
        }
        else
        {
            // Just send to the target user
            await context.Broker.SendToConnectionAsync(targetUser.ConnectionId, nickMsg.ToString(), cancellationToken);
        }

        // Update nickname in all channels
        foreach (var channelName in targetUser.Channels.Keys)
        {
            var channel = context.Channels.GetByName(channelName);
            channel?.UpdateMemberNickname(targetUser.ConnectionId, newNickname);
        }
    }
}

/// <summary>
/// Handles the SAMODE command - sets channel or user modes as a service.
/// Requires IRC operator privileges.
/// </summary>
/// <remarks>
/// Syntax: SAMODE &lt;target&gt; &lt;modes&gt; [params...]
/// This command allows operators to set modes on channels or users,
/// bypassing permission checks.
/// </remarks>
public sealed class SamodeHandler : CommandHandlerBase
{
    /// <inheritdoc/>
    public override string Command => "SAMODE";

    /// <inheritdoc/>
    public override int MinimumParameters => 2;

    /// <inheritdoc/>
    public override bool RequiresOperator => true;

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var target = context.Message.Parameters[0];
        var modeString = context.Message.Parameters[1];
        var modeParams = context.Message.Parameters.Skip(2).ToList();

        // Determine if this is a channel or user mode
        if (target.StartsWith('#') || target.StartsWith('&'))
        {
            await HandleChannelModeAsync(context, target, modeString, modeParams, cancellationToken);
        }
        else
        {
            await HandleUserModeAsync(context, target, modeString, cancellationToken);
        }
    }

    private static async ValueTask HandleChannelModeAsync(
        CommandContext context,
        string channelStr,
        string modeString,
        IList<string> modeParams,
        CancellationToken cancellationToken)
    {
        if (!ChannelName.TryCreate(channelStr, out var channelName, out _))
        {
            await context.ReplyAsync(
                IrcNumerics.NoSuchChannel(context.ServerName, context.User.Nickname.Value, channelStr),
                cancellationToken);
            return;
        }

        var channel = context.Channels.GetByName(channelName);
        if (channel is null)
        {
            await context.ReplyAsync(
                IrcNumerics.NoSuchChannel(context.ServerName, context.User.Nickname.Value, channelStr),
                cancellationToken);
            return;
        }

        // Apply modes
        bool adding = true;
        int paramIndex = 0;
        var appliedModes = new List<char>();
        var appliedParams = new List<string>();
        var directionChanges = new List<(bool Adding, int StartIndex)> { (true, 0) };

        foreach (var c in modeString)
        {
            if (c == '+')
            {
                if (!adding)
                {
                    directionChanges.Add((true, appliedModes.Count));
                }
                adding = true;
                continue;
            }
            if (c == '-')
            {
                if (adding)
                {
                    directionChanges.Add((false, appliedModes.Count));
                }
                adding = false;
                continue;
            }

            var applied = ApplyChannelMode(context, channel, c, adding, modeParams, ref paramIndex, out var usedParam);
            if (applied)
            {
                appliedModes.Add(c);
                if (usedParam is not null)
                {
                    appliedParams.Add(usedParam);
                }
            }
        }

        // Build and send MODE message if any modes were applied
        if (appliedModes.Count > 0)
        {
            var modeStr = BuildModeString(appliedModes, directionChanges);
            var paramStr = appliedParams.Count > 0 ? " " + string.Join(" ", appliedParams) : "";

            var modeMsg = IrcMessage.CreateWithSource(
                context.ServerName, "MODE", channelStr, modeStr + paramStr);

            if (context.Capabilities.HasServerTime)
            {
                modeMsg = modeMsg.WithTags(new Dictionary<string, string?>
                {
                    ["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ",
                        System.Globalization.CultureInfo.InvariantCulture)
                });
            }

            await context.Broker.SendToChannelAsync(channelStr, modeMsg.ToString(), null, cancellationToken);
        }
    }

    private static bool ApplyChannelMode(
        CommandContext context,
        Core.Entities.Channel channel,
        char mode,
        bool adding,
        IList<string> modeParams,
        ref int paramIndex,
        out string? usedParam)
    {
        usedParam = null;

        switch (mode)
        {
            // Simple flags
            case 'i':
                SetChannelMode(channel, ChannelMode.InviteOnly, adding);
                return true;
            case 'm':
                SetChannelMode(channel, ChannelMode.Moderated, adding);
                return true;
            case 'n':
                SetChannelMode(channel, ChannelMode.NoExternalMessages, adding);
                return true;
            case 's':
                SetChannelMode(channel, ChannelMode.Secret, adding);
                return true;
            case 't':
                SetChannelMode(channel, ChannelMode.TopicProtected, adding);
                return true;
            case 'p':
                SetChannelMode(channel, ChannelMode.Private, adding);
                return true;
            case 'C':
                SetChannelMode(channel, ChannelMode.NoCTCP, adding);
                return true;
            case 'c':
                SetChannelMode(channel, ChannelMode.NoColors, adding);
                return true;
            case 'S':
                SetChannelMode(channel, ChannelMode.StripColors, adding);
                return true;
            case 'R':
                SetChannelMode(channel, ChannelMode.RegisteredOnly, adding);
                return true;

            // Modes with parameters
            case 'k':
                if (paramIndex < modeParams.Count)
                {
                    usedParam = modeParams[paramIndex++];
                    if (adding)
                    {
                        channel.SetKey(usedParam);
                    }
                    else
                    {
                        channel.RemoveKey();
                    }
                    return true;
                }
                return false;

            case 'l':
                if (adding && paramIndex < modeParams.Count)
                {
                    usedParam = modeParams[paramIndex++];
                    if (int.TryParse(usedParam, out var limit) && limit > 0)
                    {
                        channel.SetLimit(limit);
                        return true;
                    }
                }
                else if (!adding)
                {
                    channel.RemoveLimit();
                    return true;
                }
                return false;

            // List modes with parameters
            case 'b':
            case 'e':
            case 'I':
                if (paramIndex < modeParams.Count)
                {
                    usedParam = modeParams[paramIndex++];
                    if (adding)
                    {
                        switch (mode)
                        {
                            case 'b':
                                channel.AddBan(usedParam, context.ServerName);
                                break;
                            // Note: 'e' and 'I' would need similar methods added to Channel
                            // For now, we only support 'b'
                        }
                    }
                    else
                    {
                        switch (mode)
                        {
                            case 'b':
                                channel.RemoveBan(usedParam);
                                break;
                        }
                    }
                    return mode == 'b'; // Only return true for supported modes
                }
                return false;

            // Member modes
            case 'o':
            case 'v':
            case 'h':
            case 'a':
            case 'q':
                if (paramIndex < modeParams.Count)
                {
                    usedParam = modeParams[paramIndex++];
                    if (Nickname.TryCreate(usedParam, out var targetNick, out _))
                    {
                        // Find the member by nickname
                        var member = channel.Members.Values.FirstOrDefault(m =>
                            m.Nickname.Value.Equals(targetNick.Value, StringComparison.OrdinalIgnoreCase));

                        if (member is not null)
                        {
                            var memberMode = mode switch
                            {
                                'o' => ChannelMemberMode.Op,
                                'v' => ChannelMemberMode.Voice,
                                'h' => ChannelMemberMode.HalfOp,
                                'a' => ChannelMemberMode.Admin,
                                'q' => ChannelMemberMode.Owner,
                                _ => ChannelMemberMode.None
                            };
                            if (adding)
                            {
                                channel.AddMemberMode(member.ConnectionId, memberMode);
                            }
                            else
                            {
                                channel.RemoveMemberMode(member.ConnectionId, memberMode);
                            }
                            return true;
                        }
                    }
                }
                return false;

            default:
                return false;
        }
    }

    private static void SetChannelMode(Core.Entities.Channel channel, ChannelMode mode, bool adding)
    {
        if (adding)
        {
            channel.AddMode(mode);
        }
        else
        {
            channel.RemoveMode(mode);
        }
    }

    private static string BuildModeString(List<char> modes, List<(bool Adding, int StartIndex)> directionChanges)
    {
        var result = new System.Text.StringBuilder();
        bool? currentDir = null;

        for (int i = 0; i < modes.Count; i++)
        {
            // Check if direction changes at this index
            var dirChange = directionChanges.LastOrDefault(d => d.StartIndex <= i);
            if (currentDir != dirChange.Adding)
            {
                currentDir = dirChange.Adding;
                result.Append(currentDir == true ? '+' : '-');
            }
            result.Append(modes[i]);
        }

        return result.ToString();
    }

    private static async ValueTask HandleUserModeAsync(
        CommandContext context,
        string targetNick,
        string modeString,
        CancellationToken cancellationToken)
    {
        if (!Nickname.TryCreate(targetNick, out var nickname, out _))
        {
            await context.ReplyAsync(
                IrcNumerics.NoSuchNick(context.ServerName, context.User.Nickname.Value, targetNick),
                cancellationToken);
            return;
        }

        var targetUser = context.Users.GetByNickname(nickname);
        if (targetUser is null)
        {
            await context.ReplyAsync(
                IrcNumerics.NoSuchNick(context.ServerName, context.User.Nickname.Value, targetNick),
                cancellationToken);
            return;
        }

        // Apply user modes
        bool adding = true;
        var appliedModes = new List<char>();
        var directionChanges = new List<(bool Adding, int StartIndex)> { (true, 0) };

        foreach (var c in modeString)
        {
            if (c == '+')
            {
                if (!adding)
                {
                    directionChanges.Add((true, appliedModes.Count));
                }
                adding = true;
                continue;
            }
            if (c == '-')
            {
                if (adding)
                {
                    directionChanges.Add((false, appliedModes.Count));
                }
                adding = false;
                continue;
            }

            var applied = ApplyUserMode(targetUser, c, adding);
            if (applied)
            {
                appliedModes.Add(c);
            }
        }

        // Build and send MODE message if any modes were applied
        if (appliedModes.Count > 0)
        {
            var modeStr = BuildModeString(appliedModes, directionChanges);

            var modeMsg = IrcMessage.CreateWithSource(context.ServerName, "MODE", targetNick, modeStr);

            if (context.Capabilities.HasServerTime)
            {
                modeMsg = modeMsg.WithTags(new Dictionary<string, string?>
                {
                    ["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ",
                        System.Globalization.CultureInfo.InvariantCulture)
                });
            }

            // Send to the target user
            await context.Broker.SendToConnectionAsync(targetUser.ConnectionId, modeMsg.ToString(), cancellationToken);
        }
    }

    private static bool ApplyUserMode(Core.Entities.User user, char mode, bool adding)
    {
        UserMode? userMode = mode switch
        {
            'i' => UserMode.Invisible,
            'w' => UserMode.Wallops,
            'o' => UserMode.Operator,
            's' => UserMode.ServerNotices,
            'r' => UserMode.Registered,
            'B' => UserMode.Bot,
            _ => null
        };

        if (userMode is null)
        {
            return false;
        }

        // Prevent removing operator status via SAMODE (require KILL/DIE for deop)
        if (!adding && userMode == UserMode.Operator)
        {
            return false;
        }

        if (adding)
        {
            user.AddMode(userMode.Value);
        }
        else
        {
            user.RemoveMode(userMode.Value);
        }

        return true;
    }
}
