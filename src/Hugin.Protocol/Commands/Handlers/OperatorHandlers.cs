using System.Collections.Concurrent;
using System.Globalization;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Hugin.Core.Interfaces;
using Hugin.Core.Plugins;
using Hugin.Core.Scripting;
using Hugin.Core.Triggers;
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

            case 'C': // Server links (C-lines) - requires oper
                if (!context.User.Modes.HasFlag(UserMode.Operator))
                {
                    await context.ReplyAsync(
                        IrcNumerics.NoPrivileges(context.ServerName, nick),
                        cancellationToken);
                    return;
                }
                await SendServerLinksAsync(context, nick, cancellationToken);
                break;

            case 'K': // K-lines (bans) - requires oper
                if (!context.User.Modes.HasFlag(UserMode.Operator))
                {
                    await context.ReplyAsync(
                        IrcNumerics.NoPrivileges(context.ServerName, nick),
                        cancellationToken);
                    return;
                }
                await SendKlinesAsync(context, nick, cancellationToken);
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

    private static async ValueTask SendServerLinksAsync(CommandContext context, string nick, CancellationToken cancellationToken = default)
    {
        // 213 RPL_STATSCLINE - Server connections (C-lines)
        // In a real implementation, this would come from IServerLinkManager
        await context.ReplyAsync(
            IrcMessage.CreateWithSource(
                context.ServerName,
                "213", // RPL_STATSCLINE
                nick,
                "C",
                "*",
                "*",
                "0",
                ":No server links configured"),
            cancellationToken);
    }

    private static async ValueTask SendKlinesAsync(CommandContext context, string nick, CancellationToken cancellationToken = default)
    {
        // 216 RPL_STATSKLINE - K-lines (bans)
        var banRepo = context.ServiceProvider(typeof(Core.Interfaces.IServerBanRepository))
            as Core.Interfaces.IServerBanRepository;

        if (banRepo != null)
        {
            var klines = banRepo.GetByType(Core.Entities.BanType.KLine);
            foreach (var kline in klines.Take(50)) // Limit for safety
            {
                await context.ReplyAsync(
                    IrcMessage.CreateWithSource(
                        context.ServerName,
                        "216", // RPL_STATSKLINE
                        nick,
                        "K",
                        kline.Pattern,
                        "*",
                        kline.ExpiresAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "permanent",
                        $":{kline.Reason}"),
                    cancellationToken);
            }

            var glines = banRepo.GetByType(Core.Entities.BanType.GLine);
            foreach (var gline in glines.Take(50))
            {
                await context.ReplyAsync(
                    IrcMessage.CreateWithSource(
                        context.ServerName,
                        "223", // RPL_STATSGLINE
                        nick,
                        "G",
                        gline.Pattern,
                        "*",
                        gline.ExpiresAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "permanent",
                        $":{gline.Reason}"),
                    cancellationToken);
            }
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

    /// <summary>
    /// Optional specific subsystem to rehash.
    /// </summary>
    private static readonly HashSet<string> ValidSubsystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "MOTD", "OPER", "TLS", "CERTS", "DNS", "ALL"
    };

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

        var subsystem = context.Message.Parameters.Count > 0
            ? context.Message.Parameters[0].ToUpperInvariant()
            : "ALL";

        // Validate subsystem if specified
        if (!ValidSubsystems.Contains(subsystem))
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $":Unknown REHASH target: {subsystem}. Valid targets: {string.Join(", ", ValidSubsystems)}"),
                cancellationToken);
            return;
        }

        // 382 RPL_REHASHING
        await context.ReplyAsync(
            IrcNumerics.Rehashing(context.ServerName, nick, subsystem == "ALL" ? "hugin.conf" : subsystem),
            cancellationToken);

        // Try to get the reloader from context service provider
        var reloader = context.ServiceProvider(typeof(Core.Interfaces.IConfigurationReloader))
            as Core.Interfaces.IConfigurationReloader;

        if (reloader != null)
        {
            var report = await reloader.ReloadAllAsync(cancellationToken);

            if (report.AllSucceeded)
            {
                await context.ReplyAsync(
                    IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                        $":Configuration reloaded successfully ({report.Succeeded.Count} components)"),
                    cancellationToken);
            }
            else
            {
                await context.ReplyAsync(
                    IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                        $":Configuration reload partial: {report.Succeeded.Count} succeeded, {report.Failed.Count} failed"),
                    cancellationToken);

                foreach (var failed in report.Failed)
                {
                    await context.ReplyAsync(
                        IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                            $":Failed to reload: {failed}"),
                        cancellationToken);
                }
            }
        }
        else
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    ":Configuration reload requested"),
                cancellationToken);
        }
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

