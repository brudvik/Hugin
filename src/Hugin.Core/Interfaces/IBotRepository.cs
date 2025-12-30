using Hugin.Core.Entities;

namespace Hugin.Core.Interfaces;

/// <summary>
/// Repository for managing bots.
/// </summary>
public interface IBotRepository
{
    /// <summary>
    /// Gets a bot by ID.
    /// </summary>
    /// <param name="id">Bot ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bot if found.</returns>
    Task<Bot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a bot by nickname.
    /// </summary>
    /// <param name="nickname">Bot nickname.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bot if found.</returns>
    Task<Bot?> GetByNicknameAsync(string nickname, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active bots.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of active bots.</returns>
    Task<IEnumerable<Bot>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new bot.
    /// </summary>
    /// <param name="nickname">Bot nickname.</param>
    /// <param name="ident">Bot ident.</param>
    /// <param name="realname">Bot realname.</param>
    /// <param name="uid">Bot UID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created bot.</returns>
    Task<Bot> CreateAsync(string nickname, string ident, string realname, string uid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a bot.
    /// </summary>
    /// <param name="bot">Bot to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateAsync(Bot bot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a bot.
    /// </summary>
    /// <param name="id">Bot ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository for managing channel bot assignments.
/// </summary>
public interface IChannelBotRepository
{
    /// <summary>
    /// Gets all bot assignments for a channel.
    /// </summary>
    /// <param name="channelName">Channel name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of channel bot assignments.</returns>
    Task<IEnumerable<ChannelBot>> GetByChannelAsync(string channelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the bot assignment for a specific bot in a channel.
    /// </summary>
    /// <param name="botId">Bot ID.</param>
    /// <param name="channelName">Channel name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The assignment if found.</returns>
    Task<ChannelBot?> GetAssignmentAsync(Guid botId, string channelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all channels a bot is assigned to.
    /// </summary>
    /// <param name="botId">Bot ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of channel assignments.</returns>
    Task<IEnumerable<ChannelBot>> GetByBotAsync(Guid botId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a bot to a channel.
    /// </summary>
    /// <param name="botId">Bot ID.</param>
    /// <param name="channelName">Channel name.</param>
    /// <param name="assignedBy">Account ID of assigner.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created assignment.</returns>
    Task<ChannelBot> AssignAsync(Guid botId, string channelName, Guid assignedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unassigns a bot from a channel.
    /// </summary>
    /// <param name="botId">Bot ID.</param>
    /// <param name="channelName">Channel name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if unassigned, false if not found.</returns>
    Task<bool> UnassignAsync(Guid botId, string channelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a channel bot assignment.
    /// </summary>
    /// <param name="assignment">Assignment to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateAsync(ChannelBot assignment, CancellationToken cancellationToken = default);
}
