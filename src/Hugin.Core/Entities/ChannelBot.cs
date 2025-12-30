namespace Hugin.Core.Entities;

/// <summary>
/// Represents a bot assignment to a channel.
/// </summary>
public sealed class ChannelBot
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the bot ID.
    /// </summary>
    public Guid BotId { get; set; }

    /// <summary>
    /// Gets or sets the channel name (e.g., "#channel").
    /// </summary>
    public string ChannelName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the account ID of who assigned the bot.
    /// </summary>
    public Guid AssignedBy { get; set; }

    /// <summary>
    /// Gets or sets when the bot was assigned.
    /// </summary>
    public DateTimeOffset AssignedAt { get; set; }

    /// <summary>
    /// Gets or sets optional greeting message the bot sends on join.
    /// </summary>
    public string? GreetMessage { get; set; }

    /// <summary>
    /// Gets or sets whether the bot should auto-greet new joiners.
    /// </summary>
    public bool AutoGreet { get; set; }

    /// <summary>
    /// Creates a new channel bot assignment.
    /// </summary>
    public ChannelBot()
    {
    }

    /// <summary>
    /// Creates a new channel bot assignment with the specified values.
    /// </summary>
    /// <param name="id">Assignment identifier.</param>
    /// <param name="botId">Bot identifier.</param>
    /// <param name="channelName">Channel name.</param>
    /// <param name="assignedBy">Account ID of assigner.</param>
    public ChannelBot(Guid id, Guid botId, string channelName, Guid assignedBy)
    {
        Id = id;
        BotId = botId;
        ChannelName = channelName;
        AssignedBy = assignedBy;
        AssignedAt = DateTimeOffset.UtcNow;
        AutoGreet = false;
    }
}