/// <summary>
/// Handles the LOADMOD command - loads a dynamic module.
/// Syntax: LOADMOD &lt;module_id&gt;
/// </summary>
public sealed class LoadModHandler : CommandHandlerBase
{
    private readonly IModuleManager? _moduleManager;

    /// <summary>
    /// Creates a new LOADMOD handler.
    /// </summary>
    /// <param name="moduleManager">Optional module manager.</param>
    public LoadModHandler(IModuleManager? moduleManager = null)
    {
        _moduleManager = moduleManager;
    }

    /// <inheritdoc />
    public override string Command => "LOADMOD";

    /// <inheritdoc />
    public override int MinimumParameters => 1;

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

        if (_moduleManager is null)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    "Module system is not available"),
                cancellationToken);
            return;
        }

        var moduleId = context.Message.Parameters[0];

        // Check if already loaded
        if (_moduleManager.IsModuleLoaded(moduleId))
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Module {moduleId} is already loaded"),
                cancellationToken);
            return;
        }

        // Attempt to load
        var success = await _moduleManager.LoadModuleAsync(moduleId, cancellationToken);

        if (success)
        {
            var info = _moduleManager.GetModuleInfo(moduleId);
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Loaded module: {info?.Name ?? moduleId} v{info?.Version ?? "?"}"),
                cancellationToken);
        }
        else
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Failed to load module {moduleId}"),
                cancellationToken);
        }
    }
}

/// <summary>
/// Handles the UNLOADMOD command - unloads a dynamic module.
/// Syntax: UNLOADMOD &lt;module_id&gt;
/// </summary>
public sealed class UnloadModHandler : CommandHandlerBase
{
    private readonly IModuleManager? _moduleManager;

    /// <summary>
    /// Creates a new UNLOADMOD handler.
    /// </summary>
    /// <param name="moduleManager">Optional module manager.</param>
    public UnloadModHandler(IModuleManager? moduleManager = null)
    {
        _moduleManager = moduleManager;
    }

    /// <inheritdoc />
    public override string Command => "UNLOADMOD";

    /// <inheritdoc />
    public override int MinimumParameters => 1;

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

        if (_moduleManager is null)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    "Module system is not available"),
                cancellationToken);
            return;
        }

        var moduleId = context.Message.Parameters[0];

        // Check if loaded
        if (!_moduleManager.IsModuleLoaded(moduleId))
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Module {moduleId} is not loaded"),
                cancellationToken);
            return;
        }

        var info = _moduleManager.GetModuleInfo(moduleId);

        // Attempt to unload
        var success = await _moduleManager.UnloadModuleAsync(moduleId, cancellationToken);

        if (success)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Unloaded module: {info?.Name ?? moduleId}"),
                cancellationToken);
        }
        else
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Failed to unload module {moduleId}"),
                cancellationToken);
        }
    }
}

/// <summary>
/// Handles the MODLIST command - lists loaded modules.
/// Syntax: MODLIST
/// </summary>
public sealed class ModListHandler : CommandHandlerBase
{
    private readonly IModuleManager? _moduleManager;

    /// <summary>
    /// Creates a new MODLIST handler.
    /// </summary>
    /// <param name="moduleManager">Optional module manager.</param>
    public ModListHandler(IModuleManager? moduleManager = null)
    {
        _moduleManager = moduleManager;
    }

