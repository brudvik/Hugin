using Hugin.Core.Entities;

namespace Hugin.Core.Interfaces;

/// <summary>
/// Repository for server link configurations.
/// </summary>
public interface IServerLinkRepository
{
    /// <summary>
    /// Gets all server links.
    /// </summary>
    Task<IReadOnlyList<ServerLinkEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all enabled server links.
    /// </summary>
    Task<IReadOnlyList<ServerLinkEntity>> GetEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all server links with auto-connect enabled.
    /// </summary>
    Task<IReadOnlyList<ServerLinkEntity>> GetAutoConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a server link by ID.
    /// </summary>
    Task<ServerLinkEntity?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a server link by name.
    /// </summary>
    Task<ServerLinkEntity?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a server link by SID.
    /// </summary>
    Task<ServerLinkEntity?> GetBySidAsync(string sid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new server link.
    /// </summary>
    Task<ServerLinkEntity> AddAsync(ServerLinkEntity link, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing server link.
    /// </summary>
    Task UpdateAsync(ServerLinkEntity link, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a server link.
    /// </summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last connected time for a server link.
    /// </summary>
    Task UpdateLastConnectedAsync(int id, DateTimeOffset connectedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a receive password for a server by name.
    /// </summary>
    Task<bool> ValidatePasswordAsync(string serverName, string password, CancellationToken cancellationToken = default);
}
