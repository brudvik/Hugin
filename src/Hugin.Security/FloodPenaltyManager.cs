using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Hugin.Security;

/// <summary>
/// Manages anti-flood penalties for users.
/// Commands and messages add penalties, which must be "paid off" before more can be sent.
/// This prevents rapid command flooding while allowing normal usage.
/// </summary>
public sealed class FloodPenaltyManager : IFloodPenaltyManager
{
    private readonly ILogger<FloodPenaltyManager> _logger;
    private readonly ConcurrentDictionary<Guid, UserPenaltyState> _userPenalties = new();
    private readonly FloodPenaltySettings _settings;

    /// <summary>
    /// Creates a new flood penalty manager.
    /// </summary>
    /// <param name="logger">Logger for penalty operations.</param>
    /// <param name="settings">Optional penalty settings.</param>
    public FloodPenaltyManager(ILogger<FloodPenaltyManager> logger, FloodPenaltySettings? settings = null)
    {
        _logger = logger;
        _settings = settings ?? new FloodPenaltySettings();
    }

    /// <inheritdoc />
    public bool CheckAndApplyPenalty(Guid userId, string command, out TimeSpan waitTime)
    {
        var state = _userPenalties.GetOrAdd(userId, _ => new UserPenaltyState());
        var now = DateTimeOffset.UtcNow;

        // Get penalty for this command
        var penalty = GetPenaltyForCommand(command);

        lock (state)
        {
            // First, drain penalty based on elapsed time
            if (state.LastCheck.HasValue)
            {
                var elapsed = now - state.LastCheck.Value;
                var drained = elapsed.TotalMilliseconds * _settings.DrainRatePerSecond / 1000.0;
                state.CurrentPenalty = Math.Max(0, state.CurrentPenalty - drained);
            }
            state.LastCheck = now;

            // Check if we're over the threshold
            if (state.CurrentPenalty >= _settings.MaxPenalty)
            {
                // Calculate wait time until penalty drains to acceptable level
                var excessPenalty = state.CurrentPenalty - _settings.MaxPenalty + penalty;
                waitTime = TimeSpan.FromSeconds(excessPenalty / _settings.DrainRatePerSecond);

                if (!state.HasBeenWarned)
                {
                    state.HasBeenWarned = true;
                    state.ExcessiveCount++;
                    _logger.LogWarning("User {UserId} exceeded flood limit, must wait {Wait}ms",
                        userId, waitTime.TotalMilliseconds);
                }

                return false;
            }

            // Apply penalty
            state.CurrentPenalty += penalty;
            state.HasBeenWarned = false;
            waitTime = TimeSpan.Zero;
            return true;
        }
    }

    /// <inheritdoc />
    public void ResetPenalty(Guid userId)
    {
        _userPenalties.TryRemove(userId, out _);
    }

    /// <inheritdoc />
    public double GetCurrentPenalty(Guid userId)
    {
        if (_userPenalties.TryGetValue(userId, out var state))
        {
            lock (state)
            {
                return state.CurrentPenalty;
            }
        }
        return 0;
    }

    /// <inheritdoc />
    public void AddBonusPenalty(Guid userId, double amount, string reason)
    {
        var state = _userPenalties.GetOrAdd(userId, _ => new UserPenaltyState());
        lock (state)
        {
            state.CurrentPenalty += amount;
            _logger.LogDebug("Added bonus penalty {Amount} to user {UserId}: {Reason}",
                amount, userId, reason);
        }
    }

    /// <summary>
    /// Gets the penalty for a specific command.
    /// </summary>
    private double GetPenaltyForCommand(string command)
    {
        // Check for specific command penalties
        if (_settings.CommandPenalties.TryGetValue(command.ToUpperInvariant(), out var penalty))
        {
            return penalty;
        }

        // Use default based on command type
        return command.ToUpperInvariant() switch
        {
            // Low penalty for simple queries
            "PING" or "PONG" => _settings.MinPenalty,

            // Medium penalty for normal messages
            "PRIVMSG" or "NOTICE" => _settings.DefaultPenalty,

            // Higher penalty for channel operations
            "JOIN" or "PART" or "KICK" => _settings.DefaultPenalty * 1.5,

            // Higher penalty for mode changes
            "MODE" => _settings.DefaultPenalty * 2,

            // High penalty for expensive operations
            "WHO" or "WHOIS" or "WHOWAS" or "LIST" => _settings.DefaultPenalty * 3,

            // Default for unknown commands
            _ => _settings.DefaultPenalty
        };
    }

