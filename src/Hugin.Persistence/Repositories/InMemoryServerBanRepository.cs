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
