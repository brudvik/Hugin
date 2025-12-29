using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Hugin.Server.Services;

/// <summary>
/// Manages channel join throttling (+j mode).
/// Limits the number of joins to a channel within a time window.
/// </summary>
public sealed class ChannelJoinThrottle
{
    private readonly ILogger<ChannelJoinThrottle> _logger;
    private readonly ConcurrentDictionary<string, JoinThrottleSettings> _channelSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, JoinTracker> _channelTrackers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new channel join throttle instance.
    /// </summary>
    /// <param name="logger">Logger for throttle events.</param>
    public ChannelJoinThrottle(ILogger<ChannelJoinThrottle> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sets join throttle settings for a channel.
    /// Format: joins:seconds (e.g., "5:10" means 5 joins per 10 seconds)
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <param name="settingsString">The settings string.</param>
    /// <returns>True if settings were parsed and applied.</returns>
    public bool SetChannelSettings(string channelName, string settingsString)
    {
        if (!TryParseSettings(settingsString, out var settings))
        {
            return false;
        }

        _channelSettings[channelName] = settings;
        _channelTrackers[channelName] = new JoinTracker(settings.JoinLimit, settings.WindowSeconds);

        _logger.LogInformation("Set join throttle for {Channel}: {Joins} per {Seconds}s",
            channelName, settings.JoinLimit, settings.WindowSeconds);

        return true;
    }

    /// <summary>
    /// Removes join throttle settings for a channel.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    public void RemoveChannelSettings(string channelName)
    {
        _channelSettings.TryRemove(channelName, out _);
        _channelTrackers.TryRemove(channelName, out _);
    }

    /// <summary>
    /// Gets the join throttle settings for a channel.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <returns>The settings, or null if not set.</returns>
    public JoinThrottleSettings? GetChannelSettings(string channelName)
    {
        return _channelSettings.TryGetValue(channelName, out var settings) ? settings : null;
    }

    /// <summary>
    /// Checks if a join should be allowed based on throttle limits.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <param name="userNickname">The nickname of the joining user (for logging).</param>
    /// <returns>True if the join is allowed; false if throttled.</returns>
    public bool CheckJoin(string channelName, string userNickname)
    {
        if (!_channelTrackers.TryGetValue(channelName, out var tracker))
        {
            return true; // No throttle configured
        }

        var now = DateTimeOffset.UtcNow;
        var allowed = tracker.TryJoin(now);

        if (!allowed)
        {
            var settings = _channelSettings[channelName];
            _logger.LogWarning("Join throttled for {Nick} in {Channel} ({Limit} per {Window}s)",
                userNickname, channelName, settings.JoinLimit, settings.WindowSeconds);
        }

        return allowed;
    }

    /// <summary>
    /// Records a successful join for throttle tracking.
    /// Called after the join is actually completed.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    public void RecordJoin(string channelName)
    {
        if (_channelTrackers.TryGetValue(channelName, out var tracker))
        {
            tracker.RecordJoin(DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Gets the time until the next join is allowed.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <returns>TimeSpan until allowed, or TimeSpan.Zero if allowed now.</returns>
    public TimeSpan GetTimeUntilAllowed(string channelName)
    {
        if (!_channelTrackers.TryGetValue(channelName, out var tracker))
        {
            return TimeSpan.Zero;
        }

        return tracker.GetTimeUntilAllowed(DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Parses join throttle settings string.
    /// </summary>
    private static bool TryParseSettings(string settingsString, out JoinThrottleSettings settings)
    {
        settings = new JoinThrottleSettings();

        if (string.IsNullOrWhiteSpace(settingsString))
        {
            return false;
        }

        var parts = settingsString.Split(':');
        if (parts.Length != 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var joins) || !int.TryParse(parts[1], out var seconds))
        {
            return false;
        }

        if (joins <= 0 || seconds <= 0)
        {
            return false;
        }

        settings.JoinLimit = joins;
        settings.WindowSeconds = seconds;
        return true;
    }
}

/// <summary>
/// Join throttle settings for a channel.
/// </summary>
public sealed class JoinThrottleSettings
{
    /// <summary>Maximum number of joins allowed in the window.</summary>
    public int JoinLimit { get; set; }

    /// <summary>Window size in seconds.</summary>
    public int WindowSeconds { get; set; }

    /// <summary>
    /// Formats settings as a mode parameter string.
    /// </summary>
    public override string ToString() => $"{JoinLimit}:{WindowSeconds}";
}

/// <summary>
/// Tracks joins for throttling.
/// Uses a sliding window approach.
/// </summary>
internal sealed class JoinTracker
{
    private readonly object _lock = new();
    private readonly int _limit;
    private readonly int _windowSeconds;
    private readonly Queue<DateTimeOffset> _joins = new();

    /// <summary>
    /// Creates a new join tracker.
    /// </summary>
    /// <param name="limit">Maximum joins per window.</param>
    /// <param name="windowSeconds">Window size in seconds.</param>
    public JoinTracker(int limit, int windowSeconds)
    {
        _limit = limit;
        _windowSeconds = windowSeconds;
    }

    /// <summary>
    /// Checks if a join would be allowed.
    /// </summary>
    /// <param name="now">Current time.</param>
    /// <returns>True if allowed.</returns>
    public bool TryJoin(DateTimeOffset now)
    {
        lock (_lock)
        {
            CleanOldEntries(now);
            return _joins.Count < _limit;
        }
    }

    /// <summary>
    /// Records a join.
    /// </summary>
    /// <param name="now">Current time.</param>
    public void RecordJoin(DateTimeOffset now)
    {
        lock (_lock)
        {
            CleanOldEntries(now);
            _joins.Enqueue(now);
        }
    }

    /// <summary>
    /// Gets time until next join is allowed.
    /// </summary>
    /// <param name="now">Current time.</param>
    /// <returns>Time until allowed.</returns>
    public TimeSpan GetTimeUntilAllowed(DateTimeOffset now)
    {
        lock (_lock)
        {
            CleanOldEntries(now);

            if (_joins.Count < _limit)
            {
                return TimeSpan.Zero;
            }

            if (_joins.TryPeek(out var oldest))
            {
                var windowEnd = oldest.AddSeconds(_windowSeconds);
                return windowEnd > now ? windowEnd - now : TimeSpan.Zero;
            }

            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Removes entries outside the window.
    /// </summary>
    private void CleanOldEntries(DateTimeOffset now)
    {
        var threshold = now.AddSeconds(-_windowSeconds);

        while (_joins.TryPeek(out var oldest) && oldest < threshold)
        {
            _joins.Dequeue();
        }
    }
}