    /// <summary>
    /// Removes inactive user states to prevent memory leaks.
    /// </summary>
    public void CleanupInactive(TimeSpan inactiveThreshold)
    {
        var now = DateTimeOffset.UtcNow;
        var toRemove = _userPenalties
            .Where(kv =>
            {
                lock (kv.Value)
                {
                    return !kv.Value.LastCheck.HasValue ||
                           now - kv.Value.LastCheck.Value > inactiveThreshold;
                }
            })
            .Select(kv => kv.Key)
            .ToList();

        foreach (var userId in toRemove)
        {
            _userPenalties.TryRemove(userId, out _);
        }

        if (toRemove.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} inactive penalty states", toRemove.Count);
        }
    }

    /// <summary>
    /// Gets statistics about the penalty system.
    /// </summary>
    public FloodPenaltyStats GetStats()
    {
        var states = _userPenalties.Values.ToList();
        var activeCount = 0;
        var overLimitCount = 0;
        var totalPenalty = 0.0;

        foreach (var state in states)
        {
            lock (state)
            {
                if (state.CurrentPenalty > 0)
                {
                    activeCount++;
                    totalPenalty += state.CurrentPenalty;
                }
                if (state.CurrentPenalty >= _settings.MaxPenalty)
                {
                    overLimitCount++;
                }
            }
        }

        return new FloodPenaltyStats
        {
            TrackedUsers = states.Count,
            ActivePenalties = activeCount,
            UsersOverLimit = overLimitCount,
            TotalPenaltyValue = totalPenalty,
            AveragePenalty = activeCount > 0 ? totalPenalty / activeCount : 0
        };
    }
}

/// <summary>
/// Interface for flood penalty management.
/// </summary>
public interface IFloodPenaltyManager
{
    /// <summary>
    /// Checks if a command is allowed and applies penalty if so.
    /// </summary>
    /// <param name="userId">The user's connection ID.</param>
    /// <param name="command">The command being executed.</param>
    /// <param name="waitTime">If denied, how long the user must wait.</param>
    /// <returns>True if allowed, false if user must wait.</returns>
    bool CheckAndApplyPenalty(Guid userId, string command, out TimeSpan waitTime);

    /// <summary>
    /// Resets all penalties for a user.
    /// </summary>
    /// <param name="userId">The user's connection ID.</param>
    void ResetPenalty(Guid userId);

    /// <summary>
    /// Gets the current penalty for a user.
    /// </summary>
    /// <param name="userId">The user's connection ID.</param>
    /// <returns>The current penalty value.</returns>
    double GetCurrentPenalty(Guid userId);

    /// <summary>
    /// Adds extra penalty to a user (e.g., for failed auth attempts).
    /// </summary>
    /// <param name="userId">The user's connection ID.</param>
    /// <param name="amount">Amount of penalty to add.</param>
    /// <param name="reason">Reason for the penalty.</param>
    void AddBonusPenalty(Guid userId, double amount, string reason);
}

/// <summary>
/// Settings for the flood penalty system.
/// </summary>
public sealed class FloodPenaltySettings
{
    /// <summary>
    /// Maximum penalty before throttling.
    /// Default: 10000 (10 seconds worth of commands).
    /// </summary>
    public double MaxPenalty { get; init; } = 10000;

    /// <summary>
    /// Default penalty per command in milliseconds.
    /// Default: 1000 (1 second).
    /// </summary>
    public double DefaultPenalty { get; init; } = 1000;

    /// <summary>
    /// Minimum penalty for lightweight commands.
    /// Default: 100 (0.1 seconds).
    /// </summary>
    public double MinPenalty { get; init; } = 100;

    /// <summary>
    /// How fast penalty drains per second of real time.
    /// Default: 1000 (1 second of penalty per second of real time).
    /// </summary>
    public double DrainRatePerSecond { get; init; } = 1000;

    /// <summary>
    /// Custom penalties for specific commands.
    /// </summary>
    public Dictionary<string, double> CommandPenalties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Per-user penalty state.
/// </summary>
internal sealed class UserPenaltyState
{
    /// <summary>Current penalty amount.</summary>
    public double CurrentPenalty { get; set; }

    /// <summary>Last time penalty was checked/drained.</summary>
    public DateTimeOffset? LastCheck { get; set; }

    /// <summary>Whether user has been warned about flooding.</summary>
    public bool HasBeenWarned { get; set; }

    /// <summary>Number of times user has exceeded limit.</summary>
    public int ExcessiveCount { get; set; }
}

/// <summary>
/// Statistics about the penalty system.
/// </summary>
public sealed class FloodPenaltyStats
{
    /// <summary>Total users being tracked.</summary>
    public int TrackedUsers { get; init; }

    /// <summary>Users with active penalties.</summary>
    public int ActivePenalties { get; init; }

    /// <summary>Users currently over the limit.</summary>
    public int UsersOverLimit { get; init; }

    /// <summary>Total penalty value across all users.</summary>
    public double TotalPenaltyValue { get; init; }

    /// <summary>Average penalty per active user.</summary>
    public double AveragePenalty { get; init; }
}
