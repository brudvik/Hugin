// CA1304/CA1311/CA1862: ToLower/ToUpper in EF Core LINQ queries are translated to SQL LOWER()/UPPER()
// which are culture-invariant. Suppressing these warnings is intentional.
#pragma warning disable CA1304, CA1311, CA1862

using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hugin.Persistence.Repositories;

/// <summary>
/// PostgreSQL implementation of message repository for chat history.
/// </summary>
public sealed class MessageRepository : IMessageRepository
{
    private readonly HuginDbContext _context;

    /// <summary>
    /// Creates a new message repository.
    /// </summary>
    /// <param name="context">The database context.</param>
    public MessageRepository(HuginDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task StoreAsync(StoredMessage message, CancellationToken cancellationToken = default)
    {
        var entity = StoredMessageEntity.FromDomain(message);
        _context.Messages.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<StoredMessage>> GetAfterAsync(
        string target, DateTimeOffset after, int limit = 100, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Messages
            .Where(m => m.Target.ToLower() == target.ToLower() && m.Timestamp > after)
            .OrderBy(m => m.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToDomain());
    }

    public async Task<IEnumerable<StoredMessage>> GetBeforeAsync(
        string target, DateTimeOffset before, int limit = 100, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Messages
            .Where(m => m.Target.ToLower() == target.ToLower() && m.Timestamp < before)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);

        // Return in chronological order
        return entities.OrderBy(e => e.Timestamp).Select(e => e.ToDomain());
    }

    public async Task<IEnumerable<StoredMessage>> GetAroundAsync(
        string target, string messageId, int limit = 100, CancellationToken cancellationToken = default)
    {
        // Find the reference message
        var reference = await _context.Messages
            .FirstOrDefaultAsync(m => m.MessageId == messageId, cancellationToken);

        if (reference is null)
        {
            return Enumerable.Empty<StoredMessage>();
        }

        int halfLimit = limit / 2;

        var before = await _context.Messages
            .Where(m => m.Target.ToLower() == target.ToLower() && m.Timestamp < reference.Timestamp)
            .OrderByDescending(m => m.Timestamp)
            .Take(halfLimit)
            .ToListAsync(cancellationToken);

        var after = await _context.Messages
            .Where(m => m.Target.ToLower() == target.ToLower() && m.Timestamp >= reference.Timestamp)
            .OrderBy(m => m.Timestamp)
            .Take(limit - before.Count)
            .ToListAsync(cancellationToken);

        return before.OrderBy(e => e.Timestamp)
            .Concat(after)
            .Select(e => e.ToDomain());
    }

    public async Task<IEnumerable<StoredMessage>> GetBetweenAsync(
        string target, DateTimeOffset start, DateTimeOffset endTime, int limit = 100, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Messages
            .Where(m => m.Target.ToLower() == target.ToLower() &&
                        m.Timestamp >= start && m.Timestamp <= endTime)
            .OrderBy(m => m.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToDomain());
    }

    public async Task<IEnumerable<StoredMessage>> GetLatestAsync(
        string target, int limit = 100, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Messages
            .Where(m => m.Target.ToLower() == target.ToLower())
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);

        // Return in chronological order
        return entities.OrderBy(e => e.Timestamp).Select(e => e.ToDomain());
    }

    public async Task<StoredMessage?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Messages
            .FirstOrDefaultAsync(m => m.MessageId == messageId, cancellationToken);

        return entity?.ToDomain();
    }

    public async Task<IEnumerable<string>> GetTargetsForAccountAsync(
        string accountName, CancellationToken cancellationToken = default)
    {
        return await _context.Messages
            .Where(m => m.SenderAccount != null && m.SenderAccount.ToLower() == accountName.ToLower())
            .Select(m => m.Target)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await _context.Messages
            .Where(m => m.Timestamp < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task DeleteForTargetAsync(string target, CancellationToken cancellationToken = default)
    {
        await _context.Messages
            .Where(m => m.Target.ToLower() == target.ToLower())
            .ExecuteDeleteAsync(cancellationToken);
    }
}
