using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hugin.Persistence.Repositories;

/// <summary>
/// Entity Framework Core implementation of the bot repository.
/// </summary>
public sealed class BotRepository : IBotRepository
{
    private readonly HuginDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="BotRepository"/> class.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    public BotRepository(HuginDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<Bot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Bots
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        return entity?.ToDomain();
    }

    /// <inheritdoc />
    public async Task<Bot?> GetByNicknameAsync(string nickname, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Bots
            .FirstOrDefaultAsync(b => b.Nickname == nickname, cancellationToken);

        return entity?.ToDomain();
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Bot>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.Bots
            .Where(b => b.IsActive)
            .OrderBy(b => b.Nickname)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToDomain());
    }

    /// <inheritdoc />
    public async Task<Bot> CreateAsync(string nickname, string ident, string realname, string uid, CancellationToken cancellationToken = default)
    {
        var entity = new BotEntity
        {
            Id = Guid.NewGuid(),
            Nickname = nickname,
            Ident = ident,
            Realname = realname,
            Uid = uid,
            Host = "services.bot",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

        _dbContext.Bots.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity.ToDomain();
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Bot bot, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Bots
            .FirstOrDefaultAsync(b => b.Id == bot.Id, cancellationToken);

        if (entity is null)
        {
            throw new InvalidOperationException($"Bot {bot.Id} not found");
        }

        entity.Nickname = bot.Nickname;
        entity.Ident = bot.Ident;
        entity.Realname = bot.Realname;
        entity.Host = bot.Host;
        entity.Uid = bot.Uid;
        entity.IsActive = bot.IsActive;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Bots.FindAsync(new object[] { id }, cancellationToken);
        if (entity is not null)
        {
            _dbContext.Bots.Remove(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>
/// Entity Framework Core implementation of the channel bot repository.
/// </summary>
public sealed class ChannelBotRepository : IChannelBotRepository
{
    private readonly HuginDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelBotRepository"/> class.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    public ChannelBotRepository(HuginDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ChannelBot>> GetByChannelAsync(string channelName, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.ChannelBots
            .Where(cb => cb.ChannelName == channelName)
            .OrderBy(cb => cb.AssignedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToDomain());
    }

    /// <inheritdoc />
    public async Task<ChannelBot?> GetAssignmentAsync(Guid botId, string channelName, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.ChannelBots
            .FirstOrDefaultAsync(cb => cb.BotId == botId && cb.ChannelName == channelName, cancellationToken);

        return entity?.ToDomain();
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ChannelBot>> GetByBotAsync(Guid botId, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.ChannelBots
            .Where(cb => cb.BotId == botId)
            .OrderBy(cb => cb.ChannelName)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToDomain());
    }

    /// <inheritdoc />
    public async Task<ChannelBot> AssignAsync(Guid botId, string channelName, Guid assignedBy, CancellationToken cancellationToken = default)
    {
        var entity = new ChannelBotEntity
        {
            Id = Guid.NewGuid(),
            BotId = botId,
            ChannelName = channelName,
            AssignedBy = assignedBy,
            AssignedAt = DateTimeOffset.UtcNow,
            AutoGreet = false
        };

        _dbContext.ChannelBots.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity.ToDomain();
    }

    /// <inheritdoc />
    public async Task<bool> UnassignAsync(Guid botId, string channelName, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.ChannelBots
            .FirstOrDefaultAsync(cb => cb.BotId == botId && cb.ChannelName == channelName, cancellationToken);

        if (entity is null)
        {
            return false;
        }

        _dbContext.ChannelBots.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(ChannelBot assignment, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.ChannelBots
            .FirstOrDefaultAsync(cb => cb.Id == assignment.Id, cancellationToken);

        if (entity is null)
        {
            throw new InvalidOperationException($"Channel bot assignment {assignment.Id} not found");
        }

        entity.GreetMessage = assignment.GreetMessage;
        entity.AutoGreet = assignment.AutoGreet;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
