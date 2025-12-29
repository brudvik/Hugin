namespace Hugin.Core.Interfaces;

/// <summary>
/// Provides read-only information about server links.
/// This interface is used by the Protocol layer to query server link status
/// without depending on the S2S protocol implementation.
/// </summary>
public interface IServerLinkInfo
{
    /// <summary>
    /// Checks if a server is currently linked.
    /// </summary>
    /// <param name="serverName">The server name to check.</param>
    /// <returns>True if the server is linked; otherwise false.</returns>
    bool IsServerLinked(string serverName);

    /// <summary>
    /// Gets the names of all linked servers.
    /// </summary>
    IEnumerable<string> LinkedServerNames { get; }

    /// <summary>
    /// Gets the number of linked servers.
    /// </summary>
    int LinkedServerCount { get; }

    /// <summary>
    /// Requests disconnection of a server.
    /// </summary>
    /// <param name="serverName">The server name to disconnect.</param>
    /// <param name="reason">The reason for disconnection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if disconnect was initiated; otherwise false.</returns>
    ValueTask<bool> DisconnectServerAsync(string serverName, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests connection to a server.
    /// </summary>
    /// <param name="serverName">The server name to connect to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if connection was initiated; otherwise false.</returns>
    ValueTask<bool> ConnectServerAsync(string serverName, CancellationToken cancellationToken = default);
}
