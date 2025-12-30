namespace Hugin.Core.Entities;

/// <summary>
/// Represents a registered (persistent) channel.
/// </summary>
public sealed class RegisteredChannel
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the channel name (including # prefix).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the account ID of the channel founder.
    /// </summary>
    public Guid FounderId { get; set; }

    /// <summary>
    /// Gets or sets the stored topic for the channel.
    /// </summary>
    public string? Topic { get; set; }

    /// <summary>
    /// Gets or sets the default channel modes.
    /// </summary>
    public string? Modes { get; set; }

    /// <summary>
    /// Gets or sets the channel key (password).
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// Gets or sets when the channel was registered.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>
    /// Gets or sets when the channel was last used.
    /// </summary>
    public DateTimeOffset LastUsedAt { get; set; }

    /// <summary>
    /// Gets or sets whether to keep the topic when the channel becomes empty.
    /// </summary>
    public bool KeepTopic { get; set; } = true;

    /// <summary>
    /// Gets or sets whether only identified users can join.
    /// </summary>
    public bool Secure { get; set; }

    /// <summary>
    /// Gets or sets the successor account ID (inherits if founder drops).
    /// </summary>
    public Guid? SuccessorId { get; set; }

    /// <summary>
    /// Creates a new registered channel.
    /// </summary>
    public RegisteredChannel()
    {
    }

    /// <summary>
    /// Creates a new registered channel with the specified values.
    /// </summary>
    /// <param name="id">The unique identifier.</param>
    /// <param name="name">The channel name.</param>
    /// <param name="founderId">The founder's account ID.</param>
    public RegisteredChannel(Guid id, string name, Guid founderId)
    {
        Id = id;
        Name = name;
        FounderId = founderId;
        RegisteredAt = DateTimeOffset.UtcNow;
        LastUsedAt = DateTimeOffset.UtcNow;
    }
}
