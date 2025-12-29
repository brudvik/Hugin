using System.Collections.Concurrent;
using System.Globalization;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the WHOWAS command.
/// Returns historical information about users who have disconnected.
/// </summary>
public sealed class WhowasHandler : CommandHandlerBase
{
    /// <summary>
    /// Maximum number of entries to return by default.
    /// </summary>
    private const int DefaultMaxEntries = 10;

    /// <summary>
    /// Static storage for WHOWAS history.
    /// In production, this would be injected as a service.
    /// </summary>
    private static readonly ConcurrentDictionary<string, List<WhowasEntry>> WhowasHistory = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maximum entries to keep per nickname.
    /// </summary>
    private const int MaxEntriesPerNick = 10;

    public override string Command => "WHOWAS";
    public override int MinimumParameters => 1;

    /// <summary>
    /// Records a user's information when they disconnect.
    /// Call this from the QUIT handler.
    /// </summary>
    /// <param name="nickname">The user's nickname.</param>
    /// <param name="username">The user's username.</param>
    /// <param name="hostname">The user's hostname.</param>
    /// <param name="realname">The user's realname.</param>
    /// <param name="serverName">The server name.</param>
    public static void RecordUser(string nickname, string username, string hostname, string realname, string serverName)
    {
        var entry = new WhowasEntry(nickname, username, hostname, realname, serverName, DateTimeOffset.UtcNow);

        WhowasHistory.AddOrUpdate(
            nickname,
            _ => [entry],
            (_, list) =>
            {
                lock (list)
                {
                    list.Insert(0, entry);
                    if (list.Count > MaxEntriesPerNick)
                    {
                        list.RemoveAt(list.Count - 1);
                    }
                }
                return list;
            });
    }

    /// <summary>
    /// Records a user in the WHOWAS history.
    /// Convenience overload that extracts information from a User entity.
    /// </summary>
    /// <param name="user">The user to record.</param>
    /// <param name="serverName">The server name.</param>
    public static void RecordUser(User user, string serverName)
    {
        if (user.Nickname == null) return;
        
        RecordUser(
            user.Nickname.Value,
            user.Username ?? "*",
            user.DisplayedHostname,
            user.RealName ?? "",
            serverName);
    }

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User.Nickname.Value;
        var targetNick = context.Message.Parameters[0];

        // Parse optional count parameter
        var maxEntries = DefaultMaxEntries;
        if (context.Message.Parameters.Count > 1 &&
            int.TryParse(context.Message.Parameters[1], out var count) &&
            count > 0)
        {
            maxEntries = Math.Min(count, DefaultMaxEntries);
        }

        if (WhowasHistory.TryGetValue(targetNick, out var entries) && entries.Count > 0)
        {
            // Copy entries under lock, then iterate outside the lock to allow await
            List<WhowasEntry> entriesToShow;
            lock (entries)
            {
                entriesToShow = entries.Take(maxEntries).ToList();
            }

            foreach (var entry in entriesToShow)
            {
                // 314 RPL_WHOWASUSER
                await context.ReplyAsync(
                    IrcNumerics.WhowasUser(context.ServerName, nick, entry.Nickname, entry.Username, entry.Hostname, entry.Realname),
                    cancellationToken);

                // 312 RPL_WHOISSERVER (reused for WHOWAS)
                await context.ReplyAsync(
                    IrcNumerics.WhoisServer(context.ServerName, nick, entry.Nickname, entry.ServerName, entry.Timestamp.ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture)),
                    cancellationToken);
            }
        }
        else
        {
            // 406 ERR_WASNOSUCHNICK
            await context.ReplyAsync(
                IrcNumerics.WasNoSuchNick(context.ServerName, nick, targetNick),
                cancellationToken);
        }

        // 369 RPL_ENDOFWHOWAS
        await context.ReplyAsync(
            IrcNumerics.EndOfWhowas(context.ServerName, nick, targetNick),
            cancellationToken);
    }

    /// <summary>
    /// Represents a WHOWAS history entry.
    /// </summary>
    private sealed record WhowasEntry(
        string Nickname,
        string Username,
        string Hostname,
        string Realname,
        string ServerName,
        DateTimeOffset Timestamp);
}

/// <summary>
/// Handles the KILL command.
/// Disconnects a user from the server (operator only).
/// </summary>
public sealed class KillHandler : CommandHandlerBase
{
    public override string Command => "KILL";
    public override int MinimumParameters => 2;

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User.Nickname.Value;

        // Check if user is an operator
        if (!context.User.Modes.HasFlag(UserMode.Operator))
        {
            await context.ReplyAsync(
                IrcNumerics.NoPrivileges(context.ServerName, nick),
                cancellationToken);
            return;
        }

        var targetNickStr = context.Message.Parameters[0];
        var reason = context.Message.Parameters.Count > 1 ? context.Message.Parameters[1] : "Killed";

        if (!Nickname.TryCreate(targetNickStr, out var targetNickname, out _))
        {
            await SendNoSuchNickAsync(context, targetNickStr, cancellationToken);
            return;
        }

