using System.Collections.Concurrent;
using Hugin.Core.Entities;
using Hugin.Core.Interfaces;

namespace Hugin.Persistence.Repositories;

/// <summary>
/// In-memory repository for server bans.
/// </summary>
public sealed class InMemoryServerBanRepository : IServerBanRepository
{
    private readonly ConcurrentDictionary<Guid, ServerBan> _bans = new();

    /// <inheritdoc />
    public void Add(ServerBan ban)
    {
        _bans[ban.Id] = ban;
    }

    /// <inheritdoc />
    public ValueTask AddAsync(ServerBan ban, CancellationToken cancellationToken = default)
    {
        Add(ban);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public bool Remove(BanType type, string pattern)
    {
        var toRemove = _bans.Values
            .FirstOrDefault(b => b.Type == type && 
                b.Pattern.Equals(pattern, StringComparison.OrdinalIgnoreCase));

        if (toRemove is not null)
        {
            return _bans.TryRemove(toRemove.Id, out _);
        }

        return false;
    }

    /// <inheritdoc />
    public ValueTask<bool> RemoveAsync(string mask, CancellationToken cancellationToken = default)
    {
        var toRemove = _bans.Values
            .FirstOrDefault(b => b.Pattern.Equals(mask, StringComparison.OrdinalIgnoreCase) ||
                                 b.Mask.Equals(mask, StringComparison.OrdinalIgnoreCase));

        if (toRemove is not null)
        {
            return ValueTask.FromResult(_bans.TryRemove(toRemove.Id, out _));
        }

        return ValueTask.FromResult(false);
    }

    /// <inheritdoc />
    public bool Remove(Guid id)
    {
        return _bans.TryRemove(id, out _);
    }

    /// <inheritdoc />
    public IReadOnlyList<ServerBan> GetByType(BanType type)
    {
        return _bans.Values
            .Where(b => b.Type == type && !b.IsExpired)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<ServerBan> GetAllActive()
    {
        return _bans.Values
            .Where(b => !b.IsExpired)
            .ToList();
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ServerBan>> GetActiveGlinesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ServerBan> result = _bans.Values
            .Where(b => b.Type == BanType.GLine && !b.IsExpired)
            .ToList();
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public ServerBan? FindMatchingBan(string userHost)
    {
        return _bans.Values
            .Where(b => (b.Type == BanType.KLine || b.Type == BanType.GLine) && !b.IsExpired)
            .FirstOrDefault(b => b.Matches(userHost));
    }

    /// <inheritdoc />
    public ServerBan? FindMatchingZLine(string ipAddress)
    {
        return _bans.Values
            .Where(b => b.Type == BanType.ZLine && !b.IsExpired)
            .FirstOrDefault(b => b.MatchesIp(ipAddress));
    }

    /// <inheritdoc />
    public ServerBan? FindMatchingJupe(string serverName)
    {
        return _bans.Values
            .Where(b => b.Type == BanType.Jupe && !b.IsExpired)
            .FirstOrDefault(b => b.Pattern.Equals(serverName, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public int PurgeExpired()
    {
        var expired = _bans.Values.Where(b => b.IsExpired).ToList();
        var count = 0;

        foreach (var ban in expired)
        {
            if (_bans.TryRemove(ban.Id, out _))
            {
                count++;
            }
        }

        return count;
    }
}
