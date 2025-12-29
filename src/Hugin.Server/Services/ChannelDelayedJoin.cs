using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Hugin.Server.Services;

/// <summary>
/// Manages delayed join functionality (+D mode, also known as auditorium mode).
/// Users who join are hidden until they speak, change mode, or are opped.
/// </summary>
public sealed class ChannelDelayedJoin
{
    private readonly ILogger<ChannelDelayedJoin> _logger;
    private readonly ConcurrentDictionary<string, DelayedJoinChannel> _channels = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new delayed join manager.
    /// </summary>
    /// <param name="logger">Logger for delayed join events.</param>
    public ChannelDelayedJoin(ILogger<ChannelDelayedJoin> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Enables delayed join mode for a channel.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    public void EnableDelayedJoin(string channelName)
    {
        _channels.GetOrAdd(channelName, _ => new DelayedJoinChannel());
        _logger.LogInformation("Enabled delayed join for {Channel}", channelName);
    }

    /// <summary>
    /// Disables delayed join mode for a channel.
    /// All hidden users become visible.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <returns>List of user IDs that were hidden and should now be shown.</returns>
    public IReadOnlyList<Guid> DisableDelayedJoin(string channelName)
    {
        if (_channels.TryRemove(channelName, out var channel))
        {
            var hiddenUsers = channel.GetHiddenUsers();
            _logger.LogInformation("Disabled delayed join for {Channel}, revealing {Count} users",
                channelName, hiddenUsers.Count);
            return hiddenUsers;
        }

        return Array.Empty<Guid>();
    }

    /// <summary>
    /// Checks if delayed join is enabled for a channel.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <returns>True if delayed join is enabled.</returns>
    public bool IsEnabled(string channelName)
    {
        return _channels.ContainsKey(channelName);
    }

    /// <summary>
    /// Records a user joining a delayed-join channel.
    /// The user starts as hidden.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <param name="userId">The user's connection ID.</param>
    /// <param name="nickname">The user's nickname (for logging).</param>
    public void RecordJoin(string channelName, Guid userId, string nickname)
    {
        if (_channels.TryGetValue(channelName, out var channel))
        {
            channel.AddHiddenUser(userId, nickname);
            _logger.LogDebug("User {Nick} joined {Channel} in delayed mode (hidden)",
                nickname, channelName);
        }
    }

    /// <summary>
    /// Records a user leaving a delayed-join channel.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <param name="userId">The user's connection ID.</param>
    public void RecordPart(string channelName, Guid userId)
    {
        if (_channels.TryGetValue(channelName, out var channel))
        {
            channel.RemoveUser(userId);
        }
    }

    /// <summary>
    /// Checks if a user is currently hidden (delayed join not yet revealed).
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <param name="userId">The user's connection ID.</param>
    /// <returns>True if the user is hidden.</returns>
    public bool IsUserHidden(string channelName, Guid userId)
    {
        if (_channels.TryGetValue(channelName, out var channel))
        {
            return channel.IsHidden(userId);
        }

        return false;
    }

    /// <summary>
    /// Reveals a user (makes them visible in the channel).
    /// Called when user speaks, changes mode, or is opped.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <param name="userId">The user's connection ID.</param>
    /// <param name="reason">The reason for revealing.</param>
    /// <returns>True if the user was hidden and is now revealed.</returns>
    public bool RevealUser(string channelName, Guid userId, RevealReason reason)
    {
        if (_channels.TryGetValue(channelName, out var channel))
        {
            var revealed = channel.RevealUser(userId, out var nickname);
            if (revealed)
            {
                _logger.LogDebug("User {Nick} revealed in {Channel} (reason: {Reason})",
                    nickname ?? userId.ToString(), channelName, reason);
            }
            return revealed;
        }

        return false;
    }

    /// <summary>
    /// Gets all hidden users in a channel.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <returns>List of hidden user IDs.</returns>
    public IReadOnlyList<Guid> GetHiddenUsers(string channelName)
    {
        if (_channels.TryGetValue(channelName, out var channel))
        {
            return channel.GetHiddenUsers();
        }

        return Array.Empty<Guid>();
    }

    /// <summary>
    /// Gets the count of hidden users in a channel.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <returns>The count of hidden users.</returns>
    public int GetHiddenUserCount(string channelName)
    {
        if (_channels.TryGetValue(channelName, out var channel))
        {
            return channel.HiddenCount;
        }

        return 0;
    }
}

/// <summary>
/// Reason for revealing a hidden user.
/// </summary>
public enum RevealReason
{
    /// <summary>User sent a message.</summary>
    Message,

    /// <summary>User sent a CTCP/action.</summary>
    Ctcp,

    /// <summary>User was given voice or higher.</summary>
    ModeChange,

    /// <summary>User changed their nick.</summary>
    NickChange,

    /// <summary>Channel mode +D was removed.</summary>
    ModeDisabled,

    /// <summary>Operator revealed the user.</summary>
    OperatorAction
}

/// <summary>
/// Tracks hidden users in a delayed-join channel.
/// </summary>
internal sealed class DelayedJoinChannel
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, string> _hiddenUsers = new();

    /// <summary>
    /// Gets the number of hidden users.
    /// </summary>
    public int HiddenCount
    {
        get
        {
            lock (_lock)
            {
                return _hiddenUsers.Count;
            }
        }
    }

    /// <summary>
    /// Adds a user to the hidden list.
    /// </summary>
    public void AddHiddenUser(Guid userId, string nickname)
    {
        lock (_lock)
        {
            _hiddenUsers[userId] = nickname;
        }
    }

    /// <summary>
    /// Removes a user (they left the channel).
    /// </summary>
    public void RemoveUser(Guid userId)
    {
        lock (_lock)
        {
            _hiddenUsers.Remove(userId);
        }
    }

    /// <summary>
    /// Checks if a user is hidden.
    /// </summary>
    public bool IsHidden(Guid userId)
    {
        lock (_lock)
        {
            return _hiddenUsers.ContainsKey(userId);
        }
    }

    /// <summary>
    /// Reveals a user.
    /// </summary>
    public bool RevealUser(Guid userId, out string? nickname)
    {
        lock (_lock)
        {
            if (_hiddenUsers.Remove(userId, out nickname))
            {
                return true;
            }
            nickname = null;
            return false;
        }
    }

    /// <summary>
    /// Gets all hidden user IDs.
    /// </summary>
    public IReadOnlyList<Guid> GetHiddenUsers()
    {
        lock (_lock)
        {
            return _hiddenUsers.Keys.ToList();
        }
    }
}