        var targetUser = context.Users.GetByNickname(targetNickname);
        if (targetUser is null)
        {
            await SendNoSuchNickAsync(context, targetNickStr, cancellationToken);
            return;
        }

        // Cannot kill yourself
        if (targetUser.ConnectionId == context.User.ConnectionId)
        {
            // Some servers allow this, but we'll prevent it
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick, ":Cannot KILL yourself"),
                cancellationToken);
            return;
        }

        // Record in WHOWAS history
        WhowasHandler.RecordUser(
            targetUser.Nickname.Value,
            targetUser.Username,
            targetUser.Hostname,
            targetUser.RealName,
            context.ServerName);

        // Send KILL message to the target
        var killMessage = $"Killed ({nick} ({reason}))";
        var errorMsg = IrcMessage.Create("ERROR", $":Closing Link: {targetUser.Hostname} ({killMessage})");
        await context.Broker.SendToConnectionAsync(targetUser.ConnectionId, errorMsg.ToString(), cancellationToken);

        // Broadcast QUIT to all shared channels
        var quitMsg = IrcMessage.CreateWithSource(
            $"{targetUser.Nickname.Value}!{targetUser.Username}@{targetUser.Hostname}",
            "QUIT",
            $":Killed ({nick} ({reason}))");

        // Get all channels the target is in and broadcast
        foreach (var channelEntry in targetUser.Channels)
        {
            var channel = context.Channels.GetByName(channelEntry.Key);
            if (channel is not null)
            {
                foreach (var memberEntry in channel.Members)
                {
                    if (memberEntry.Key != targetUser.ConnectionId)
                    {
                        await context.Broker.SendToConnectionAsync(memberEntry.Key, quitMsg.ToString(), cancellationToken);
                    }
                }
                // Remove from channel
                channel.RemoveMember(targetUser.ConnectionId);
            }
        }

        // Remove user from repository
        context.Users.Remove(targetUser.ConnectionId);

        // Close the connection (in a real implementation, this would signal the network layer)
        // For now, we just remove the user - the connection manager would handle the actual disconnect
    }
}

/// <summary>
/// Handles the WALLOPS command.
/// Sends a message to all operators.
/// </summary>
public sealed class WallopsHandler : CommandHandlerBase
{
    public override string Command => "WALLOPS";
    public override int MinimumParameters => 1;

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User.Nickname.Value;

        // Check if user is an operator
        if (!context.User.Modes.HasFlag(UserMode.Operator))
        {
            await context.ReplyAsync(
                IrcNumerics.NoPrivileges(context.ServerName, nick),
                cancellationToken);
            return;
        }

        var message = context.Message.Parameters[0];
        var source = $"{context.User.Nickname.Value}!{context.User.Username}@{context.User.Hostname}";
        var wallopsMsg = IrcMessage.CreateWithSource(source, "WALLOPS", $":{message}");

        // Send to all users with +w (wallops) mode
        foreach (var user in context.Users.GetAll())
        {
            if (user.Modes.HasFlag(UserMode.Wallops) || user.Modes.HasFlag(UserMode.Operator))
            {
                await context.Broker.SendToConnectionAsync(user.ConnectionId, wallopsMsg.ToString(), cancellationToken);
            }
        }
    }
}

/// <summary>
/// Handles the STATS command.
/// Returns server statistics (operator only for some queries).
/// </summary>
public sealed class StatsHandler : CommandHandlerBase
{
    public override string Command => "STATS";
    public override int MinimumParameters => 0;

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User.Nickname.Value;

        // Default to 'u' (uptime) if no parameter
        var query = context.Message.Parameters.Count > 0 ? context.Message.Parameters[0] : "u";

        if (query.Length == 0)
        {
            query = "u";
        }

        var queryChar = char.ToUpperInvariant(query[0]);

        switch (queryChar)
        {
            case 'U': // Uptime
                await SendUptimeAsync(context, nick, cancellationToken);
                break;

            case 'M': // Commands and their usage counts
                await SendCommandStatsAsync(context, nick, cancellationToken);
                break;

            case 'O': // O-lines (operator blocks) - requires oper
                if (!context.User.Modes.HasFlag(UserMode.Operator))
                {
                    await context.ReplyAsync(
                        IrcNumerics.NoPrivileges(context.ServerName, nick),
                        cancellationToken);
                    return;
                }
                await SendOlinesAsync(context, nick, cancellationToken);
                break;

            case 'L': // Connections - requires oper
                if (!context.User.Modes.HasFlag(UserMode.Operator))
                {
                    await context.ReplyAsync(
                        IrcNumerics.NoPrivileges(context.ServerName, nick),
                        cancellationToken);
                    return;
                }
                await SendConnectionStatsAsync(context, nick, cancellationToken);
                break;

            default:
                // Unknown stats query - just send end
                break;
        }

