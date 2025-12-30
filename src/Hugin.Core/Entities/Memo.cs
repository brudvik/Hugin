namespace Hugin.Core.Entities;

/// <summary>
/// Represents a memo (offline message) between users.
/// </summary>
public sealed class Memo
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the sender's account ID.
    /// </summary>
    public Guid SenderId { get; set; }

    /// <summary>
    /// Gets or sets the sender's nickname at time of sending.
    /// </summary>
    public string SenderNickname { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the recipient's account ID.
    /// </summary>
    public Guid RecipientId { get; set; }

    /// <summary>
    /// Gets or sets the memo text.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the memo was sent.
    /// </summary>
    public DateTimeOffset SentAt { get; set; }

    /// <summary>
    /// Gets or sets when the memo was read (null if unread).
    /// </summary>
    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>
    /// Gets whether the memo has been read.
    /// </summary>
    public bool IsRead => ReadAt.HasValue;

    /// <summary>
    /// Creates a new memo.
    /// </summary>
    public Memo()
    {
    }

    /// <summary>
    /// Creates a new memo with the specified values.
    /// </summary>
    public Memo(Guid id, Guid senderId, string senderNickname, Guid recipientId, string text, DateTimeOffset sentAt)
    {
        Id = id;
        SenderId = senderId;
        SenderNickname = senderNickname;
        RecipientId = recipientId;
        Text = text;
        SentAt = sentAt;
    }

    /// <summary>
    /// Marks the memo as read.
    /// </summary>
    public void MarkAsRead()
    {
        ReadAt = DateTimeOffset.UtcNow;
    }
}
