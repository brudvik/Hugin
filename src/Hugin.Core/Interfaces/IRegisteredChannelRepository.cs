using Hugin.Core.Entities;

namespace Hugin.Core.Interfaces;

/// <summary>
/// Repository for persistent channel registration storage.
/// </summary>
public interface IRegisteredChannelRepository
{
    /// <summary>
    /// Gets a registered channel by ID.
    /// </summary>
    /// <param name="id">The channel registration ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registered channel, or null if not found.</returns>
    Task<RegisteredChannel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a registered channel by name.
    /// </summary>
    /// <param name="name">The channel name (including # prefix).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registered channel, or null if not found.</returns>
    Task<RegisteredChannel?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all registered channels for a founder.
    /// </summary>
    /// <param name="founderId">The account ID of the founder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of registered channels.</returns>
    Task<IEnumerable<RegisteredChannel>> GetByFounderAsync(Guid founderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a channel is registered.
    /// </summary>
    /// <param name="name">The channel name (including # prefix).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the channel is registered; otherwise false.</returns>
    Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a new channel.
    /// </summary>
    /// <param name="name">The channel name (including # prefix).</param>
    /// <param name="founderId">The account ID of the founder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly registered channel.</returns>
    Task<RegisteredChannel> CreateAsync(string name, Guid founderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a registered channel.
    /// </summary>
    /// <param name="channel">The registered channel to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateAsync(RegisteredChannel channel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a channel registration.
    /// </summary>
    /// <param name="id">The channel registration ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the channel was deleted; otherwise false.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a channel registration by name.
    /// </summary>
    /// <param name="name">The channel name (including # prefix).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the channel was deleted; otherwise false.</returns>
    Task<bool> DeleteByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total count of registered channels.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of registered channels.</returns>
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);
}