    /// <inheritdoc />
    public override string Command => "MODLIST";

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

        if (_moduleManager is null)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    "Module system is not available"),
                cancellationToken);
            return;
        }

        var modules = _moduleManager.LoadedModules;

        await context.ReplyAsync(
            IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                $"Loaded modules: {modules.Count}"),
            cancellationToken);

        foreach (var module in modules)
        {
            var loadedAt = module.LoadedAt?.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"  {module.Id}: {module.Name} v{module.Version} by {module.Author ?? "unknown"} (loaded: {loadedAt})"),
                cancellationToken);
        }

        await context.ReplyAsync(
            IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                "End of module list"),
            cancellationToken);
    }
}

/// <summary>
/// Handles the LOADSCRIPT command.
/// Loads a Lua script from the scripts directory.
/// Syntax: LOADSCRIPT &lt;filename&gt;
/// </summary>
public sealed class LoadScriptHandler : CommandHandlerBase
{
    private readonly ILuaScriptEngine? _scriptEngine;

    /// <summary>
    /// Creates a new LOADSCRIPT handler.
    /// </summary>
    /// <param name="scriptEngine">Optional script engine.</param>
    public LoadScriptHandler(ILuaScriptEngine? scriptEngine = null)
    {
        _scriptEngine = scriptEngine;
    }

    /// <inheritdoc />
    public override string Command => "LOADSCRIPT";

    /// <inheritdoc />
    public override int MinimumParameters => 1;

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

        if (_scriptEngine is null)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    "Lua scripting is not available"),
                cancellationToken);
            return;
        }

        var filename = context.Message.Parameters[0];

        // Ensure .lua extension
        if (!filename.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
        {
            filename += ".lua";
        }

        // Build full path (scripts directory)
        var scriptsDir = Path.Combine(AppContext.BaseDirectory, "scripts");
        var fullPath = Path.Combine(scriptsDir, filename);

        // Security: ensure path is within scripts directory
        var normalizedPath = Path.GetFullPath(fullPath);
        var normalizedScriptsDir = Path.GetFullPath(scriptsDir);

        if (!normalizedPath.StartsWith(normalizedScriptsDir, StringComparison.OrdinalIgnoreCase))
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    "Invalid script path"),
                cancellationToken);
            return;
        }

        var script = await _scriptEngine.LoadScriptAsync(fullPath, cancellationToken);

        if (script != null)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Loaded script: {script.Name} ({script.Id}) with {script.RegisteredHandlers.Count} handlers"),
                cancellationToken);
        }
        else
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Failed to load script: {filename}"),
                cancellationToken);
        }
    }
}

/// <summary>
/// Handles the UNLOADSCRIPT command.
/// Unloads a Lua script by ID.
/// Syntax: UNLOADSCRIPT &lt;script_id&gt;
/// </summary>
public sealed class UnloadScriptHandler : CommandHandlerBase
{
    private readonly ILuaScriptEngine? _scriptEngine;

    /// <summary>
    /// Creates a new UNLOADSCRIPT handler.
    /// </summary>
    /// <param name="scriptEngine">Optional script engine.</param>
    public UnloadScriptHandler(ILuaScriptEngine? scriptEngine = null)
    {
        _scriptEngine = scriptEngine;
    }

    /// <inheritdoc />
    public override string Command => "UNLOADSCRIPT";

    /// <inheritdoc />
    public override int MinimumParameters => 1;

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

        if (_scriptEngine is null)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    "Lua scripting is not available"),
                cancellationToken);
            return;
        }

        var scriptId = context.Message.Parameters[0];

        if (_scriptEngine.UnloadScript(scriptId))
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Unloaded script: {scriptId}"),
                cancellationToken);
        }
        else
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Script not found: {scriptId}"),
                cancellationToken);
        }
    }
}

