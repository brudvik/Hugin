using Hugin.Core.ValueObjects;

namespace Hugin.Core.Entities;

/// <summary>
/// Represents a message stored for chat history (IRCv3 chathistory).
/// </summary>
public sealed class StoredMessage
{
    /// <summary>
    /// Gets the unique message ID (msgid tag).
    /// </summary>
    public string MessageId { get; }

    /// <summary>
    /// Gets the message timestamp.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Gets the sender's hostmask at the time of sending.
    /// </summary>
    public string SenderHostmask { get; }

    /// <summary>
    /// Gets the sender's account name (if authenticated).
    /// </summary>
    public string? SenderAccount { get; }

    /// <summary>
    /// Gets the target (channel name or nickname).
    /// </summary>
    public string Target { get; }

    /// <summary>
    /// Gets the message type (PRIVMSG or NOTICE).
    /// </summary>
    public MessageType Type { get; }

    /// <summary>
    /// Gets the message content.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// Gets the message tags (serialized).
    /// </summary>
    public string? Tags { get; }

    /// <summary>
    /// Creates a new stored message.
    /// </summary>
    /// <param name="messageId">The unique message ID.</param>
    /// <param name="timestamp">The message timestamp.</param>
    /// <param name="senderHostmask">The sender's hostmask.</param>
    /// <param name="senderAccount">The sender's account name.</param>
    /// <param name="target">The message target.</param>
    /// <param name="type">The message type.</param>
    /// <param name="content">The message content.</param>
    /// <param name="tags">Optional message tags.</param>
    public StoredMessage(
        string messageId,
        DateTimeOffset timestamp,
        string senderHostmask,
        string? senderAccount,
        string target,
        MessageType type,
        string content,
        string? tags = null)
    {
        MessageId = messageId;
        Timestamp = timestamp;
        SenderHostmask = senderHostmask;
        SenderAccount = senderAccount;
        Target = target;
        Type = type;
        Content = content;
        Tags = tags;
    }
}

/// <summary>
/// Message type for stored messages.
/// </summary>
public enum MessageType
{
    Privmsg,
    Notice
}
