using Hugin.Core.Entities;

namespace Hugin.Core.Interfaces;

/// <summary>
/// Repository for managing memos (offline messages).
/// </summary>
public interface IMemoRepository
{
    /// <summary>
    /// Creates a new memo.
    /// </summary>
    /// <param name="senderId">Sender's account ID.</param>
    /// <param name="senderNickname">Sender's nickname.</param>
    /// <param name="recipientId">Recipient's account ID.</param>
    /// <param name="text">Memo text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created memo.</returns>
    Task<Memo> CreateAsync(Guid senderId, string senderNickname, Guid recipientId, string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all memos for a recipient.
    /// </summary>
    /// <param name="recipientId">Recipient's account ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of memos.</returns>
    Task<IEnumerable<Memo>> GetByRecipientAsync(Guid recipientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets unread memos for a recipient.
    /// </summary>
    /// <param name="recipientId">Recipient's account ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of unread memos.</returns>
    Task<IEnumerable<Memo>> GetUnreadByRecipientAsync(Guid recipientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a memo by ID.
    /// </summary>
    /// <param name="id">Memo ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The memo if found.</returns>
    Task<Memo?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a memo as read.
    /// </summary>
    /// <param name="id">Memo ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkAsReadAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a memo.
    /// </summary>
    /// <param name="id">Memo ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all memos for a recipient.
    /// </summary>
    /// <param name="recipientId">Recipient's account ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of memos deleted.</returns>
    Task<int> DeleteAllByRecipientAsync(Guid recipientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of unread memos for a recipient.
    /// </summary>
    /// <param name="recipientId">Recipient's account ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Count of unread memos.</returns>
    Task<int> GetUnreadCountAsync(Guid recipientId, CancellationToken cancellationToken = default);
}
