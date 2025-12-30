using System.Text.RegularExpressions;
using Hugin.Core.Entities;
using Hugin.Core.Interfaces;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the KLINE command for local server bans.
/// Usage: KLINE [duration] user@host :reason
/// </summary>
public sealed class KlineHandler : CommandHandlerBase
{
    private readonly IServerBanRepository _banRepository;
    private readonly IUserRepository _userRepository;
    private readonly IConnectionManager _connectionManager;

    public override string Command => "KLINE";
    public override int MinimumParameters => 2;
    public override bool RequiresOperator => true;

    public KlineHandler(
        IServerBanRepository banRepository, 
        IUserRepository userRepository,
        IConnectionManager connectionManager)
    {
        _banRepository = banRepository;
        _userRepository = userRepository;
        _connectionManager = connectionManager;
    }

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var args = context.Message.Parameters;
        var nick = context.User.Nickname.Value;
        var server = context.ServerName;

        // Parse optional duration
        TimeSpan? duration = null;
        var argIndex = 0;

        if (TryParseDuration(args[0], out var parsedDuration))
        {
            duration = parsedDuration;
            argIndex++;
        }

        if (args.Count <= argIndex)
        {
            await SendNeedMoreParamsAsync(context, cancellationToken);
            return;
        }

        var pattern = args[argIndex++];
        
        // Validate pattern contains @
        if (!pattern.Contains('@'))
        {
            pattern = "*@" + pattern; // Convert host-only to *@host
        }

        var reason = args.Count > argIndex ? args[argIndex] : "No reason given";

        // Create and add the ban
        var ban = new ServerBan(
            BanType.KLine,
            pattern,
            reason,
            context.User.Hostmask.ToString(),
            duration);

        _banRepository.Add(ban);

        // Notify operators
        var durationStr = duration.HasValue ? FormatDuration(duration.Value) : "permanently";
        var notice = $"*** {nick} added K-Line for {pattern} ({durationStr}): {reason}";
        
        await context.Broker.SendToOperatorsAsync(
            IrcMessage.CreateWithSource(server, "NOTICE", "*", notice).ToString(),
            cancellationToken);

        // Disconnect affected users
        await DisconnectMatchingUsersAsync(pattern, reason, server, context.Broker, cancellationToken);
    }

    /// <summary>
    /// Disconnects all users matching the given ban pattern.
    /// </summary>
    private async ValueTask DisconnectMatchingUsersAsync(
        string pattern, 
        string reason, 
        string serverName,
        IMessageBroker broker,
        CancellationToken cancellationToken)
    {
        var regex = CreateBanRegex(pattern);
        var matchedUsers = _userRepository.GetAll()
            .Where(u => u.IsRegistered && regex.IsMatch(u.Hostmask.ToString()))
            .ToList();

        foreach (var user in matchedUsers)
        {
            // Send error message before closing
            var killMessage = IrcMessage.CreateWithSource(
                serverName, 
                "ERROR", 
                $"Closing Link: K-Lined: {reason}");
            
            await broker.SendToConnectionAsync(
                user.ConnectionId, 
                killMessage.ToString(), 
                cancellationToken);

            // Close the connection
            await _connectionManager.CloseConnectionAsync(
                user.ConnectionId, 
                $"K-Lined: {reason}", 
                cancellationToken);
        }
    }

    /// <summary>
    /// Creates a regex from a ban pattern (supports * and ? wildcards).
    /// </summary>
    private static Regex CreateBanRegex(string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private static bool TryParseDuration(string input, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;

        if (string.IsNullOrEmpty(input))
            return false;

        // Try parsing as minutes (bare number)
        if (int.TryParse(input, out var minutes))
        {
            duration = TimeSpan.FromMinutes(minutes);
            return true;
        }

        // Try parsing with suffix: 1h, 1d, 1w, etc.
        var lastChar = char.ToLowerInvariant(input[^1]);
        if (int.TryParse(input[..^1], out var value))
        {
            duration = lastChar switch
            {
                'm' => TimeSpan.FromMinutes(value),
                'h' => TimeSpan.FromHours(value),
                'd' => TimeSpan.FromDays(value),
                'w' => TimeSpan.FromDays(value * 7),
                _ => TimeSpan.Zero
            };
            return duration != TimeSpan.Zero;
        }

        return false;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 7)
            return $"{(int)(duration.TotalDays / 7)} week(s)";
        if (duration.TotalDays >= 1)
            return $"{(int)duration.TotalDays} day(s)";
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours} hour(s)";
        return $"{(int)duration.TotalMinutes} minute(s)";
    }
}

/// <summary>
/// Handles the UNKLINE command to remove K-Lines.
/// Usage: UNKLINE user@host
/// </summary>
public sealed class UnklineHandler : CommandHandlerBase
{
    private readonly IServerBanRepository _banRepository;

    public override string Command => "UNKLINE";
    public override int MinimumParameters => 1;
    public override bool RequiresOperator => true;

    public UnklineHandler(IServerBanRepository banRepository)
    {
        _banRepository = banRepository;
    }

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var pattern = context.Message.Parameters[0];
        var nick = context.User.Nickname.Value;
        var server = context.ServerName;

        if (!pattern.Contains('@'))
        {
            pattern = "*@" + pattern;
        }

        if (_banRepository.Remove(BanType.KLine, pattern))
        {
            var notice = $"*** {nick} removed K-Line for {pattern}";
            await context.Broker.SendToOperatorsAsync(
                IrcMessage.CreateWithSource(server, "NOTICE", "*", notice).ToString(),
                cancellationToken);
        }
        else
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(server, "NOTICE", nick, $"No K-Line found for {pattern}"),
                cancellationToken);
        }
    }
}

