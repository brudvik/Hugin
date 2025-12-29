using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the WHOIS command.
/// </summary>
public sealed class WhoisHandler : CommandHandlerBase
{
    public override string Command => "WHOIS";
    public override int MinimumParameters => 1;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        // WHOIS [server] nickname[,nickname,...]
        // For now, we ignore the server parameter
        var targetParam = context.Message.Parameters.Count > 1 
            ? context.Message.Parameters[1] 
            : context.Message.Parameters[0];

        var nicknames = targetParam.Split(',');

        foreach (var nickStr in nicknames)
        {
            await SendWhoisReplyAsync(context, nickStr.Trim(), cancellationToken);
        }
    }

    private static async ValueTask SendWhoisReplyAsync(CommandContext context, string targetNick, CancellationToken cancellationToken)
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

        var nick = context.User.Nickname.Value;
        var targetNickValue = targetUser.Nickname.Value;

        // 311 RPL_WHOISUSER - Basic user info
        await context.ReplyAsync(
            IrcNumerics.WhoisUser(context.ServerName, nick, targetNickValue,
                targetUser.Username, targetUser.DisplayedHostname, targetUser.RealName),
            cancellationToken);

        // 319 RPL_WHOISCHANNELS - Channels the user is on
        var channelList = BuildChannelList(context, targetUser);
        if (!string.IsNullOrEmpty(channelList))
        {
            await context.ReplyAsync(
                IrcNumerics.WhoisChannels(context.ServerName, nick, targetNickValue, channelList),
                cancellationToken);
        }

        // 312 RPL_WHOISSERVER - Server the user is on
        await context.ReplyAsync(
            IrcNumerics.WhoisServer(context.ServerName, nick, targetNickValue, context.ServerName, "Hugin IRC Server"),
            cancellationToken);

        // 330 RPL_WHOISACCOUNT - Account name if logged in
        if (targetUser.Account is not null)
        {
            await context.ReplyAsync(
                IrcNumerics.WhoisAccount(context.ServerName, nick, targetNickValue, targetUser.Account),
                cancellationToken);
        }

        // 671 RPL_WHOISSECURE - Using TLS
        if (targetUser.IsSecure)
        {
            await context.ReplyAsync(
                IrcNumerics.WhoisSecure(context.ServerName, nick, targetNickValue),
                cancellationToken);
        }

        // 313 RPL_WHOISOPERATOR - Is an IRC operator
        if (targetUser.IsOperator)
        {
            await context.ReplyAsync(
                IrcNumerics.WhoisOperator(context.ServerName, nick, targetNickValue),
                cancellationToken);
        }

        // 301 RPL_AWAY - Is away
        if (targetUser.IsAway)
        {
            await context.ReplyAsync(
                IrcNumerics.Away(context.ServerName, nick, targetNickValue, targetUser.AwayMessage!),
                cancellationToken);
        }

        // 317 RPL_WHOISIDLE - Idle and signon time
        await context.ReplyAsync(
            IrcNumerics.WhoisIdle(context.ServerName, nick, targetNickValue,
                targetUser.GetIdleSeconds(), targetUser.ConnectedAt.ToUnixTimeSeconds()),
            cancellationToken);

        // 318 RPL_ENDOFWHOIS - End of WHOIS
        await context.ReplyAsync(
            IrcNumerics.EndOfWhois(context.ServerName, nick, targetNickValue),
            cancellationToken);
    }

    private static string BuildChannelList(CommandContext context, Core.Entities.User targetUser)
    {
        var channels = new List<string>();

        foreach (var (channelName, memberMode) in targetUser.Channels)
        {
            var channel = context.Channels.GetByName(channelName);
            if (channel is null) continue;

            // Skip secret channels unless the requester is also on them
            if (channel.Modes.HasFlag(ChannelMode.Secret))
            {
                if (!channel.HasMember(context.User.ConnectionId) && !context.User.IsOperator)
                {
                    continue;
                }
            }

            string prefix = GetMemberPrefix(memberMode);
            channels.Add($"{prefix}{channelName.Value}");
        }

        return string.Join(" ", channels);
    }

    private static string GetMemberPrefix(ChannelMemberMode mode)
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
/// Handles the WHO command.
/// </summary>
public sealed class WhoHandler : CommandHandlerBase
{
    public override string Command => "WHO";

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var mask = context.Message.Parameters.Count > 0 ? context.Message.Parameters[0] : "*";
        var flags = context.Message.Parameters.Count > 1 ? context.Message.Parameters[1] : "";

