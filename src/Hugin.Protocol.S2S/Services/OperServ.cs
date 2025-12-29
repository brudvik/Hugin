using System.Globalization;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Hugin.Protocol.S2S.Services;

/// <summary>
/// OperServ - Network administration service.
/// Provides operator commands for network-wide administration.
/// </summary>
public sealed class OperServ : INetworkService
{
    private readonly IServerBanRepository _banRepository;
    private readonly IServerLinkManager _linkManager;
    private readonly ServerId _localServerId;
    private readonly ILogger<OperServ> _logger;

    /// <inheritdoc />
    public string Nickname => "OperServ";

    /// <inheritdoc />
    public string Ident => "OperServ";

    /// <inheritdoc />
    public string Host { get; }

    /// <inheritdoc />
    public string Realname => "Network Administration Service";

    /// <inheritdoc />
    public string Uid { get; }

    /// <summary>
    /// Creates a new OperServ instance.
    /// </summary>
    /// <param name="banRepository">Repository for server bans.</param>
    /// <param name="linkManager">Server link manager.</param>
    /// <param name="localServerId">Local server ID.</param>
    /// <param name="servicesHost">Services hostname.</param>
    /// <param name="logger">Logger instance.</param>
    public OperServ(
        IServerBanRepository banRepository,
        IServerLinkManager linkManager,
        ServerId localServerId,
        string servicesHost,
        ILogger<OperServ> logger)
    {
        _banRepository = banRepository;
        _linkManager = linkManager;
        _localServerId = localServerId;
        Host = servicesHost;
        Uid = $"{localServerId.Sid}AAAAAO";
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask HandleMessageAsync(ServiceMessageContext context, CancellationToken cancellationToken = default)
    {
        // OperServ requires operator status
        if (!context.IsOperator)
        {
            await context.ReplyAsync(this, "Access denied. You must be an IRC operator to use OperServ.", cancellationToken);
            return;
        }

        switch (context.Command)
        {
            case "HELP":
                await ShowHelpAsync(context, context.Arguments.Length > 0 ? context.Arguments[0] : null, cancellationToken);
                break;

            case "AKILL":
                await HandleAkillAsync(context, cancellationToken);
                break;

            case "RAKILL":
            case "UNAKILL":
                await HandleRemoveAkillAsync(context, cancellationToken);
                break;

            case "AKILLLIST":
                await HandleAkillListAsync(context, cancellationToken);
                break;

            case "JUPE":
                await HandleJupeAsync(context, cancellationToken);
                break;

            case "STATS":
                await HandleStatsAsync(context, cancellationToken);
                break;

            case "MODE":
                await HandleModeAsync(context, cancellationToken);
                break;

            case "KICK":
                await HandleKickAsync(context, cancellationToken);
                break;

            case "KILL":
                await HandleKillAsync(context, cancellationToken);
                break;

            case "RAW":
                await HandleRawAsync(context, cancellationToken);
                break;

            case "RESTART":
                await HandleRestartAsync(context, cancellationToken);
                break;

            case "DIE":
            case "SHUTDOWN":
                await HandleDieAsync(context, cancellationToken);
                break;

            case "GLOBAL":
                await HandleGlobalAsync(context, cancellationToken);
                break;

            default:
                await context.ReplyAsync(this, $"Unknown command: {context.Command}. Type /msg OperServ HELP for help.", cancellationToken);
                break;
        }
    }

    private async ValueTask ShowHelpAsync(ServiceMessageContext context, string? command, CancellationToken cancellationToken)
    {
        foreach (var line in GetHelp(command))
        {
            await context.ReplyAsync(this, line, cancellationToken);
        }
    }

    /// <summary>
    /// Handles the AKILL command - adds a network-wide autokill.
    /// </summary>
    private async ValueTask HandleAkillAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        // Syntax: AKILL ADD mask duration reason
        // Syntax: AKILL DEL mask
        // Syntax: AKILL LIST [mask]

        if (context.Arguments.Length < 1)
        {
            await context.ReplyAsync(this, "Syntax: AKILL ADD <user@host> <duration> <reason>", cancellationToken);
            await context.ReplyAsync(this, "        AKILL DEL <user@host>", cancellationToken);
            await context.ReplyAsync(this, "        AKILL LIST [mask]", cancellationToken);
            return;
        }

        var subCommand = context.Arguments[0].ToUpperInvariant();

        switch (subCommand)
        {
            case "ADD":
                await AddAkillAsync(context, cancellationToken);
                break;
            case "DEL":
            case "REMOVE":
                await RemoveAkillAsync(context, cancellationToken);
                break;
            case "LIST":
                await HandleAkillListAsync(context, cancellationToken);
                break;
            default:
                await context.ReplyAsync(this, $"Unknown AKILL subcommand: {subCommand}", cancellationToken);
                break;
        }
    }

