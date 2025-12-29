using System.Collections.Concurrent;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Microsoft.Extensions.Logging;

namespace Hugin.Server.Services;

/// <summary>
/// Manages channel-level flood protection (+f mode).
/// </summary>
public sealed class ChannelFloodProtection
{
    private readonly ILogger<ChannelFloodProtection> _logger;
    private readonly ConcurrentDictionary<string, ChannelFloodSettings> _channelSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<(string Channel, Guid UserId), UserFloodTracker> _userTrackers = new();

    /// <summary>
    /// Creates a new channel flood protection instance.
    /// </summary>
    /// <param name="logger">Logger for flood protection events.</param>
    public ChannelFloodProtection(ILogger<ChannelFloodProtection> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sets flood protection settings for a channel.
    /// Format: [*][c#:t#][,j#:t#][,k#:t#][,m#:t#][,n#:t#][,t#:t#]
    /// c = CTCPs, j = joins, k = kicks, m = messages, n = nick changes, t = text (lines)
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <param name="settingsString">The settings string (e.g., "5:2" for 5 messages per 2 seconds).</param>
    /// <returns>True if settings were parsed and applied.</returns>
    public bool SetChannelSettings(string channelName, string settingsString)
    {
        if (!TryParseSettings(settingsString, out var settings))
        {
            return false;
        }

        _channelSettings[channelName] = settings;
        _logger.LogInformation("Set flood protection for {Channel}: {Settings}", channelName, settingsString);
        return true;
    }

    /// <summary>
    /// Removes flood protection settings for a channel.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    public void RemoveChannelSettings(string channelName)
    {
        _channelSettings.TryRemove(channelName, out _);

        // Remove all user trackers for this channel
        var keysToRemove = _userTrackers.Keys.Where(k => k.Channel.Equals(channelName, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var key in keysToRemove)
        {
            _userTrackers.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Gets the flood protection settings for a channel.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <returns>The settings, or null if not set.</returns>
    public ChannelFloodSettings? GetChannelSettings(string channelName)
    {
        return _channelSettings.TryGetValue(channelName, out var settings) ? settings : null;
    }

    /// <summary>
    /// Checks if an action should be allowed based on flood limits.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <param name="userId">The user's connection ID.</param>
    /// <param name="actionType">The type of action.</param>
    /// <returns>The flood check result.</returns>
    public FloodCheckResult CheckFlood(string channelName, Guid userId, FloodActionType actionType)
    {
        if (!_channelSettings.TryGetValue(channelName, out var settings))
        {
            return FloodCheckResult.Allowed;
        }

        var tracker = _userTrackers.GetOrAdd((channelName, userId), _ => new UserFloodTracker());
        var now = DateTimeOffset.UtcNow;

        // Get the limit for this action type
        var (limit, windowSeconds) = GetLimitForAction(settings, actionType);
        if (limit == 0)
        {
            return FloodCheckResult.Allowed;
        }

        // Clean old entries and check
        tracker.CleanOldEntries(actionType, now, TimeSpan.FromSeconds(windowSeconds));
        var count = tracker.GetCount(actionType);

        if (count >= limit)
        {
            _logger.LogWarning("Flood limit exceeded: {Channel} user {UserId} {Action} ({Count}/{Limit} in {Window}s)",
                channelName, userId, actionType, count, limit, windowSeconds);

            return new FloodCheckResult
            {
                IsAllowed = false,
                ActionType = actionType,
                Count = count,
                Limit = limit,
                WindowSeconds = windowSeconds,
                Action = settings.BanOnFlood ? FloodAction.Ban : FloodAction.Kick
            };
        }

        // Record this action
        tracker.RecordAction(actionType, now);
        return FloodCheckResult.Allowed;
    }

    /// <summary>
    /// Parses flood protection settings string.
    /// </summary>
    private static bool TryParseSettings(string settingsString, out ChannelFloodSettings settings)
    {
        settings = new ChannelFloodSettings();

        if (string.IsNullOrWhiteSpace(settingsString))
        {
            return false;
        }

        var input = settingsString.AsSpan();

        // Check for ban mode prefix (*)
        if (input.Length > 0 && input[0] == '*')
        {
            settings.BanOnFlood = true;
            input = input[1..];
        }

        // Parse type:count:time format
        // Simple format: messages:seconds (e.g., "5:2" means 5 messages per 2 seconds)
        // Complex format: type:count:seconds[,type:count:seconds]...
        var parts = input.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var segments = part.Split(':', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 2)
            {
                // Simple format: count:seconds (applies to messages)
                if (int.TryParse(segments[0], out var count) && int.TryParse(segments[1], out var seconds))
                {
                    settings.MessageLimit = count;
                    settings.MessageWindowSeconds = seconds;
                }
            }
            else if (segments.Length == 3)
            {
                // Complex format: type:count:seconds
                if (!int.TryParse(segments[1], out var count) || !int.TryParse(segments[2], out var seconds))
                {
                    continue;
                }

                switch (segments[0].ToLowerInvariant())
                {
                    case "c": // CTCP
                        settings.CtcpLimit = count;
                        settings.CtcpWindowSeconds = seconds;
                        break;
                    case "j": // Join
                        settings.JoinLimit = count;
                        settings.JoinWindowSeconds = seconds;
                        break;
                    case "k": // Kick
                        settings.KickLimit = count;
                        settings.KickWindowSeconds = seconds;
                        break;
                    case "m": // Messages
                        settings.MessageLimit = count;
                        settings.MessageWindowSeconds = seconds;
                        break;
                    case "n": // Nick changes
                        settings.NickLimit = count;
                        settings.NickWindowSeconds = seconds;
                        break;
                    case "t": // Text (same as messages)
                        settings.MessageLimit = count;
                        settings.MessageWindowSeconds = seconds;
                        break;
                }
            }
        }

        return settings.MessageLimit > 0 || settings.JoinLimit > 0 ||
               settings.CtcpLimit > 0 || settings.KickLimit > 0 ||
               settings.NickLimit > 0;
    }

    private static (int Limit, int WindowSeconds) GetLimitForAction(ChannelFloodSettings settings, FloodActionType action)
    {
        return action switch
        {
            FloodActionType.Message => (settings.MessageLimit, settings.MessageWindowSeconds),
            FloodActionType.Join => (settings.JoinLimit, settings.JoinWindowSeconds),
            FloodActionType.Ctcp => (settings.CtcpLimit, settings.CtcpWindowSeconds),
            FloodActionType.Kick => (settings.KickLimit, settings.KickWindowSeconds),
            FloodActionType.NickChange => (settings.NickLimit, settings.NickWindowSeconds),
            _ => (0, 0)
        };
    }
}

/// <summary>
/// Flood protection settings for a channel.
/// </summary>
public sealed class ChannelFloodSettings
{
    /// <summary>Ban instead of kick on flood.</summary>
    public bool BanOnFlood { get; set; }

    /// <summary>Message limit per window.</summary>
    public int MessageLimit { get; set; }

    /// <summary>Message window in seconds.</summary>
    public int MessageWindowSeconds { get; set; } = 10;

    /// <summary>Join limit per window.</summary>
    public int JoinLimit { get; set; }

    /// <summary>Join window in seconds.</summary>
    public int JoinWindowSeconds { get; set; } = 60;

    /// <summary>CTCP limit per window.</summary>
    public int CtcpLimit { get; set; }

    /// <summary>CTCP window in seconds.</summary>
    public int CtcpWindowSeconds { get; set; } = 60;

    /// <summary>Kick limit per window.</summary>
    public int KickLimit { get; set; }

    /// <summary>Kick window in seconds.</summary>
    public int KickWindowSeconds { get; set; } = 60;

    /// <summary>Nick change limit per window.</summary>
    public int NickLimit { get; set; }

    /// <summary>Nick change window in seconds.</summary>
    public int NickWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Formats settings as a mode string.
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>();

        if (BanOnFlood)
        {
            parts.Add("*");
        }

        if (MessageLimit > 0)
        {
            parts.Add($"m:{MessageLimit}:{MessageWindowSeconds}");
        }

        if (JoinLimit > 0)
        {
            parts.Add($"j:{JoinLimit}:{JoinWindowSeconds}");
        }

        if (CtcpLimit > 0)
        {
            parts.Add($"c:{CtcpLimit}:{CtcpWindowSeconds}");
        }

        if (KickLimit > 0)
        {
            parts.Add($"k:{KickLimit}:{KickWindowSeconds}");
        }

        if (NickLimit > 0)
        {
            parts.Add($"n:{NickLimit}:{NickWindowSeconds}");
        }

        return string.Join(",", parts);
    }
}

/// <summary>
/// Types of actions that can be flood-limited.
/// </summary>
public enum FloodActionType
{
    /// <summary>Channel message.</summary>
    Message,

    /// <summary>Channel join.</summary>
    Join,

    /// <summary>CTCP to channel.</summary>
    Ctcp,

    /// <summary>Kick from channel.</summary>
    Kick,

    /// <summary>Nick change.</summary>
    NickChange
}

/// <summary>
/// Action to take when flood limit is exceeded.
/// </summary>
public enum FloodAction
{
    /// <summary>Kick the user.</summary>
    Kick,

    /// <summary>Ban the user.</summary>
    Ban,

    /// <summary>Set the channel to moderated.</summary>
    SetModerated
}

/// <summary>
/// Result of a flood check.
/// </summary>
public readonly struct FloodCheckResult
{
    /// <summary>Allowed result (no flood detected).</summary>
    public static readonly FloodCheckResult Allowed = new() { IsAllowed = true };

    /// <summary>Whether the action is allowed.</summary>
    public bool IsAllowed { get; init; }

    /// <summary>The action type that was checked.</summary>
    public FloodActionType ActionType { get; init; }

    /// <summary>Current count of actions.</summary>
    public int Count { get; init; }

    /// <summary>The limit.</summary>
    public int Limit { get; init; }

    /// <summary>Window size in seconds.</summary>
    public int WindowSeconds { get; init; }

    /// <summary>Action to take.</summary>
    public FloodAction Action { get; init; }
}

/// <summary>
/// Tracks flood actions for a user in a channel.
/// </summary>
internal sealed class UserFloodTracker
{
    private readonly object _lock = new();
    private readonly Dictionary<FloodActionType, List<DateTimeOffset>> _actions = new();

    /// <summary>
    /// Records an action.
    /// </summary>
    public void RecordAction(FloodActionType type, DateTimeOffset timestamp)
    {
        lock (_lock)
        {
            if (!_actions.TryGetValue(type, out var list))
            {
                list = new List<DateTimeOffset>();
                _actions[type] = list;
            }

            list.Add(timestamp);
        }
    }

    /// <summary>
    /// Gets the count of actions in the current window.
    /// </summary>
    public int GetCount(FloodActionType type)
    {
        lock (_lock)
        {
            return _actions.TryGetValue(type, out var list) ? list.Count : 0;
        }
    }

    /// <summary>
    /// Cleans entries older than the window.
    /// </summary>
    public void CleanOldEntries(FloodActionType type, DateTimeOffset now, TimeSpan window)
    {
        lock (_lock)
        {
            if (_actions.TryGetValue(type, out var list))
            {
                var threshold = now - window;
                list.RemoveAll(t => t < threshold);
            }
        }
    }
}