/// <summary>
/// Handles the SCRIPTS command.
/// Lists all loaded Lua scripts.
/// Syntax: SCRIPTS [script_id]
/// </summary>
public sealed class ScriptsHandler : CommandHandlerBase
{
    private readonly ILuaScriptEngine? _scriptEngine;

    /// <summary>
    /// Creates a new SCRIPTS handler.
    /// </summary>
    /// <param name="scriptEngine">Optional script engine.</param>
    public ScriptsHandler(ILuaScriptEngine? scriptEngine = null)
    {
        _scriptEngine = scriptEngine;
    }

    /// <inheritdoc />
    public override string Command => "SCRIPTS";

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

        if (_scriptEngine is null)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    "Lua scripting is not available"),
                cancellationToken);
            return;
        }

        // If a script ID is provided, show detailed info
        if (context.Message.Parameters.Count > 0)
        {
            var scriptId = context.Message.Parameters[0];
            var script = _scriptEngine.LoadedScripts.FirstOrDefault(s =>
                s.Id.Equals(scriptId, StringComparison.OrdinalIgnoreCase));

            if (script == null)
            {
                await context.ReplyAsync(
                    IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                        $"Script not found: {scriptId}"),
                    cancellationToken);
                return;
            }

            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Script: {script.Name} (ID: {script.Id})"),
                cancellationToken);

            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"  Version: {script.Version ?? "unknown"}, Author: {script.Author ?? "unknown"}"),
                cancellationToken);

            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"  Status: {(script.IsEnabled ? "enabled" : "disabled")}, Loaded: {script.LoadedAt:yyyy-MM-dd HH:mm:ss}"),
                cancellationToken);

            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"  Handlers: {string.Join(", ", script.RegisteredHandlers)}"),
                cancellationToken);

            var stats = _scriptEngine.GetStatistics(scriptId);
            if (stats != null)
            {
                await context.ReplyAsync(
                    IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                        $"  Stats: {stats.TotalCalls} calls, {stats.TotalExecutionTimeMs:F2}ms total, {stats.ErrorCount} errors"),
                    cancellationToken);
            }

            return;
        }

        // List all scripts
        var scripts = _scriptEngine.LoadedScripts;

        await context.ReplyAsync(
            IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                $"Loaded scripts: {scripts.Count}"),
            cancellationToken);

        foreach (var script in scripts)
        {
            var status = script.IsEnabled ? "+" : "-";
            var handlerCount = script.RegisteredHandlers.Count;
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"  [{status}] {script.Id}: {script.Name} v{script.Version ?? "?"} ({handlerCount} handlers)"),
                cancellationToken);
        }

        await context.ReplyAsync(
            IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                "End of scripts list"),
            cancellationToken);
    }
}

/// <summary>
/// Handles the RELOADSCRIPT command.
/// Reloads a Lua script by ID.
/// Syntax: RELOADSCRIPT &lt;script_id&gt;
/// </summary>
public sealed class ReloadScriptHandler : CommandHandlerBase
{
    private readonly ILuaScriptEngine? _scriptEngine;

    /// <summary>
    /// Creates a new RELOADSCRIPT handler.
    /// </summary>
    /// <param name="scriptEngine">Optional script engine.</param>
    public ReloadScriptHandler(ILuaScriptEngine? scriptEngine = null)
    {
        _scriptEngine = scriptEngine;
    }

    /// <inheritdoc />
    public override string Command => "RELOADSCRIPT";

    /// <inheritdoc />
    public override int MinimumParameters => 1;

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

        if (_scriptEngine is null)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    "Lua scripting is not available"),
                cancellationToken);
            return;
        }

        var scriptId = context.Message.Parameters[0];

        var script = await _scriptEngine.ReloadScriptAsync(scriptId, cancellationToken);

        if (script != null)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Reloaded script: {script.Name} ({script.Id}) with {script.RegisteredHandlers.Count} handlers"),
                cancellationToken);
        }
        else
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Failed to reload script: {scriptId}"),
                cancellationToken);
        }
    }
}

