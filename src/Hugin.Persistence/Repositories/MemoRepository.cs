using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hugin.Persistence.Repositories;

/// <summary>
/// Entity Framework Core implementation of the memo repository.
/// </summary>
public sealed class MemoRepository : IMemoRepository
{
    private readonly HuginDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoRepository"/> class.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    public MemoRepository(HuginDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<Memo> CreateAsync(Guid senderId, string senderNickname, Guid recipientId, string text, CancellationToken cancellationToken = default)
    {
        var entity = new MemoEntity
        {
            Id = Guid.NewGuid(),
            SenderId = senderId,
            SenderNickname = senderNickname,
            RecipientId = recipientId,
            Text = text,
            SentAt = DateTimeOffset.UtcNow,
            ReadAt = null
        };

        _dbContext.Memos.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity.ToDomain();
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Memo>> GetByRecipientAsync(Guid recipientId, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.Memos
            .Where(m => m.RecipientId == recipientId)
            .OrderBy(m => m.SentAt)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToDomain());
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Memo>> GetUnreadByRecipientAsync(Guid recipientId, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.Memos
            .Where(m => m.RecipientId == recipientId && m.ReadAt == null)
            .OrderBy(m => m.SentAt)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToDomain());
    }

    /// <inheritdoc />
    public async Task<Memo?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Memos
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        return entity?.ToDomain();
    }

    /// <inheritdoc />
    public async Task MarkAsReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Memos.FindAsync(new object[] { id }, cancellationToken);
        if (entity is not null)
        {
            entity.ReadAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Memos.FindAsync(new object[] { id }, cancellationToken);
        if (entity is not null)
        {
            _dbContext.Memos.Remove(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<int> DeleteAllByRecipientAsync(Guid recipientId, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.Memos
            .Where(m => m.RecipientId == recipientId)
            .ToListAsync(cancellationToken);

        _dbContext.Memos.RemoveRange(entities);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entities.Count;
    }

    /// <inheritdoc />
    public async Task<int> GetUnreadCountAsync(Guid recipientId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Memos
            .CountAsync(m => m.RecipientId == recipientId && m.ReadAt == null, cancellationToken);
    }
}
