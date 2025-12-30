using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hugin.Persistence.Repositories;

/// <summary>
/// Entity Framework Core implementation of the virtual host repository.
/// </summary>
public sealed class VirtualHostRepository : IVirtualHostRepository
{
    private readonly HuginDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualHostRepository"/> class.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    public VirtualHostRepository(HuginDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<VirtualHost?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.VirtualHosts
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        return entity?.ToDomain();
    }

    /// <inheritdoc />
    public async Task<VirtualHost?> GetActiveByAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.VirtualHosts
            .FirstOrDefaultAsync(v => v.AccountId == accountId && v.IsActive, cancellationToken);

        return entity?.ToDomain();
    }

    /// <inheritdoc />
    public async Task<IEnumerable<VirtualHost>> GetByAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.VirtualHosts
            .Where(v => v.AccountId == accountId)
            .OrderByDescending(v => v.RequestedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToDomain());
    }

    /// <inheritdoc />
    public async Task<IEnumerable<VirtualHost>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.VirtualHosts
            .Where(v => v.ApprovedAt == null)
            .OrderBy(v => v.RequestedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToDomain());
    }

    /// <inheritdoc />
    public async Task<bool> IsHostnameInUseAsync(string hostname, CancellationToken cancellationToken = default)
    {
        return await _dbContext.VirtualHosts
            .AnyAsync(v => v.Hostname == hostname && (v.IsActive || v.ApprovedAt != null), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<VirtualHost> CreateAsync(Guid accountId, string hostname, CancellationToken cancellationToken = default)
    {
        var entity = new VirtualHostEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Hostname = hostname,
            RequestedAt = DateTimeOffset.UtcNow,
            IsActive = false
        };

        _dbContext.VirtualHosts.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity.ToDomain();
    }

    /// <inheritdoc />
    public async Task UpdateAsync(VirtualHost vhost, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.VirtualHosts
            .FirstOrDefaultAsync(v => v.Id == vhost.Id, cancellationToken);

        if (entity is null)
        {
            throw new InvalidOperationException($"VirtualHost {vhost.Id} not found");
        }

        entity.Hostname = vhost.Hostname;
        entity.ApprovedAt = vhost.ApprovedAt;
        entity.ApprovedBy = vhost.ApprovedBy;
        entity.IsActive = vhost.IsActive;
        entity.Notes = vhost.Notes;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.VirtualHosts.FindAsync(new object[] { id }, cancellationToken);
        if (entity is not null)
        {
            _dbContext.VirtualHosts.Remove(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<int> DeactivateAllForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.VirtualHosts
            .Where(v => v.AccountId == accountId && v.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var entity in entities)
        {
            entity.IsActive = false;
        }

        if (entities.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return entities.Count;
    }
}