/// <summary>
/// Handles the TRIGGERS command.
/// Lists all loaded triggers or shows details for a specific trigger.
/// Syntax: TRIGGERS [trigger_id]
/// </summary>
public sealed class TriggersHandler : CommandHandlerBase
{
    private readonly ITriggerManager? _triggerManager;

    /// <summary>
    /// Creates a new TRIGGERS handler.
    /// </summary>
    /// <param name="triggerManager">Optional trigger manager.</param>
    public TriggersHandler(ITriggerManager? triggerManager = null)
    {
        _triggerManager = triggerManager;
    }

    /// <inheritdoc />
    public override string Command => "TRIGGERS";

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

        if (_triggerManager is null)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    "Trigger system is not available"),
                cancellationToken);
            return;
        }

        // If a trigger ID is provided, show detailed info
        if (context.Message.Parameters.Count > 0)
        {
            var triggerId = context.Message.Parameters[0];
            var trigger = _triggerManager.GetTrigger(triggerId);

            if (trigger == null)
            {
                await context.ReplyAsync(
                    IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                        $"Trigger not found: {triggerId}"),
                    cancellationToken);
                return;
            }

            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Trigger: {trigger.Name ?? trigger.Id} (ID: {trigger.Id})"),
                cancellationToken);

            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"  Status: {(trigger.Enabled ? "enabled" : "disabled")}, Priority: {trigger.Priority}"),
                cancellationToken);

            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"  Events: {string.Join(", ", trigger.Events)}"),
                cancellationToken);

            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"  Conditions: {trigger.Conditions.Count}, Actions: {trigger.Actions.Count}"),
                cancellationToken);

            var stats = _triggerManager.GetStatistics(triggerId);
            if (stats != null)
            {
                var lastFired = stats.LastFired?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "never";
                await context.ReplyAsync(
                    IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                        $"  Stats: {stats.FireCount} fires, last: {lastFired}"),
                    cancellationToken);
            }

            return;
        }

        // List all triggers
        var triggers = _triggerManager.Triggers;

        await context.ReplyAsync(
            IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                $"Loaded triggers: {triggers.Count}"),
            cancellationToken);

        foreach (var trigger in triggers)
        {
            var status = trigger.Enabled ? "+" : "-";
            var events = string.Join(",", trigger.Events.Take(3));
            if (trigger.Events.Count > 3) events += "...";

            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"  [{status}] {trigger.Id}: {trigger.Name ?? "unnamed"} [{events}] (pri:{trigger.Priority})"),
                cancellationToken);
        }

        await context.ReplyAsync(
            IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                "End of triggers list"),
            cancellationToken);
    }
}

/// <summary>
/// Handles the TRIGGERENABLE command.
/// Enables or disables a trigger.
/// Syntax: TRIGGERENABLE &lt;trigger_id&gt; &lt;on|off&gt;
/// </summary>
public sealed class TriggerEnableHandler : CommandHandlerBase
{
    private readonly ITriggerManager? _triggerManager;

    /// <summary>
    /// Creates a new TRIGGERENABLE handler.
    /// </summary>
    /// <param name="triggerManager">Optional trigger manager.</param>
    public TriggerEnableHandler(ITriggerManager? triggerManager = null)
    {
        _triggerManager = triggerManager;
    }

    /// <inheritdoc />
    public override string Command => "TRIGGERENABLE";

    /// <inheritdoc />
    public override int MinimumParameters => 2;

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

        if (_triggerManager is null)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    "Trigger system is not available"),
                cancellationToken);
            return;
        }

        var triggerId = context.Message.Parameters[0];
        var state = context.Message.Parameters[1].ToUpperInvariant();
        var enabled = state is "ON" or "1" or "TRUE" or "ENABLE" or "YES";

        if (_triggerManager.SetTriggerEnabled(triggerId, enabled))
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Trigger {triggerId} {(enabled ? "enabled" : "disabled")}"),
                cancellationToken);
        }
        else
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Trigger not found: {triggerId}"),
                cancellationToken);
        }
    }
}

