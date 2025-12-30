using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hugin.Persistence.Repositories;

/// <summary>
/// Entity Framework Core implementation of the registered channel repository.
/// </summary>
public sealed class RegisteredChannelRepository : IRegisteredChannelRepository
{
    private readonly HuginDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegisteredChannelRepository"/> class.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    public RegisteredChannelRepository(HuginDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<RegisteredChannel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.RegisteredChannels
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        return entity?.ToDomain();
    }

    /// <inheritdoc />
    public async Task<RegisteredChannel?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.RegisteredChannels
            .FirstOrDefaultAsync(c => c.Name == name, cancellationToken);

        return entity?.ToDomain();
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RegisteredChannel>> GetByFounderAsync(Guid founderId, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.RegisteredChannels
            .Where(c => c.FounderId == founderId)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToDomain());
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _dbContext.RegisteredChannels
            .AnyAsync(c => c.Name == name, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RegisteredChannel> CreateAsync(string name, Guid founderId, CancellationToken cancellationToken = default)
    {
        var entity = new ChannelRegistrationEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            FounderId = founderId,
            RegisteredAt = DateTimeOffset.UtcNow,
            LastUsedAt = DateTimeOffset.UtcNow,
            KeepTopic = true,
            Secure = false
        };

        _dbContext.RegisteredChannels.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity.ToDomain();
    }

    /// <inheritdoc />
    public async Task UpdateAsync(RegisteredChannel channel, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.RegisteredChannels
            .FirstOrDefaultAsync(c => c.Id == channel.Id, cancellationToken);

        if (entity is null)
        {
            throw new InvalidOperationException($"Channel registration {channel.Id} not found");
        }

        entity.Name = channel.Name;
        entity.FounderId = channel.FounderId;
        entity.Topic = channel.Topic;
        entity.Modes = channel.Modes;
        entity.Key = channel.Key;
        entity.LastUsedAt = channel.LastUsedAt;
        entity.KeepTopic = channel.KeepTopic;
        entity.Secure = channel.Secure;
        entity.SuccessorId = channel.SuccessorId;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.RegisteredChannels
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (entity is null)
        {
            return false;
        }

        _dbContext.RegisteredChannels.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.RegisteredChannels
            .FirstOrDefaultAsync(c => c.Name == name, cancellationToken);

        if (entity is null)
        {
            return false;
        }

        _dbContext.RegisteredChannels.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.RegisteredChannels.CountAsync(cancellationToken);
    }
}
