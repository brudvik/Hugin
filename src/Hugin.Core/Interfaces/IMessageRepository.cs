using Hugin.Core.Entities;

namespace Hugin.Core.Interfaces;

/// <summary>
/// Repository for message history storage.
/// </summary>
public interface IMessageRepository
{
    /// <summary>
    /// Stores a message.
    /// </summary>
    Task StoreAsync(StoredMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets messages for a target (channel or user) after a timestamp.
    /// </summary>
    Task<IEnumerable<StoredMessage>> GetAfterAsync(
        string target,
        DateTimeOffset after,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets messages for a target before a timestamp.
    /// </summary>
    Task<IEnumerable<StoredMessage>> GetBeforeAsync(
        string target,
        DateTimeOffset before,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets messages around a specific message ID.
    /// </summary>
    Task<IEnumerable<StoredMessage>> GetAroundAsync(
        string target,
        string messageId,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets messages between two timestamps.
    /// </summary>
    Task<IEnumerable<StoredMessage>> GetBetweenAsync(
        string target,
        DateTimeOffset start,
        DateTimeOffset endTime,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the latest messages for a target.
    /// </summary>
    Task<IEnumerable<StoredMessage>> GetLatestAsync(
        string target,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a message by ID.
    /// </summary>
    Task<StoredMessage?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all targets (channels/users) a user has history with.
    /// </summary>
    Task<IEnumerable<string>> GetTargetsForAccountAsync(
        string accountName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes old messages (for retention policy).
    /// </summary>
    Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all messages for a target.
    /// </summary>
    Task DeleteForTargetAsync(string target, CancellationToken cancellationToken = default);
}