/// <summary>
/// Handles the RELOADTRIGGERS command.
/// Reloads all triggers from their source files.
/// Syntax: RELOADTRIGGERS
/// </summary>
public sealed class ReloadTriggersHandler : CommandHandlerBase
{
    private readonly ITriggerManager? _triggerManager;

    /// <summary>
    /// Creates a new RELOADTRIGGERS handler.
    /// </summary>
    /// <param name="triggerManager">Optional trigger manager.</param>
    public ReloadTriggersHandler(ITriggerManager? triggerManager = null)
    {
        _triggerManager = triggerManager;
    }

    /// <inheritdoc />
    public override string Command => "RELOADTRIGGERS";

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

        if (_triggerManager is null)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    "Trigger system is not available"),
                cancellationToken);
            return;
        }

        var count = await _triggerManager.ReloadTriggersAsync(cancellationToken);

        await context.ReplyAsync(
            IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                $"Reloaded {count} triggers"),
            cancellationToken);
    }
}

/// <summary>
/// Handles the PLUGINS command.
/// Lists all discovered plugins or shows details for a specific plugin.
/// Syntax: PLUGINS [plugin_id]
/// </summary>
public sealed class PluginsHandler : CommandHandlerBase
{
    private readonly IPluginManager? _pluginManager;

    /// <summary>
    /// Creates a new PLUGINS handler.
    /// </summary>
    /// <param name="pluginManager">Optional plugin manager.</param>
    public PluginsHandler(IPluginManager? pluginManager = null)
    {
        _pluginManager = pluginManager;
    }

    /// <inheritdoc />
    public override string Command => "PLUGINS";

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

        if (_pluginManager is null)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    "Plugin system is not available"),
                cancellationToken);
            return;
        }

        // If a plugin ID is provided, show detailed info
        if (context.Message.Parameters.Count > 0)
        {
            var pluginId = context.Message.Parameters[0];
            var plugin = _pluginManager.GetPlugin(pluginId);

            if (plugin == null)
            {
                await context.ReplyAsync(
                    IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                        $"Plugin not found: {pluginId}"),
                    cancellationToken);
                return;
            }

            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Plugin: {plugin.Manifest.Name} (ID: {plugin.Manifest.Id})"),
                cancellationToken);

            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"  Version: {plugin.Manifest.Version}, Author: {plugin.Manifest.Author ?? "unknown"}"),
                cancellationToken);

            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"  Status: {(plugin.IsLoaded ? "loaded" : "not loaded")}, Enabled: {(plugin.IsEnabled ? "yes" : "no")}"),
                cancellationToken);

            if (!string.IsNullOrEmpty(plugin.Manifest.Description))
            {
                await context.ReplyAsync(
                    IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                        $"  Description: {plugin.Manifest.Description}"),
                    cancellationToken);
            }

            if (plugin.LoadedAt.HasValue)
            {
                await context.ReplyAsync(
                    IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                        $"  Loaded: {plugin.LoadedAt.Value:yyyy-MM-dd HH:mm:ss}"),
                    cancellationToken);
            }

            if (!string.IsNullOrEmpty(plugin.LoadError))
            {
                await context.ReplyAsync(
                    IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                        $"  Error: {plugin.LoadError}"),
                    cancellationToken);
            }

            return;
        }

        // List all plugins
        var plugins = _pluginManager.Plugins;

        await context.ReplyAsync(
            IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                $"Discovered plugins: {plugins.Count}"),
            cancellationToken);

        foreach (var plugin in plugins)
        {
            var loadedStatus = plugin.IsLoaded ? "L" : "-";
            var enabledStatus = plugin.IsEnabled ? "E" : "-";
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"  [{loadedStatus}{enabledStatus}] {plugin.Manifest.Id}: {plugin.Manifest.Name} v{plugin.Manifest.Version}"),
                cancellationToken);
        }

        await context.ReplyAsync(
            IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                "End of plugins list (L=Loaded, E=Enabled)"),
            cancellationToken);
    }
}

