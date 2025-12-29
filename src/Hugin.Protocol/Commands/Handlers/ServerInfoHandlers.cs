using System.Globalization;
using System.Text;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the VERSION command.
/// Returns server version information.
/// </summary>
public sealed class VersionHandler : CommandHandlerBase
{
    /// <summary>
    /// The current version of the Hugin IRC server.
    /// </summary>
    private const string ServerVersion = "hugin-0.1.0";

    /// <summary>
    /// Debug level indicator.
    /// </summary>
    private const string DebugLevel = "release";

    public override string Command => "VERSION";
    public override int MinimumParameters => 0;

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User.Nickname.Value;

        // 351 RPL_VERSION
        await context.ReplyAsync(
            IrcNumerics.Version(context.ServerName, nick, ServerVersion, DebugLevel, context.ServerName, "Modern IRC server with IRCv3 support"),
            cancellationToken);

        // Also send ISUPPORT (005) tokens if this is a post-registration VERSION request
        // Most servers send this, but it's optional
    }
}

/// <summary>
/// Handles the TIME command.
/// Returns the server's current local time.
/// </summary>
public sealed class TimeHandler : CommandHandlerBase
{
    public override string Command => "TIME";
    public override int MinimumParameters => 0;

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User.Nickname.Value;
        var timeString = DateTimeOffset.Now.ToString("ddd MMM dd yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);

        // 391 RPL_TIME
        await context.ReplyAsync(
            IrcNumerics.Time(context.ServerName, nick, context.ServerName, timeString),
            cancellationToken);
    }
}

/// <summary>
/// Handles the INFO command.
/// Returns information about the server.
/// </summary>
public sealed class InfoHandler : CommandHandlerBase
{
    /// <summary>
    /// Server information lines.
    /// </summary>
    private static readonly string[] InfoLines =
    [
        "Hugin IRC Server",
        "",
        "A modern, security-focused IRC server written in C#",
        "following RFC 1459/2812 and IRCv3 specifications.",
        "",
        "Features:",
        "- TLS 1.2/1.3 mandatory connections",
        "- SASL authentication (PLAIN, EXTERNAL)",
        "- IRCv3.2+ capabilities",
        "- Hostname cloaking",
        "- Rate limiting",
        "",
        "https://github.com/hugin-irc/hugin",
        "",
        "Written by the Hugin development team"
    ];

    public override string Command => "INFO";
    public override int MinimumParameters => 0;

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User.Nickname.Value;

        // Send each info line
        foreach (var line in InfoLines)
        {
            // 371 RPL_INFO
            await context.ReplyAsync(
                IrcNumerics.Info(context.ServerName, nick, line),
                cancellationToken);
        }

        // 374 RPL_ENDOFINFO
        await context.ReplyAsync(
            IrcNumerics.EndOfInfo(context.ServerName, nick),
            cancellationToken);
    }
}

/// <summary>
/// Handles the ADMIN command.
/// Returns administrative information about the server.
/// </summary>
public sealed class AdminHandler : CommandHandlerBase
{
    public override string Command => "ADMIN";
    public override int MinimumParameters => 0;

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User.Nickname.Value;

        // 256 RPL_ADMINME
        await context.ReplyAsync(
            IrcNumerics.AdminMe(context.ServerName, nick, context.ServerName),
            cancellationToken);

        // 257 RPL_ADMINLOC1
        await context.ReplyAsync(
            IrcNumerics.AdminLoc1(context.ServerName, nick, "Hugin IRC Network"),
            cancellationToken);

        // 258 RPL_ADMINLOC2
        await context.ReplyAsync(
            IrcNumerics.AdminLoc2(context.ServerName, nick, "Running Hugin IRC Server"),
            cancellationToken);

        // 259 RPL_ADMINEMAIL
        await context.ReplyAsync(
            IrcNumerics.AdminEmail(context.ServerName, nick, "admin@example.com"),
            cancellationToken);
    }
}

/// <summary>
/// Handles the USERHOST command.
/// Returns host information for up to 5 nicknames.
/// </summary>
public sealed class UserhostHandler : CommandHandlerBase
{
    /// <summary>
    /// Maximum number of nicknames to process.
    /// </summary>
    private const int MaxNicknames = 5;

    public override string Command => "USERHOST";
    public override int MinimumParameters => 1;

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User.Nickname.Value;
        var replies = new StringBuilder();
        var count = Math.Min(context.Message.Parameters.Count, MaxNicknames);

        for (var i = 0; i < count; i++)
        {
            var targetNickStr = context.Message.Parameters[i];
            if (!Nickname.TryCreate(targetNickStr, out var targetNickname, out _))
            {
                continue;
            }

            var targetUser = context.Users.GetByNickname(targetNickname);

            if (targetUser is not null)
            {
                if (replies.Length > 0)
                {
                    replies.Append(' ');
                }

                // Format: nickname[*]=±hostname
                // * indicates operator, + indicates not away, - indicates away
                var isOper = targetUser.Modes.HasFlag(Core.Enums.UserMode.Operator);
                var isAway = targetUser.IsAway;
                var operFlag = isOper ? "*" : "";
                var awayFlag = isAway ? "-" : "+";

                replies.Append(CultureInfo.InvariantCulture, $"{targetUser.Nickname.Value}{operFlag}={awayFlag}{targetUser.Username}@{targetUser.Hostname}");
            }
        }