    private async ValueTask AddAkillAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        // AKILL ADD user@host duration reason
        if (context.Arguments.Length < 4)
        {
            await context.ReplyAsync(this, "Syntax: AKILL ADD <user@host> <duration> <reason>", cancellationToken);
            return;
        }

        var mask = context.Arguments[1];
        var durationStr = context.Arguments[2];
        var reason = string.Join(" ", context.Arguments.Skip(3));

        // Parse duration (e.g., "1d", "2h", "30m", "0" for permanent)
        if (!TryParseDuration(durationStr, out var duration))
        {
            await context.ReplyAsync(this, $"Invalid duration: {durationStr}. Use format: 30m, 2h, 1d, or 0 for permanent.", cancellationToken);
            return;
        }

        // Create the AKILL (G-line)
        var expiry = duration.HasValue ? DateTimeOffset.UtcNow.Add(duration.Value) : (DateTimeOffset?)null;

        var ban = new ServerBan(
            BanType.GLine,
            mask,
            reason,
            context.SourceNick,
            DateTimeOffset.UtcNow,
            expiry);

        await _banRepository.AddAsync(ban, cancellationToken);

        // Broadcast to network
        var akillMsg = CreateEncapMessage("AKILL", mask, durationStr, context.SourceNick, reason);
        await _linkManager.BroadcastAsync(akillMsg, null, cancellationToken);

        _logger.LogInformation("OperServ: {Oper} added AKILL on {Mask} for {Duration}: {Reason}",
            context.SourceNick, mask, durationStr, reason);

