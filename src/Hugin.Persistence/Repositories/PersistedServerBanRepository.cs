using System.Collections.Concurrent;
using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hugin.Persistence.Repositories;

/// <summary>
/// Repository for server bans that persists to database while maintaining an in-memory cache for fast lookups.
/// Bans are loaded from the database on initialization and all changes are persisted immediately.
/// </summary>
public sealed class PersistedServerBanRepository : IServerBanRepository
{
    private readonly ConcurrentDictionary<Guid, ServerBan> _bans = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PersistedServerBanRepository> _logger;
    private bool _initialized;
    private readonly object _initLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PersistedServerBanRepository"/> class.
    /// </summary>
    /// <param name="scopeFactory">Service scope factory for creating database contexts.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public PersistedServerBanRepository(
        IServiceScopeFactory scopeFactory,
        ILogger<PersistedServerBanRepository> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Ensures bans are loaded from the database. Called lazily on first access.
    /// </summary>
    private void EnsureInitialized()
    {
        if (_initialized) return;

        lock (_initLock)
        {
            if (_initialized) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<HuginDbContext>();

                var entities = context.ServerBans.ToList();
                foreach (var entity in entities)
                {
                    // Skip expired bans
                    if (entity.ExpiresAt.HasValue && entity.ExpiresAt.Value <= DateTimeOffset.UtcNow)
                    {
                        continue;
                    }

                    var ban = entity.ToDomain();
                    _bans[ban.Id] = ban;
                }

                _logger.LogInformation("Loaded {Count} active server bans from database", _bans.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load server bans from database");
            }

            _initialized = true;
        }
    }

    /// <inheritdoc />
    public void Add(ServerBan ban)
    {
        EnsureInitialized();
        _bans[ban.Id] = ban;

        // Persist to database
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<HuginDbContext>();

            var entity = new ServerBanEntity
            {
                Id = ban.Id,
                BanType = (int)ban.Type,
                Pattern = ban.Pattern,
                Reason = ban.Reason,
                CreatedAt = ban.CreatedAt,
                ExpiresAt = ban.ExpiresAt,
                SetBy = ban.SetBy
            };

            context.ServerBans.Add(entity);
            context.SaveChanges();

            _logger.LogDebug("Persisted {BanType} for {Pattern} to database", ban.Type, ban.Pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist ban to database: {Pattern}", ban.Pattern);
        }
    }

    /// <inheritdoc />
    public async ValueTask AddAsync(ServerBan ban, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        _bans[ban.Id] = ban;

        // Persist to database
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<HuginDbContext>();

            var entity = new ServerBanEntity
            {
                Id = ban.Id,
                BanType = (int)ban.Type,
                Pattern = ban.Pattern,
                Reason = ban.Reason,
                CreatedAt = ban.CreatedAt,
                ExpiresAt = ban.ExpiresAt,
                SetBy = ban.SetBy
            };

            context.ServerBans.Add(entity);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogDebug("Persisted {BanType} for {Pattern} to database", ban.Type, ban.Pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist ban to database: {Pattern}", ban.Pattern);
        }
    }

    /// <inheritdoc />
    public bool Remove(BanType type, string pattern)
    {
        EnsureInitialized();
        var toRemove = _bans.Values
            .FirstOrDefault(b => b.Type == type &&
                b.Pattern.Equals(pattern, StringComparison.OrdinalIgnoreCase));

        if (toRemove is null)
        {
            return false;
        }

        if (!_bans.TryRemove(toRemove.Id, out _))
        {
            return false;
        }

        // Remove from database
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<HuginDbContext>();

            var entity = context.ServerBans.Find(toRemove.Id);
            if (entity is not null)
            {
                context.ServerBans.Remove(entity);
                context.SaveChanges();
            }

            _logger.LogDebug("Removed {BanType} for {Pattern} from database", type, pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove ban from database: {Pattern}", pattern);
        }

        return true;
    }

    /// <inheritdoc />
    public async ValueTask<bool> RemoveAsync(string mask, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        var toRemove = _bans.Values
            .FirstOrDefault(b => b.Pattern.Equals(mask, StringComparison.OrdinalIgnoreCase) ||
                                 b.Mask.Equals(mask, StringComparison.OrdinalIgnoreCase));

        if (toRemove is null)
        {
            return false;
        }

        if (!_bans.TryRemove(toRemove.Id, out _))
        {
            return false;
        }

        // Remove from database
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<HuginDbContext>();

            var entity = await context.ServerBans.FindAsync(new object[] { toRemove.Id }, cancellationToken);
            if (entity is not null)
            {
                context.ServerBans.Remove(entity);
                await context.SaveChangesAsync(cancellationToken);
            }

            _logger.LogDebug("Removed ban for {Mask} from database", mask);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove ban from database: {Mask}", mask);
        }

        return true;
    }

    /// <inheritdoc />
    public bool Remove(Guid id)
    {
        EnsureInitialized();
        if (!_bans.TryRemove(id, out var removed))
        {
            return false;
        }

        // Remove from database
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<HuginDbContext>();

            var entity = context.ServerBans.Find(id);
            if (entity is not null)
            {
                context.ServerBans.Remove(entity);
                context.SaveChanges();
            }

            _logger.LogDebug("Removed ban {Id} from database", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove ban from database: {Id}", id);
        }

        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<ServerBan> GetByType(BanType type)
    {
        EnsureInitialized();
        return _bans.Values
            .Where(b => b.Type == type && !b.IsExpired)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<ServerBan> GetAllActive()
    {
        EnsureInitialized();
        return _bans.Values
            .Where(b => !b.IsExpired)
            .ToList();
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ServerBan>> GetActiveGlinesAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        IReadOnlyList<ServerBan> result = _bans.Values
            .Where(b => b.Type == BanType.GLine && !b.IsExpired)
            .ToList();
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public ServerBan? FindMatchingBan(string userHost)
    {
        EnsureInitialized();
        return _bans.Values
            .Where(b => (b.Type == BanType.KLine || b.Type == BanType.GLine) && !b.IsExpired)
            .FirstOrDefault(b => b.Matches(userHost));
    }

    /// <inheritdoc />
    public ServerBan? FindMatchingZLine(string ipAddress)
    {
        EnsureInitialized();
        return _bans.Values
            .Where(b => b.Type == BanType.ZLine && !b.IsExpired)
            .FirstOrDefault(b => b.MatchesIp(ipAddress));
    }

    /// <inheritdoc />
    public ServerBan? FindMatchingJupe(string serverName)
    {
        EnsureInitialized();
        return _bans.Values
            .Where(b => b.Type == BanType.Jupe && !b.IsExpired)
            .FirstOrDefault(b => b.Pattern.Equals(serverName, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public int PurgeExpired()
    {
        EnsureInitialized();
        var expired = _bans.Values.Where(b => b.IsExpired).ToList();
        var count = 0;

        foreach (var ban in expired)
        {
            if (_bans.TryRemove(ban.Id, out _))
            {
                count++;

                // Remove from database
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<HuginDbContext>();

                    var entity = context.ServerBans.Find(ban.Id);
                    if (entity is not null)
                    {
                        context.ServerBans.Remove(entity);
                        context.SaveChanges();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to remove expired ban from database: {Id}", ban.Id);
                }
            }
        }

        if (count > 0)
        {
            _logger.LogInformation("Purged {Count} expired bans", count);
        }

        return count;
    }
}