/// <summary>
/// Handles the LOADPLUGIN command.
/// Loads a plugin by ID.
/// Syntax: LOADPLUGIN &lt;plugin_id&gt;
/// </summary>
public sealed class LoadPluginHandler : CommandHandlerBase
{
    private readonly IPluginManager? _pluginManager;

    /// <summary>
    /// Creates a new LOADPLUGIN handler.
    /// </summary>
    /// <param name="pluginManager">Optional plugin manager.</param>
    public LoadPluginHandler(IPluginManager? pluginManager = null)
    {
        _pluginManager = pluginManager;
    }

    /// <inheritdoc />
    public override string Command => "LOADPLUGIN";

    /// <inheritdoc />
    public override int MinimumParameters => 1;

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

        if (_pluginManager is null)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    "Plugin system is not available"),
                cancellationToken);
            return;
        }

        var pluginId = context.Message.Parameters[0];

        if (await _pluginManager.LoadPluginAsync(pluginId, cancellationToken))
        {
            var plugin = _pluginManager.GetPlugin(pluginId);
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Loaded plugin: {plugin?.Manifest.Name ?? pluginId} v{plugin?.Manifest.Version ?? "?"}"),
                cancellationToken);
        }
        else
        {
            var plugin = _pluginManager.GetPlugin(pluginId);
            var error = plugin?.LoadError ?? "Unknown error";
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Failed to load plugin {pluginId}: {error}"),
                cancellationToken);
        }
    }
}

/// <summary>
/// Handles the UNLOADPLUGIN command.
/// Unloads a plugin by ID.
/// Syntax: UNLOADPLUGIN &lt;plugin_id&gt;
/// </summary>
public sealed class UnloadPluginHandler : CommandHandlerBase
{
    private readonly IPluginManager? _pluginManager;

    /// <summary>
    /// Creates a new UNLOADPLUGIN handler.
    /// </summary>
    /// <param name="pluginManager">Optional plugin manager.</param>
    public UnloadPluginHandler(IPluginManager? pluginManager = null)
    {
        _pluginManager = pluginManager;
    }

    /// <inheritdoc />
    public override string Command => "UNLOADPLUGIN";

    /// <inheritdoc />
    public override int MinimumParameters => 1;

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

        if (_pluginManager is null)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    "Plugin system is not available"),
                cancellationToken);
            return;
        }

        var pluginId = context.Message.Parameters[0];

        if (await _pluginManager.UnloadPluginAsync(pluginId, cancellationToken))
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Unloaded plugin: {pluginId}"),
                cancellationToken);
        }
        else
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Failed to unload plugin: {pluginId}"),
                cancellationToken);
        }
    }
}

/// <summary>
/// Handles the RELOADPLUGIN command.
/// Reloads a plugin by ID.
/// Syntax: RELOADPLUGIN &lt;plugin_id&gt;
/// </summary>
public sealed class ReloadPluginHandler : CommandHandlerBase
{
    private readonly IPluginManager? _pluginManager;

    /// <summary>
    /// Creates a new RELOADPLUGIN handler.
    /// </summary>
    /// <param name="pluginManager">Optional plugin manager.</param>
    public ReloadPluginHandler(IPluginManager? pluginManager = null)
    {
        _pluginManager = pluginManager;
    }

    /// <inheritdoc />
    public override string Command => "RELOADPLUGIN";

    /// <inheritdoc />
    public override int MinimumParameters => 1;

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

        if (_pluginManager is null)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    "Plugin system is not available"),
                cancellationToken);
            return;
        }

        var pluginId = context.Message.Parameters[0];

        if (await _pluginManager.ReloadPluginAsync(pluginId, cancellationToken))
        {
            var plugin = _pluginManager.GetPlugin(pluginId);
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Reloaded plugin: {plugin?.Manifest.Name ?? pluginId} v{plugin?.Manifest.Version ?? "?"}"),
                cancellationToken);
        }
        else
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $"Failed to reload plugin: {pluginId}"),
                cancellationToken);
        }
    }
}
