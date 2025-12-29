using Hugin.Core.Entities;

namespace Hugin.Core.Interfaces;

/// <summary>
/// Repository for managing server bans (K-Lines, G-Lines, Z-Lines).
/// </summary>
public interface IServerBanRepository
{
    /// <summary>
    /// Adds a new ban.
    /// </summary>
    /// <param name="ban">The ban to add.</param>
    void Add(ServerBan ban);

    /// <summary>
    /// Removes a ban by pattern and type.
    /// </summary>
    /// <param name="type">The type of ban.</param>
    /// <param name="pattern">The pattern to remove.</param>
    /// <returns>True if a ban was removed; otherwise false.</returns>
    bool Remove(BanType type, string pattern);

    /// <summary>
    /// Removes a ban by ID.
    /// </summary>
    /// <param name="id">The ban ID.</param>
    /// <returns>True if a ban was removed; otherwise false.</returns>
    bool Remove(Guid id);

    /// <summary>
    /// Gets all bans of a specific type.
    /// </summary>
    /// <param name="type">The type of ban.</param>
    /// <returns>All bans of the specified type.</returns>
    IReadOnlyList<ServerBan> GetByType(BanType type);

    /// <summary>
    /// Gets all active (non-expired) bans.
    /// </summary>
    /// <returns>All active bans.</returns>
    IReadOnlyList<ServerBan> GetAllActive();

    /// <summary>
    /// Checks if a user@host matches any active K-Line or G-Line.
    /// </summary>
    /// <param name="userHost">The user@host to check.</param>
    /// <returns>The matching ban if found; otherwise null.</returns>
    ServerBan? FindMatchingBan(string userHost);

    /// <summary>
    /// Checks if an IP address matches any active Z-Line.
    /// </summary>
    /// <param name="ipAddress">The IP address to check.</param>
    /// <returns>The matching ban if found; otherwise null.</returns>
    ServerBan? FindMatchingZLine(string ipAddress);

    /// <summary>
    /// Removes all expired bans.
    /// </summary>
    /// <returns>Number of expired bans removed.</returns>
    int PurgeExpired();
}