/// <summary>
/// Handles the GLINE command for network-wide bans.
/// Usage: GLINE [duration] user@host :reason
/// </summary>
public sealed class GlineHandler : CommandHandlerBase
{
    private readonly IServerBanRepository _banRepository;

    public override string Command => "GLINE";
    public override int MinimumParameters => 2;
    public override bool RequiresOperator => true;

    public GlineHandler(IServerBanRepository banRepository)
    {
        _banRepository = banRepository;
    }

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var args = context.Message.Parameters;
        var nick = context.User.Nickname.Value;
        var server = context.ServerName;

        // G-Lines are network-wide, so we just check for operator status
        // In a full implementation, you might have different oper levels

        TimeSpan? duration = null;
        var argIndex = 0;

        if (TryParseDuration(args[0], out var parsedDuration))
        {
            duration = parsedDuration;
            argIndex++;
        }

        if (args.Count <= argIndex)
        {
            await SendNeedMoreParamsAsync(context, cancellationToken);
            return;
        }

        var pattern = args[argIndex++];
        if (!pattern.Contains('@'))
        {
            pattern = "*@" + pattern;
        }

        var reason = args.Count > argIndex ? args[argIndex] : "No reason given";

        var ban = new ServerBan(
            BanType.GLine,
            pattern,
            reason,
            context.User.Hostmask.ToString(),
            duration);

        _banRepository.Add(ban);

        var durationStr = duration.HasValue ? FormatDuration(duration.Value) : "permanently";
        var notice = $"*** {nick} added G-Line for {pattern} ({durationStr}): {reason}";
        
        await context.Broker.SendToOperatorsAsync(
            IrcMessage.CreateWithSource(server, "NOTICE", "*", notice).ToString(),
            cancellationToken);
    }

    private static bool TryParseDuration(string input, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrEmpty(input)) return false;

        if (int.TryParse(input, out var minutes))
        {
            duration = TimeSpan.FromMinutes(minutes);
            return true;
        }

        var lastChar = char.ToLowerInvariant(input[^1]);
        if (int.TryParse(input[..^1], out var value))
        {
            duration = lastChar switch
            {
                'm' => TimeSpan.FromMinutes(value),
                'h' => TimeSpan.FromHours(value),
                'd' => TimeSpan.FromDays(value),
                'w' => TimeSpan.FromDays(value * 7),
                _ => TimeSpan.Zero
            };
            return duration != TimeSpan.Zero;
        }
        return false;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 7) return $"{(int)(duration.TotalDays / 7)} week(s)";
        if (duration.TotalDays >= 1) return $"{(int)duration.TotalDays} day(s)";
        if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours} hour(s)";
        return $"{(int)duration.TotalMinutes} minute(s)";
    }
}

/// <summary>
/// Handles the ZLINE command for IP-based bans.
/// Usage: ZLINE [duration] ip :reason
/// </summary>
public sealed class ZlineHandler : CommandHandlerBase
{
    private readonly IServerBanRepository _banRepository;

    public override string Command => "ZLINE";
    public override int MinimumParameters => 2;
    public override bool RequiresOperator => true;

    public ZlineHandler(IServerBanRepository banRepository)
    {
        _banRepository = banRepository;
    }

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var args = context.Message.Parameters;
        var nick = context.User.Nickname.Value;
        var server = context.ServerName;

        TimeSpan? duration = null;
        var argIndex = 0;

        if (TryParseDuration(args[0], out var parsedDuration))
        {
            duration = parsedDuration;
            argIndex++;
        }

        if (args.Count <= argIndex)
        {
            await SendNeedMoreParamsAsync(context, cancellationToken);
            return;
        }

        var pattern = args[argIndex++];
        var reason = args.Count > argIndex ? args[argIndex] : "No reason given";

        var ban = new ServerBan(
            BanType.ZLine,
            pattern,
            reason,
            context.User.Hostmask.ToString(),
            duration);

        _banRepository.Add(ban);

        var durationStr = duration.HasValue ? FormatDuration(duration.Value) : "permanently";
        var notice = $"*** {nick} added Z-Line for {pattern} ({durationStr}): {reason}";
        
        await context.Broker.SendToOperatorsAsync(
            IrcMessage.CreateWithSource(server, "NOTICE", "*", notice).ToString(),
            cancellationToken);
    }

    private static bool TryParseDuration(string input, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrEmpty(input)) return false;

        if (int.TryParse(input, out var minutes))
        {
            duration = TimeSpan.FromMinutes(minutes);
            return true;
        }

        var lastChar = char.ToLowerInvariant(input[^1]);
        if (int.TryParse(input[..^1], out var value))
        {
            duration = lastChar switch
            {
                'm' => TimeSpan.FromMinutes(value),
                'h' => TimeSpan.FromHours(value),
                'd' => TimeSpan.FromDays(value),
                'w' => TimeSpan.FromDays(value * 7),
                _ => TimeSpan.Zero
            };
            return duration != TimeSpan.Zero;
        }
        return false;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 7) return $"{(int)(duration.TotalDays / 7)} week(s)";
        if (duration.TotalDays >= 1) return $"{(int)duration.TotalDays} day(s)";
        if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours} hour(s)";
        return $"{(int)duration.TotalMinutes} minute(s)";
    }
}