        // 302 RPL_USERHOST
        await context.ReplyAsync(
            IrcNumerics.UserHost(context.ServerName, nick, replies.ToString()),
            cancellationToken);
    }
}

/// <summary>
/// Handles the ISON command.
/// Checks if specified nicknames are online.
/// </summary>
public sealed class IsonHandler : CommandHandlerBase
{
    public override string Command => "ISON";
    public override int MinimumParameters => 1;

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User.Nickname.Value;
        var onlineNicks = new StringBuilder();

        foreach (var targetNickStr in context.Message.Parameters)
        {
            if (!Nickname.TryCreate(targetNickStr, out var targetNickname, out _))
            {
                continue;
            }

            var targetUser = context.Users.GetByNickname(targetNickname);

            if (targetUser is not null)
            {
                if (onlineNicks.Length > 0)
                {
                    onlineNicks.Append(' ');
                }
                onlineNicks.Append(targetUser.Nickname.Value);
            }
        }

        // 303 RPL_ISON
        await context.ReplyAsync(
            IrcNumerics.Ison(context.ServerName, nick, onlineNicks.ToString()),
            cancellationToken);
    }
}

/// <summary>
/// Handles the SETNAME command (IRCv3).
/// Allows users to change their realname.
/// </summary>
public sealed class SetnameHandler : CommandHandlerBase
{
    public override string Command => "SETNAME";
    public override int MinimumParameters => 1;

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var newRealname = context.Message.Parameters[0];

        // Validate realname length
        if (string.IsNullOrWhiteSpace(newRealname) || newRealname.Length > 200)
        {
            // FAIL SETNAME INVALID_REALNAME :Realname is invalid
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "FAIL", "SETNAME", "INVALID_REALNAME", ":Realname is invalid"),
                cancellationToken);
            return;
        }

        // Update the realname (reuse SetUserInfo with existing username)
        context.User.SetUserInfo(context.User.Username, newRealname);

        // Send confirmation to the user
        // :nick!user@host SETNAME :New realname
        var source = $"{context.User.Nickname.Value}!{context.User.Username}@{context.User.Hostname}";
        var setnameMsg = IrcMessage.CreateWithSource(source, "SETNAME", $":{newRealname}");

        await context.ReplyAsync(setnameMsg, cancellationToken);

        // If setname capability is enabled, broadcast to shared channels
        if (context.Capabilities.HasSetname)
        {
            // Collect unique connection IDs from all shared channels
            var notifiedConnections = new HashSet<Guid> { context.User.ConnectionId };

            foreach (var channelEntry in context.User.Channels)
            {
                var channel = context.Channels.GetByName(channelEntry.Key);
                if (channel is not null)
                {
                    foreach (var memberEntry in channel.Members)
                    {
                        var memberId = memberEntry.Key;
                        if (notifiedConnections.Add(memberId))
                        {
                            await context.Broker.SendToConnectionAsync(memberId, setnameMsg.ToString(), cancellationToken);
                        }
                    }
                }
            }
        }
    }
}

/// <summary>
/// Handles the OPER command.
/// Authenticates a user as an IRC operator.
/// </summary>
public sealed class OperHandler : CommandHandlerBase
{
    /// <summary>
    /// Hardcoded operator credentials for testing.
    /// In production, these would come from configuration.
    /// </summary>
    private static readonly Dictionary<string, string> OperCredentials = new(StringComparer.OrdinalIgnoreCase)
    {
        ["admin"] = "admin123", // Example - in production use proper password hashing
        ["oper"] = "oper123"
    };

    public override string Command => "OPER";
    public override int MinimumParameters => 2;

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User.Nickname.Value;
        var operName = context.Message.Parameters[0];
        var operPassword = context.Message.Parameters[1];

        // Check if already an operator
        if (context.User.Modes.HasFlag(Core.Enums.UserMode.Operator))
        {
            // Already an operator, but we'll just re-send the success message
        }

        // Validate credentials
        if (!OperCredentials.TryGetValue(operName, out var expectedPassword) ||
            !string.Equals(operPassword, expectedPassword, StringComparison.Ordinal))
        {
            // 464 ERR_PASSWDMISMATCH
            await context.ReplyAsync(
                IrcNumerics.PasswordMismatch(context.ServerName, nick),
                cancellationToken);
            return;
        }

        // Grant operator status
        context.User.AddMode(Core.Enums.UserMode.Operator);

        // 381 RPL_YOUREOPER
        await context.ReplyAsync(
            IrcNumerics.YoureOper(context.ServerName, nick),
            cancellationToken);

        // Send MODE change to user
        var modeMsg = IrcMessage.CreateWithSource(context.ServerName, "MODE", nick, "+o");
        await context.ReplyAsync(modeMsg, cancellationToken);
    }
}