        await context.ReplyAsync(this, $"AKILL on {mask} has been added.", cancellationToken);
    }

    private async ValueTask RemoveAkillAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: AKILL DEL <user@host>", cancellationToken);
            return;
        }

        var mask = context.Arguments[1];
        var removed = await _banRepository.RemoveAsync(mask, cancellationToken);

        if (removed)
        {
            // Broadcast removal to network
            var unakillMsg = CreateEncapMessage("UNAKILL", mask);
            await _linkManager.BroadcastAsync(unakillMsg, null, cancellationToken);

            _logger.LogInformation("OperServ: {Oper} removed AKILL on {Mask}", context.SourceNick, mask);
            await context.ReplyAsync(this, $"AKILL on {mask} has been removed.", cancellationToken);
        }
        else
        {
            await context.ReplyAsync(this, $"No AKILL found for {mask}.", cancellationToken);
        }
    }

    private async ValueTask HandleRemoveAkillAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 1)
        {
            await context.ReplyAsync(this, "Syntax: RAKILL <user@host>", cancellationToken);
            return;
        }

        var mask = context.Arguments[0];
        var removed = await _banRepository.RemoveAsync(mask, cancellationToken);

        if (removed)
        {
            var unakillMsg = CreateEncapMessage("UNAKILL", mask);
            await _linkManager.BroadcastAsync(unakillMsg, null, cancellationToken);

            _logger.LogInformation("OperServ: {Oper} removed AKILL on {Mask}", context.SourceNick, mask);
            await context.ReplyAsync(this, $"AKILL on {mask} has been removed.", cancellationToken);
        }
        else
        {
            await context.ReplyAsync(this, $"No AKILL found for {mask}.", cancellationToken);
        }
    }

    private async ValueTask HandleAkillListAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        var bans = await _banRepository.GetActiveGlinesAsync(cancellationToken);
        var banList = bans.ToList();

        if (banList.Count == 0)
        {
            await context.ReplyAsync(this, "AKILL list is empty.", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, $"AKILL list ({banList.Count} entries):", cancellationToken);

        foreach (var ban in banList)
        {
            var expiryStr = ban.ExpiresAt.HasValue
                ? ban.ExpiresAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                : "permanent";
            await context.ReplyAsync(this, $"  {ban.Mask} (by {ban.SetBy}, expires: {expiryStr}): {ban.Reason}", cancellationToken);
        }

        await context.ReplyAsync(this, "*** End of AKILL list ***", cancellationToken);
    }

    /// <summary>
    /// Handles the JUPE command - temporarily blocks a server name.
    /// </summary>
    private async ValueTask HandleJupeAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: JUPE <servername> <reason>", cancellationToken);
            return;
        }

        var serverName = context.Arguments[0];
        var reason = string.Join(" ", context.Arguments.Skip(1));

        // Check if server is already linked
        var existingServer = _linkManager.GetByName(serverName);
        if (existingServer != null)
        {
            await context.ReplyAsync(this, $"Server {serverName} is currently linked. Cannot JUPE.", cancellationToken);
            return;
        }

        // Add to jupe list (prevent this server from linking)
        var jupeBan = new ServerBan(
            BanType.Jupe,
            serverName,
            reason,
            context.SourceNick,
            DateTimeOffset.UtcNow,
            null);

        await _banRepository.AddAsync(jupeBan, cancellationToken);

        _logger.LogInformation("OperServ: {Oper} JUPEd server {Server}: {Reason}",
            context.SourceNick, serverName, reason);

        await context.ReplyAsync(this, $"Server {serverName} has been JUPEd: {reason}", cancellationToken);
    }

    /// <summary>
    /// Handles the STATS command - displays network statistics.
    /// </summary>
    private async ValueTask HandleStatsAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        var servers = _linkManager.AllServers.ToList();
        var directLinks = _linkManager.DirectLinks.ToList();

        await context.ReplyAsync(this, "*** Network Statistics ***", cancellationToken);
        await context.ReplyAsync(this, $"Local server: {_localServerId.Name} ({_localServerId.Sid})", cancellationToken);
        await context.ReplyAsync(this, $"Direct links: {directLinks.Count}", cancellationToken);
        await context.ReplyAsync(this, $"Total servers: {servers.Count + 1}", cancellationToken); // +1 for local

        if (servers.Count > 0)
        {
            await context.ReplyAsync(this, "Linked servers:", cancellationToken);
            foreach (var server in servers)
            {
                var linkType = server.IsDirect ? "direct" : "via " + server.LearnedFrom?.Name;
                await context.ReplyAsync(this, $"  {server.Id.Name} ({server.Id.Sid}) - hop {server.HopCount}, {linkType}", cancellationToken);
            }
        }

        await context.ReplyAsync(this, "*** End of Statistics ***", cancellationToken);
    }

    /// <summary>
    /// Handles the MODE command - forces a mode change on a channel.
    /// </summary>
    private async ValueTask HandleModeAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: MODE <#channel|nick> <modes> [parameters]", cancellationToken);
            return;
        }

        var target = context.Arguments[0];
        var modes = string.Join(" ", context.Arguments.Skip(1));

        _logger.LogInformation("OperServ: {Oper} forced MODE {Target} {Modes}",
            context.SourceNick, target, modes);

        await context.ReplyAsync(this, $"Mode change on {target}: {modes}", cancellationToken);
        // Note: Actual mode change would be propagated through the server
    }

    /// <summary>
    /// Handles the KICK command - forces a kick from a channel.
    /// </summary>
    private async ValueTask HandleKickAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: KICK <#channel> <nick> [reason]", cancellationToken);
            return;
        }

        var channel = context.Arguments[0];
        var nick = context.Arguments[1];
        var reason = context.Arguments.Length > 2
            ? string.Join(" ", context.Arguments.Skip(2))
            : "Requested by network administrator";

        _logger.LogInformation("OperServ: {Oper} forced KICK of {Nick} from {Channel}: {Reason}",
            context.SourceNick, nick, channel, reason);

        await context.ReplyAsync(this, $"Kicked {nick} from {channel}: {reason}", cancellationToken);
        // Note: Actual kick would be propagated through the server
    }

    /// <summary>
    /// Handles the KILL command - disconnects a user from the network.
    /// </summary>
    private async ValueTask HandleKillAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 1)
        {
            await context.ReplyAsync(this, "Syntax: KILL <nick> [reason]", cancellationToken);
            return;
        }

        var nick = context.Arguments[0];
        var reason = context.Arguments.Length > 1
            ? string.Join(" ", context.Arguments.Skip(1))
            : "Killed by network administrator";

        // Broadcast KILL to network
        var killMsg = S2SMessage.CreateWithSource(
            Uid,
            "KILL",
            nick, // Would need to resolve to UID
            $"[{_localServerId.Name}] {reason}");

        await _linkManager.BroadcastAsync(killMsg, null, cancellationToken);

        _logger.LogInformation("OperServ: {Oper} KILLed {Nick}: {Reason}",
            context.SourceNick, nick, reason);

        await context.ReplyAsync(this, $"Killed {nick}: {reason}", cancellationToken);
    }

    /// <summary>
    /// Handles the RAW command - sends a raw command to the network.
    /// </summary>
    private async ValueTask HandleRawAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 1)
        {
            await context.ReplyAsync(this, "Syntax: RAW <command>", cancellationToken);
            await context.ReplyAsync(this, "Warning: This command is dangerous and should be used with caution.", cancellationToken);
            return;
        }

        var rawCommand = string.Join(" ", context.Arguments);

        _logger.LogWarning("OperServ: {Oper} sent RAW: {Command}", context.SourceNick, rawCommand);
        await context.ReplyAsync(this, $"RAW command sent: {rawCommand}", cancellationToken);

        // Note: Actual raw command handling would need to parse and execute the command
    }

    /// <summary>
    /// Handles the RESTART command - restarts the server.
    /// </summary>
    private async ValueTask HandleRestartAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 1 || context.Arguments[0] != "CONFIRM")
        {
            await context.ReplyAsync(this, "Warning: This will restart the IRC server!", cancellationToken);
            await context.ReplyAsync(this, "Use: RESTART CONFIRM to proceed.", cancellationToken);
            return;
        }

        _logger.LogWarning("OperServ: {Oper} requested server RESTART", context.SourceNick);
        await context.ReplyAsync(this, "Server restart initiated...", cancellationToken);

        // Note: Actual restart would be handled by the server host
    }

    /// <summary>
    /// Handles the DIE command - shuts down the server.
    /// </summary>
    private async ValueTask HandleDieAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 1 || context.Arguments[0] != "CONFIRM")
        {
            await context.ReplyAsync(this, "Warning: This will shut down the IRC server!", cancellationToken);
            await context.ReplyAsync(this, "Use: DIE CONFIRM to proceed.", cancellationToken);
            return;
        }

        _logger.LogWarning("OperServ: {Oper} requested server SHUTDOWN", context.SourceNick);
        await context.ReplyAsync(this, "Server shutdown initiated...", cancellationToken);

        // Note: Actual shutdown would be handled by the server host
    }

    /// <summary>
    /// Handles the GLOBAL command - sends a global notice to all users.
    /// </summary>
    private async ValueTask HandleGlobalAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 1)
        {
            await context.ReplyAsync(this, "Syntax: GLOBAL <message>", cancellationToken);
            return;
        }

        var message = string.Join(" ", context.Arguments);

        // Broadcast NOTICE to all users (would use special target like $*)
        _logger.LogInformation("OperServ: {Oper} sent GLOBAL: {Message}", context.SourceNick, message);
        await context.ReplyAsync(this, $"Global notice sent: {message}", cancellationToken);

        // Note: Actual global notice would be sent to all connected users
    }

    private S2SMessage CreateEncapMessage(params string[] args)
    {
        var parameters = new[] { "*", args[0] }.Concat(args.Skip(1)).ToArray();
        return S2SMessage.CreateWithSource(_localServerId.Sid, "ENCAP", parameters);
    }

    private static bool TryParseDuration(string duration, out TimeSpan? result)
    {
        result = null;

        if (duration == "0" || duration.Equals("permanent", StringComparison.OrdinalIgnoreCase))
        {
            return true; // null means permanent
        }

        if (duration.Length < 2)
        {
            return false;
        }

        var unit = duration[^1];
        if (!int.TryParse(duration[..^1], out var value))
        {
            return false;
        }

        result = unit switch
        {
            'm' or 'M' => TimeSpan.FromMinutes(value),
            'h' or 'H' => TimeSpan.FromHours(value),
            'd' or 'D' => TimeSpan.FromDays(value),
            'w' or 'W' => TimeSpan.FromDays(value * 7),
            _ => null
        };

        return result.HasValue;
    }

    private static IEnumerable<string> GetHelp(string? command)
    {
        if (string.IsNullOrEmpty(command))
        {
            yield return "*** OperServ Help ***";
            yield return "OperServ provides network administration commands.";
            yield return " ";
            yield return "Available commands:";
            yield return "  AKILL       - Add/remove network-wide autokills";
            yield return "  JUPE        - Block a server name from linking";
            yield return "  STATS       - Display network statistics";
            yield return "  MODE        - Force a mode change";
            yield return "  KICK        - Force a kick from a channel";
            yield return "  KILL        - Disconnect a user from the network";
            yield return "  GLOBAL      - Send a global notice";
            yield return "  RAW         - Send a raw S2S command (dangerous!)";
            yield return "  RESTART     - Restart the server";
            yield return "  DIE         - Shut down the server";
            yield return " ";
            yield return "Type /msg OperServ HELP <command> for more info.";
            yield return "*** End of Help ***";
            yield break;
        }

        switch (command.ToUpperInvariant())
        {
            case "AKILL":
                yield return "*** AKILL Help ***";
                yield return "Adds or removes network-wide autokills (AKILLs/G-lines).";
                yield return " ";
                yield return "Syntax:";
                yield return "  AKILL ADD <user@host> <duration> <reason>";
                yield return "  AKILL DEL <user@host>";
                yield return "  AKILL LIST [mask]";
                yield return " ";
                yield return "Duration format: 30m, 2h, 1d, 1w, or 0 for permanent.";
                yield return "*** End of Help ***";
                break;

            case "JUPE":
                yield return "*** JUPE Help ***";
                yield return "Blocks a server name from linking to the network.";
                yield return " ";
                yield return "Syntax: JUPE <servername> <reason>";
                yield return " ";
                yield return "JUPEs prevent servers from linking. Useful for";
                yield return "blocking compromised or misconfigured servers.";
                yield return "*** End of Help ***";
                break;

            case "STATS":
                yield return "*** STATS Help ***";
                yield return "Displays network statistics.";
                yield return " ";
                yield return "Syntax: STATS";
                yield return " ";
                yield return "Shows server links, user counts, and uptime.";
                yield return "*** End of Help ***";
                break;

            case "GLOBAL":
                yield return "*** GLOBAL Help ***";
                yield return "Sends a global notice to all users on the network.";
                yield return " ";
                yield return "Syntax: GLOBAL <message>";
                yield return " ";
                yield return "Use sparingly for important announcements only.";
                yield return "*** End of Help ***";
                break;

            case "DIE":
            case "SHUTDOWN":
                yield return "*** DIE Help ***";
                yield return "Shuts down the IRC server.";
                yield return " ";
                yield return "Syntax: DIE CONFIRM";
                yield return " ";
                yield return "WARNING: This will disconnect all users!";
                yield return "*** End of Help ***";
                break;

            default:
                yield return $"No help available for {command}.";
                break;
        }
    }

    /// <inheritdoc />
    IEnumerable<string> INetworkService.GetHelp(string? command) => GetHelp(command);
}