        // 219 RPL_ENDOFSTATS
        await context.ReplyAsync(
            IrcNumerics.EndOfStats(context.ServerName, nick, queryChar.ToString()),
            cancellationToken);
    }

    private static async ValueTask SendUptimeAsync(CommandContext context, string nick, CancellationToken cancellationToken = default)
    {
        // Calculate uptime (using process start time as approximation)
        var uptime = DateTimeOffset.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var days = (int)uptime.TotalDays;
        var hours = uptime.Hours;
        var minutes = uptime.Minutes;
        var seconds = uptime.Seconds;

        // 242 RPL_STATSUPTIME
        await context.ReplyAsync(
            IrcNumerics.StatsUptime(context.ServerName, nick, days, hours, minutes, seconds),
            cancellationToken);
    }

    private static async ValueTask SendCommandStatsAsync(CommandContext context, string nick, CancellationToken cancellationToken = default)
    {
        // 212 RPL_STATSCOMMANDS - simplified version
        // In a real implementation, you'd track command usage
        await context.ReplyAsync(
            IrcNumerics.StatsCommands(context.ServerName, nick, "PRIVMSG", 0, 0, 0),
            cancellationToken);
    }

    private static async ValueTask SendOlinesAsync(CommandContext context, string nick, CancellationToken cancellationToken = default)
    {
        // 243 RPL_STATSOLINE
        await context.ReplyAsync(
            IrcNumerics.StatsOline(context.ServerName, nick, "*", "admin", "Administrator"),
            cancellationToken);
        await context.ReplyAsync(
            IrcNumerics.StatsOline(context.ServerName, nick, "*", "oper", "Operator"),
            cancellationToken);
    }

    private static async ValueTask SendConnectionStatsAsync(CommandContext context, string nick, CancellationToken cancellationToken = default)
    {
        // 211 RPL_STATSLINKINFO
        var users = context.Users.GetAll().ToList();
        foreach (var user in users.Take(10)) // Limit to 10 for safety
        {
            await context.ReplyAsync(
                IrcNumerics.StatsLinkInfo(context.ServerName, nick, user.Nickname?.Value ?? "*", 0, 0, 0, 0, 0),
                cancellationToken);
        }
    }
}

/// <summary>
/// Handles the REHASH command.
/// Reloads the server configuration. Operator only.
/// </summary>
public sealed class RehashHandler : CommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "REHASH";

    /// <inheritdoc />
    public override int MinimumParameters => 0;

    /// <inheritdoc />
    public override bool RequiresRegistration => true;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User?.Nickname?.Value ?? "*";

        // Check operator privileges
        if (context.User == null || !context.User.IsOperator)
        {
            await context.ReplyAsync(
                IrcNumerics.NoPrivileges(context.ServerName, nick),
                cancellationToken);
            return;
        }

        // 382 RPL_REHASHING
        await context.ReplyAsync(
            IrcNumerics.Rehashing(context.ServerName, nick, "hugin.conf"),
            cancellationToken);

        // In a real implementation, this would signal the server to reload config
        // For now, we just send the acknowledgment
    }
}

/// <summary>
/// Handles the DIE command.
/// Shuts down the server. Operator only.
/// </summary>
public sealed class DieHandler : CommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "DIE";

    /// <inheritdoc />
    public override int MinimumParameters => 0;

    /// <inheritdoc />
    public override bool RequiresRegistration => true;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User?.Nickname?.Value ?? "*";

        // Check operator privileges
        if (context.User == null || !context.User.IsOperator)
        {
            await context.ReplyAsync(
                IrcNumerics.NoPrivileges(context.ServerName, nick),
                cancellationToken);
            return;
        }

        // Broadcast shutdown notice to all users
        var noticeMsg = IrcMessage.CreateWithSource(context.ServerName, "NOTICE", "*", "Server is shutting down");
        foreach (var user in context.Users.GetAll())
        {
            await context.Broker.SendToConnectionAsync(user.ConnectionId, noticeMsg.ToString(), cancellationToken);
        }

        // In a real implementation, this would signal the server to shut down gracefully
        // The hosting layer would need to handle the actual shutdown
    }
}

/// <summary>
/// Handles the RESTART command.
/// Restarts the server. Operator only.
/// </summary>
public sealed class RestartHandler : CommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "RESTART";

    /// <inheritdoc />
    public override int MinimumParameters => 0;

    /// <inheritdoc />
    public override bool RequiresRegistration => true;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User?.Nickname?.Value ?? "*";

        // Check operator privileges
        if (context.User == null || !context.User.IsOperator)
        {
            await context.ReplyAsync(
                IrcNumerics.NoPrivileges(context.ServerName, nick),
                cancellationToken);
            return;
        }

        // Broadcast restart notice to all users
        var noticeMsg = IrcMessage.CreateWithSource(context.ServerName, "NOTICE", "*", "Server is restarting");
        foreach (var user in context.Users.GetAll())
        {
            await context.Broker.SendToConnectionAsync(user.ConnectionId, noticeMsg.ToString(), cancellationToken);
        }

        // In a real implementation, this would signal the server to restart gracefully
        // The hosting layer would need to handle the actual restart
    }
}