        // Parse WHOX format: WHO <mask> <flags>%<fields>[,<querytype>]
        var whoxRequest = WhoxRequest.Parse(flags);
        var operOnly = whoxRequest?.OperOnly ?? flags.Equals("o", StringComparison.OrdinalIgnoreCase);

        // Check if it's a channel query
        if (mask.StartsWith('#') || mask.StartsWith('&'))
        {
            await SendChannelWhoAsync(context, mask, operOnly, whoxRequest, cancellationToken);
        }
        else
        {
            await SendMaskWhoAsync(context, mask, operOnly, whoxRequest, cancellationToken);
        }

        // End of WHO
        await context.ReplyAsync(
            IrcNumerics.EndOfWho(context.ServerName, context.User.Nickname.Value, mask),
            cancellationToken);
    }

    private static async ValueTask SendChannelWhoAsync(CommandContext context, string channelStr, bool operOnly, WhoxRequest? whox, CancellationToken cancellationToken)
    {
        if (!ChannelName.TryCreate(channelStr, out var channelName, out _))
        {
            return;
        }

        var channel = context.Channels.GetByName(channelName);
        if (channel is null)
        {
            return;
        }

        // Don't show members of secret channels unless the requester is on them
        if (channel.Modes.HasFlag(ChannelMode.Secret) && !channel.HasMember(context.User.ConnectionId))
        {
            return;
        }

        foreach (var member in channel.Members.Values)
        {
            var user = context.Users.GetByConnectionId(member.ConnectionId);
            if (user is null) continue;

            if (operOnly && !user.IsOperator) continue;

            // Skip invisible users not in a shared channel (unless the requester is an oper)
            if (user.Modes.HasFlag(UserMode.Invisible) && !context.User.IsOperator)
            {
                if (!SharesChannelWith(context, user)) continue;
            }

            await SendWhoReplyAsync(context, user, channelStr, member.Modes, whox, cancellationToken);
        }
    }

    private static async ValueTask SendMaskWhoAsync(CommandContext context, string mask, bool operOnly, WhoxRequest? whox, CancellationToken cancellationToken)
    {
        foreach (var user in context.Users.GetAll())
        {
            if (operOnly && !user.IsOperator) continue;

            // Skip invisible users not in a shared channel (unless the requester is an oper)
            if (user.Modes.HasFlag(UserMode.Invisible) && !context.User.IsOperator)
            {
                if (!SharesChannelWith(context, user)) continue;
            }

            // Check if user matches the mask
            if (mask != "*" && !MatchesMask(user, mask))
            {
                continue;
            }

            // Find a common channel to report (or * if none)
            string channelToReport = "*";
            ChannelMemberMode memberMode = ChannelMemberMode.None;

            foreach (var (channelName, mode) in user.Channels)
            {
                var channel = context.Channels.GetByName(channelName);
                if (channel is null) continue;

                // Skip secret channels
                if (channel.Modes.HasFlag(ChannelMode.Secret) && !channel.HasMember(context.User.ConnectionId))
                {
                    continue;
                }

                channelToReport = channelName.Value;
                memberMode = mode;
                break;
            }

            await SendWhoReplyAsync(context, user, channelToReport, memberMode, whox, cancellationToken);
        }
    }

    private static async ValueTask SendWhoReplyAsync(CommandContext context, Core.Entities.User user, string channel, ChannelMemberMode mode, WhoxRequest? whox, CancellationToken cancellationToken)
    {
        // If WHOX is requested, use the extended 354 format
        if (whox is not null)
        {
            await SendWhoxReplyAsync(context, user, channel, mode, whox, cancellationToken);
            return;
        }

        // Build status string: H/G (here/gone) + operator flag (*) + channel mode prefix
        var status = user.IsAway ? "G" : "H";
        if (user.IsOperator)
        {
            status += "*";
        }
        status += GetMemberPrefix(mode);

        await context.ReplyAsync(
            IrcNumerics.WhoReply(context.ServerName, context.User.Nickname.Value,
                channel, user.Username, user.DisplayedHostname, context.ServerName,
                user.Nickname.Value, status, 0, user.RealName),
            cancellationToken);
    }

    /// <summary>
    /// Sends an extended WHOX (354) reply.
    /// </summary>
    private static async ValueTask SendWhoxReplyAsync(
        CommandContext context,
        Core.Entities.User user,
        string channel,
        ChannelMemberMode mode,
        WhoxRequest whox,
        CancellationToken cancellationToken)
    {
        var fields = new List<string>();

        // Build fields in order based on the WHOX field specification
        // Common fields: t (query type), c (channel), u (username), i (IP), h (host), 
        //                s (server), n (nick), f (flags), d (hopcount), l (idle), 
        //                a (account), o (oplevel), r (realname)

        if (whox.HasQueryType)
        {
            fields.Add(whox.QueryType ?? "0");
        }

        if (whox.HasChannel)
        {
            fields.Add(channel);
        }

        if (whox.HasUsername)
        {
            fields.Add(user.Username);
        }

        if (whox.HasIp)
        {
            // Only show IP to operators, otherwise show 0
            fields.Add(context.User.IsOperator ? (user.IpAddress?.ToString() ?? "0") : "0");
        }

        if (whox.HasHost)
        {
            fields.Add(user.DisplayedHostname);
        }

        if (whox.HasServer)
        {
            fields.Add(context.ServerName);
        }

        if (whox.HasNick)
        {
            fields.Add(user.Nickname.Value);
        }

        if (whox.HasFlags)
        {
            var status = user.IsAway ? "G" : "H";
            if (user.IsOperator)
            {
                status += "*";
            }
            status += GetMemberPrefix(mode);
            fields.Add(status);
        }

        if (whox.HasHopcount)
        {
            fields.Add("0");
        }

        if (whox.HasIdle)
        {
            var idle = user.GetIdleSeconds();
            fields.Add(idle.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (whox.HasAccount)
        {
            fields.Add(user.Account ?? "0");
        }

        if (whox.HasOplevel)
        {
            fields.Add("n/a");
        }

        if (whox.HasRealname)
        {
            fields.Add(user.RealName);
        }

        await context.ReplyAsync(
            IrcNumerics.WhoxReply(context.ServerName, context.User.Nickname.Value, fields.ToArray()),
            cancellationToken);
    }

    private static bool SharesChannelWith(CommandContext context, Core.Entities.User otherUser)
    {
        foreach (var channelName in context.User.Channels.Keys)
        {
            if (otherUser.Channels.ContainsKey(channelName))
            {
                return true;
            }
        }
        return false;
    }

    private static bool MatchesMask(Core.Entities.User user, string mask)
    {
        // Simple wildcard matching
        var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(mask)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        try
        {
            var regex = new System.Text.RegularExpressions.Regex(pattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(100));

            return regex.IsMatch(user.Nickname.Value) ||
                   regex.IsMatch(user.Username) ||
                   regex.IsMatch(user.DisplayedHostname) ||
                   regex.IsMatch(user.RealName) ||
                   regex.IsMatch(user.Hostmask.ToString());
        }
        catch
        {
            return false;
        }
    }

    private static string GetMemberPrefix(ChannelMemberMode mode)
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
/// Represents a WHOX request with field specifiers.
/// </summary>
/// <remarks>
/// WHOX format: WHO &lt;mask&gt; &lt;flags&gt;%&lt;fields&gt;[,&lt;querytype&gt;]
/// Flags: o (operators only), plus other filters
/// Fields: t (query type), c (channel), u (user), i (IP), h (host), s (server),
///         n (nick), f (flags), d (hopcount), l (idle), a (account), o (oplevel), r (realname)
/// </remarks>
public sealed class WhoxRequest
{
    /// <summary>Whether operators only should be returned.</summary>
    public bool OperOnly { get; private init; }

    /// <summary>The query type token.</summary>
    public string? QueryType { get; private init; }

    /// <summary>Whether query type field is requested.</summary>
    public bool HasQueryType { get; private init; }

    /// <summary>Whether channel field is requested.</summary>
    public bool HasChannel { get; private init; }

    /// <summary>Whether username field is requested.</summary>
    public bool HasUsername { get; private init; }

    /// <summary>Whether IP field is requested.</summary>
    public bool HasIp { get; private init; }

    /// <summary>Whether host field is requested.</summary>
    public bool HasHost { get; private init; }

    /// <summary>Whether server field is requested.</summary>
    public bool HasServer { get; private init; }

    /// <summary>Whether nickname field is requested.</summary>
    public bool HasNick { get; private init; }

    /// <summary>Whether flags field is requested.</summary>
    public bool HasFlags { get; private init; }

    /// <summary>Whether hopcount field is requested.</summary>
    public bool HasHopcount { get; private init; }

    /// <summary>Whether idle time field is requested.</summary>
    public bool HasIdle { get; private init; }

    /// <summary>Whether account field is requested.</summary>
    public bool HasAccount { get; private init; }

    /// <summary>Whether oplevel field is requested.</summary>
    public bool HasOplevel { get; private init; }

    /// <summary>Whether realname field is requested.</summary>
    public bool HasRealname { get; private init; }

    /// <summary>
    /// Parses WHOX flags from WHO command.
    /// </summary>
    /// <param name="input">The flags parameter (e.g., "o%tcuihsnfdlar,123").</param>
    /// <returns>Parsed WHOX request or null if not a WHOX format.</returns>
    public static WhoxRequest? Parse(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        // Check for % which indicates WHOX format
        var percentIndex = input.IndexOf('%');
        if (percentIndex < 0)
        {
            return null;
        }

        var flags = input[..percentIndex];
        var fieldsAndType = input[(percentIndex + 1)..];

        // Split fields and query type
        string fields;
        string? queryType = null;
        var commaIndex = fieldsAndType.IndexOf(',');
        if (commaIndex >= 0)
        {
            fields = fieldsAndType[..commaIndex];
            queryType = fieldsAndType[(commaIndex + 1)..];
        }
        else
        {
            fields = fieldsAndType;
        }

        return new WhoxRequest
        {
            OperOnly = flags.Contains('o', StringComparison.OrdinalIgnoreCase),
            QueryType = queryType,
            HasQueryType = fields.Contains('t', StringComparison.OrdinalIgnoreCase),
            HasChannel = fields.Contains('c', StringComparison.OrdinalIgnoreCase),
            HasUsername = fields.Contains('u', StringComparison.OrdinalIgnoreCase),
            HasIp = fields.Contains('i', StringComparison.OrdinalIgnoreCase),
            HasHost = fields.Contains('h', StringComparison.OrdinalIgnoreCase),
            HasServer = fields.Contains('s', StringComparison.OrdinalIgnoreCase),
            HasNick = fields.Contains('n', StringComparison.OrdinalIgnoreCase),
            HasFlags = fields.Contains('f', StringComparison.OrdinalIgnoreCase),
            HasHopcount = fields.Contains('d', StringComparison.OrdinalIgnoreCase),
            HasIdle = fields.Contains('l', StringComparison.OrdinalIgnoreCase),
            HasAccount = fields.Contains('a', StringComparison.OrdinalIgnoreCase),
            HasOplevel = fields.Contains('o', StringComparison.OrdinalIgnoreCase),
            HasRealname = fields.Contains('r', StringComparison.OrdinalIgnoreCase),
        };
    }
}

/// <summary>
/// Handles the LIST command.
/// </summary>
public sealed class ListHandler : CommandHandlerBase
{
    public override string Command => "LIST";

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User.Nickname.Value;

        // Get filter pattern if provided
        string? pattern = context.Message.Parameters.Count > 0 ? context.Message.Parameters[0] : null;

        foreach (var channel in context.Channels.GetAll())
        {
            // Skip secret channels unless the user is on them
            if (channel.Modes.HasFlag(ChannelMode.Secret))
            {
                if (!channel.HasMember(context.User.ConnectionId) && !context.User.IsOperator)
                {
                    continue;
                }
            }

            // Apply pattern filter
            if (pattern is not null && !MatchesPattern(channel.Name.Value, pattern))
            {
                continue;
            }

            // Count visible members (for secret channels, show real count only to members)
            int visibleCount = channel.MemberCount;

            await context.ReplyAsync(
                IrcNumerics.List(context.ServerName, nick, channel.Name.Value, visibleCount, channel.Topic ?? ""),
                cancellationToken);
        }

        await context.ReplyAsync(
            IrcNumerics.ListEnd(context.ServerName, nick),
            cancellationToken);
    }

    private static bool MatchesPattern(string channelName, string pattern)
    {
        // Handle comma-separated channel list
        var patterns = pattern.Split(',');
        foreach (var p in patterns)
        {
            var trimmed = p.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Simple wildcard matching
            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(trimmed)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            try
            {
                var regex = new System.Text.RegularExpressions.Regex(regexPattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(100));

                if (regex.IsMatch(channelName))
                {
                    return true;
                }
            }
            catch
            {
                // Invalid pattern, try exact match
                if (channelName.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

/// <summary>
/// Handles the NAMES command (standalone).
/// </summary>
public sealed class NamesHandler : CommandHandlerBase
{
    public override string Command => "NAMES";

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        if (context.Message.Parameters.Count == 0)
        {
            // No parameters - list all visible users not in channels
            // This is rarely used in practice
            await context.ReplyAsync(
                IrcNumerics.EndOfNames(context.ServerName, context.User.Nickname.Value, "*"),
                cancellationToken);
            return;
        }

        var channels = context.Message.Parameters[0].Split(',');

        foreach (var channelStr in channels)
        {
            await SendNamesForChannelAsync(context, channelStr.Trim(), cancellationToken);
        }
    }

    private static async ValueTask SendNamesForChannelAsync(CommandContext context, string channelStr, CancellationToken cancellationToken)
    {
        if (!ChannelName.TryCreate(channelStr, out var channelName, out _))
        {
            await context.ReplyAsync(
                IrcNumerics.EndOfNames(context.ServerName, context.User.Nickname.Value, channelStr),
                cancellationToken);
            return;
        }

        var channel = context.Channels.GetByName(channelName);
        if (channel is null)
        {
            await context.ReplyAsync(
                IrcNumerics.EndOfNames(context.ServerName, context.User.Nickname.Value, channelStr),
                cancellationToken);
            return;
        }

        // Skip secret channels unless the user is on them
        if (channel.Modes.HasFlag(ChannelMode.Secret) && !channel.HasMember(context.User.ConnectionId))
        {
            await context.ReplyAsync(
                IrcNumerics.EndOfNames(context.ServerName, context.User.Nickname.Value, channelStr),
                cancellationToken);
            return;
        }

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

        string channelType = channel.Modes.HasFlag(ChannelMode.Secret) ? "@" : "=";
        var namesLine = string.Join(" ", names);

        await context.ReplyAsync(
            IrcNumerics.NamReply(context.ServerName, context.User.Nickname.Value, channelType, channelStr, namesLine),
            cancellationToken);
        await context.ReplyAsync(
            IrcNumerics.EndOfNames(context.ServerName, context.User.Nickname.Value, channelStr),
            cancellationToken);
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
            if (mode.HasFlag(ChannelMemberMode.Owner)) return "~";
            if (mode.HasFlag(ChannelMemberMode.Admin)) return "&";
            if (mode.HasFlag(ChannelMemberMode.Op)) return "@";
            if (mode.HasFlag(ChannelMemberMode.HalfOp)) return "%";
            if (mode.HasFlag(ChannelMemberMode.Voice)) return "+";
            return "";
        }
    }
}
