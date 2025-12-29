using System.Collections.Concurrent;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Microsoft.Extensions.Logging;

namespace Hugin.Server.Services;

/// <summary>
/// Manages the caller-ID (+g mode) accept system.
/// Users with +g only receive private messages from accepted users.
/// </summary>
public sealed class CallerIdManager
{
    private readonly ILogger<CallerIdManager> _logger;
    private readonly ConcurrentDictionary<Guid, CallerIdState> _userStates = new();

    /// <summary>
    /// Creates a new caller-ID manager.
    /// </summary>
    /// <param name="logger">Logger for caller-ID events.</param>
    public CallerIdManager(ILogger<CallerIdManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks if a message from sender should be delivered to target.
    /// </summary>
    /// <param name="target">The target user (with +g mode).</param>
    /// <param name="sender">The sending user.</param>
    /// <returns>The result of the check.</returns>
    public CallerIdCheckResult CheckMessage(User target, User sender)
    {
        // If target doesn't have +g, allow all messages
        if (!target.Modes.HasFlag(UserMode.CallerID))
        {
            return CallerIdCheckResult.Allowed;
        }

        // Operators always get through
        if (sender.IsOperator)
        {
            return CallerIdCheckResult.Allowed;
        }

        // Check if sender is on target's accept list
        if (_userStates.TryGetValue(target.ConnectionId, out var state))
        {
            if (state.IsAccepted(sender.Nickname?.Value ?? string.Empty))
            {
                return CallerIdCheckResult.Allowed;
            }

            // Check if sender shares a channel with target
            // (Common channel exception - users who share channels can message each other)
            // This would need to be checked by the caller with channel information
        }

        // Not accepted - should notify sender
        return new CallerIdCheckResult
        {
            IsAllowed = false,
            TargetNickname = target.Nickname?.Value ?? "*",
            ShouldNotify = !HasRecentlyNotified(target.ConnectionId, sender.ConnectionId)
        };
    }

    /// <summary>
    /// Adds a nickname to a user's accept list.
    /// </summary>
    /// <param name="userId">The user's connection ID.</param>
    /// <param name="nickname">The nickname to accept.</param>
    /// <returns>True if added, false if already on list.</returns>
    public bool AddToAcceptList(Guid userId, string nickname)
    {
        var state = _userStates.GetOrAdd(userId, _ => new CallerIdState());
        var added = state.AddAccepted(nickname);

        if (added)
        {
            _logger.LogDebug("User {UserId} added {Nick} to accept list", userId, nickname);
        }

        return added;
    }

    /// <summary>
    /// Removes a nickname from a user's accept list.
    /// </summary>
    /// <param name="userId">The user's connection ID.</param>
    /// <param name="nickname">The nickname to remove.</param>
    /// <returns>True if removed, false if not on list.</returns>
    public bool RemoveFromAcceptList(Guid userId, string nickname)
    {
        if (_userStates.TryGetValue(userId, out var state))
        {
            var removed = state.RemoveAccepted(nickname);
            if (removed)
            {
                _logger.LogDebug("User {UserId} removed {Nick} from accept list", userId, nickname);
            }
            return removed;
        }

        return false;
    }

    /// <summary>
    /// Gets the accept list for a user.
    /// </summary>
    /// <param name="userId">The user's connection ID.</param>
    /// <returns>List of accepted nicknames.</returns>
    public IReadOnlyList<string> GetAcceptList(Guid userId)
    {
        if (_userStates.TryGetValue(userId, out var state))
        {
            return state.GetAcceptedList();
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Clears the accept list for a user.
    /// </summary>
    /// <param name="userId">The user's connection ID.</param>
    public void ClearAcceptList(Guid userId)
    {
        if (_userStates.TryGetValue(userId, out var state))
        {
            state.ClearAccepted();
            _logger.LogDebug("Cleared accept list for user {UserId}", userId);
        }
    }

    /// <summary>
    /// Removes a user's state when they disconnect.
    /// </summary>
    /// <param name="userId">The user's connection ID.</param>
    public void RemoveUser(Guid userId)
    {
        _userStates.TryRemove(userId, out _);
    }

    /// <summary>
    /// Records that a "messages are being filtered" notification was sent.
    /// Prevents spam of the notification.
    /// </summary>
    /// <param name="targetUserId">The target user's ID.</param>
    /// <param name="senderUserId">The sender user's ID.</param>
    public void RecordNotification(Guid targetUserId, Guid senderUserId)
    {
        if (_userStates.TryGetValue(targetUserId, out var state))
        {
            state.RecordNotification(senderUserId);
        }
    }

    /// <summary>
    /// Checks if a notification was recently sent for this sender.
    /// </summary>
    private bool HasRecentlyNotified(Guid targetUserId, Guid senderUserId)
    {
        if (_userStates.TryGetValue(targetUserId, out var state))
        {
            return state.HasRecentlyNotified(senderUserId);
        }

        return false;
    }
}

/// <summary>
/// Result of a caller-ID check.
/// </summary>
public readonly struct CallerIdCheckResult
{
    /// <summary>
    /// Result for allowed messages.
    /// </summary>
    public static readonly CallerIdCheckResult Allowed = new() { IsAllowed = true };

    /// <summary>
    /// Whether the message is allowed through.
    /// </summary>
    public bool IsAllowed { get; init; }

    /// <summary>
    /// The target's nickname (for error message).
    /// </summary>
    public string? TargetNickname { get; init; }

    /// <summary>
    /// Whether to notify the sender (to avoid spam).
    /// </summary>
    public bool ShouldNotify { get; init; }
}

/// <summary>
/// State for a user's caller-ID settings.
/// </summary>
internal sealed class CallerIdState
{
    private readonly object _lock = new();
    private readonly HashSet<string> _accepted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, DateTimeOffset> _recentNotifications = new();
    private static readonly TimeSpan NotificationCooldown = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Checks if a nickname is on the accept list.
    /// </summary>
    public bool IsAccepted(string nickname)
    {
        lock (_lock)
        {
            return _accepted.Contains(nickname);
        }
    }

    /// <summary>
    /// Adds a nickname to the accept list.
    /// </summary>
    public bool AddAccepted(string nickname)
    {
        lock (_lock)
        {
            return _accepted.Add(nickname);
        }
    }

    /// <summary>
    /// Removes a nickname from the accept list.
    /// </summary>
    public bool RemoveAccepted(string nickname)
    {
        lock (_lock)
        {
            return _accepted.Remove(nickname);
        }
    }

    /// <summary>
    /// Gets the current accept list.
    /// </summary>
    public IReadOnlyList<string> GetAcceptedList()
    {
        lock (_lock)
        {
            return _accepted.ToList();
        }
    }

    /// <summary>
    /// Clears the accept list.
    /// </summary>
    public void ClearAccepted()
    {
        lock (_lock)
        {
            _accepted.Clear();
        }
    }

    /// <summary>
    /// Records a notification sent to a sender.
    /// </summary>
    public void RecordNotification(Guid senderUserId)
    {
        lock (_lock)
        {
            _recentNotifications[senderUserId] = DateTimeOffset.UtcNow;

            // Clean old entries
            var threshold = DateTimeOffset.UtcNow - NotificationCooldown - TimeSpan.FromMinutes(5);
            var toRemove = _recentNotifications.Where(kv => kv.Value < threshold).Select(kv => kv.Key).ToList();
            foreach (var key in toRemove)
            {
                _recentNotifications.Remove(key);
            }
        }
    }

    /// <summary>
    /// Checks if a notification was recently sent to this sender.
    /// </summary>
    public bool HasRecentlyNotified(Guid senderUserId)
    {
        lock (_lock)
        {
            if (_recentNotifications.TryGetValue(senderUserId, out var when))
            {
                return DateTimeOffset.UtcNow - when < NotificationCooldown;
            }
            return false;
        }
    }
}
