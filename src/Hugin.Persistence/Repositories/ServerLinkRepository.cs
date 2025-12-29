using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hugin.Persistence.Repositories;

/// <summary>
/// Repository for server link configurations.
/// </summary>
public sealed class ServerLinkRepository : IServerLinkRepository
{
    private readonly HuginDbContext _context;

    /// <summary>
    /// Creates a new server link repository.
    /// </summary>
    public ServerLinkRepository(HuginDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServerLinkEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ServerLinks
            .AsNoTracking()
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServerLinkEntity>> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ServerLinks
            .AsNoTracking()
            .Where(l => l.IsEnabled)
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServerLinkEntity>> GetAutoConnectAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ServerLinks
            .AsNoTracking()
            .Where(l => l.IsEnabled && l.AutoConnect)
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServerLinkEntity?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.ServerLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServerLinkEntity?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _context.ServerLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Name == name, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServerLinkEntity?> GetBySidAsync(string sid, CancellationToken cancellationToken = default)
    {
        return await _context.ServerLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Sid == sid, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServerLinkEntity> AddAsync(ServerLinkEntity link, CancellationToken cancellationToken = default)
    {
        link.CreatedAt = DateTimeOffset.UtcNow;
        link.UpdatedAt = DateTimeOffset.UtcNow;

        _context.ServerLinks.Add(link);
        await _context.SaveChangesAsync(cancellationToken);

        return link;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(ServerLinkEntity link, CancellationToken cancellationToken = default)
    {
        link.UpdatedAt = DateTimeOffset.UtcNow;

        _context.ServerLinks.Update(link);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var link = await _context.ServerLinks.FindAsync([id], cancellationToken);
        if (link != null)
        {
            _context.ServerLinks.Remove(link);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task UpdateLastConnectedAsync(int id, DateTimeOffset connectedAt, CancellationToken cancellationToken = default)
    {
        await _context.ServerLinks
            .Where(l => l.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.LastConnectedAt, connectedAt)
                .SetProperty(l => l.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ValidatePasswordAsync(string serverName, string password, CancellationToken cancellationToken = default)
    {
        var link = await _context.ServerLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Name == serverName && l.IsEnabled, cancellationToken);

        if (link == null)
        {
            return false;
        }

        // Simple string comparison - passwords should be stored encrypted
        // In a real implementation, you'd want to decrypt and compare
        return link.ReceivePassword == password;
    }
}
