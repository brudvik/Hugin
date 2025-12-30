using Hugin.Core.Entities;

namespace Hugin.Core.Interfaces;

/// <summary>
/// Repository for managing virtual hosts.
/// </summary>
public interface IVirtualHostRepository
{
    /// <summary>
    /// Gets a vhost by ID.
    /// </summary>
    /// <param name="id">Vhost ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The vhost if found.</returns>
    Task<VirtualHost?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the active vhost for an account.
    /// </summary>
    /// <param name="accountId">Account ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The active vhost if found.</returns>
    Task<VirtualHost?> GetActiveByAccountAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all vhosts for an account (active and inactive).
    /// </summary>
    /// <param name="accountId">Account ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of vhosts.</returns>
    Task<IEnumerable<VirtualHost>> GetByAccountAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all pending vhost requests.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of pending vhosts.</returns>
    Task<IEnumerable<VirtualHost>> GetPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a hostname is already in use.
    /// </summary>
    /// <param name="hostname">Hostname to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if in use, false otherwise.</returns>
    Task<bool> IsHostnameInUseAsync(string hostname, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new vhost request.
    /// </summary>
    /// <param name="accountId">Account ID.</param>
    /// <param name="hostname">Requested hostname.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created vhost.</returns>
    Task<VirtualHost> CreateAsync(Guid accountId, string hostname, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a vhost.
    /// </summary>
    /// <param name="vhost">Vhost to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateAsync(VirtualHost vhost, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a vhost.
    /// </summary>
    /// <param name="id">Vhost ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates all vhosts for an account.
    /// </summary>
    /// <param name="accountId">Account ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of vhosts deactivated.</returns>
    Task<int> DeactivateAllForAccountAsync(Guid accountId, CancellationToken cancellationToken = default);
}
