using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using System.Collections.Concurrent;

namespace Hugin.Persistence.Repositories;

/// <summary>
/// In-memory implementation of user repository for connected users.
/// Users are transient - they exist only while connected.
/// </summary>
public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<Guid, User> _byConnectionId = new();
    private readonly ConcurrentDictionary<string, Guid> _byNickname = new(StringComparer.OrdinalIgnoreCase);
    private int _maxUserCount;

    /// <inheritdoc />
    public User? GetByConnectionId(Guid connectionId)
    {
        return _byConnectionId.GetValueOrDefault(connectionId);
    }

    /// <inheritdoc />
    public User? GetByNickname(Nickname nickname)
    {
        if (_byNickname.TryGetValue(nickname.Value, out var connectionId))
        {
            return GetByConnectionId(connectionId);
        }
        return null;
    }

    /// <inheritdoc />
    public IEnumerable<User> GetAll()
    {
        return _byConnectionId.Values;
    }

    /// <inheritdoc />
    public IEnumerable<User> FindByHostmask(Hostmask pattern)
    {
        return _byConnectionId.Values.Where(u => u.Hostmask.Matches(pattern.ToString()));
    }

    public IEnumerable<User> GetUsersInChannel(ChannelName channelName)
    {
        return _byConnectionId.Values.Where(u => u.Channels.ContainsKey(channelName));
    }

    public IEnumerable<User> GetByAccount(string accountName)
    {
        return _byConnectionId.Values.Where(u =>
            u.Account is not null &&
            u.Account.Equals(accountName, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsNicknameInUse(Nickname nickname)
    {
        return _byNickname.ContainsKey(nickname.Value);
    }

    public void Add(User user)
    {
        if (!_byConnectionId.TryAdd(user.ConnectionId, user))
        {
            throw new InvalidOperationException($"User with connection ID {user.ConnectionId} already exists");
        }

        // Update max count
        var count = _byConnectionId.Count;
        if (count > _maxUserCount)
        {
            Interlocked.Exchange(ref _maxUserCount, count);
        }
    }

    public void Remove(Guid connectionId)
    {
        if (_byConnectionId.TryRemove(connectionId, out var user))
        {
            if (user.Nickname is not null)
            {
                _byNickname.TryRemove(user.Nickname.Value, out _);
            }
        }
    }

    /// <summary>
    /// Registers a nickname for a user. Call this when nickname changes.
    /// </summary>
    public void RegisterNickname(Guid connectionId, Nickname oldNick, Nickname newNick)
    {
        if (oldNick is not null)
        {
            _byNickname.TryRemove(oldNick.Value, out _);
        }
        _byNickname[newNick.Value] = connectionId;
    }

    public int GetCount()
    {
        return _byConnectionId.Count;
    }

    public int GetInvisibleCount()
    {
        return _byConnectionId.Values.Count(u => u.Modes.HasFlag(Core.Enums.UserMode.Invisible));
    }

    public int GetOperatorCount()
    {
        return _byConnectionId.Values.Count(u => u.IsOperator);
    }

    public IEnumerable<User> GetByServer(ServerId serverId)
    {
        return _byConnectionId.Values.Where(u => u.Server.Equals(serverId));
    }

    public int GetMaxUserCount()
    {
        return _maxUserCount;
    }
}
