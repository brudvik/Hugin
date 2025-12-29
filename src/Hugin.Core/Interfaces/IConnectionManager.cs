namespace Hugin.Core.Interfaces;

/// <summary>
/// Manages client connections.
/// </summary>
public interface IConnectionManager
{
    /// <summary>
    /// Registers a new connection.
    /// </summary>
    void RegisterConnection(Guid connectionId, IClientConnection connection);

    /// <summary>
    /// Unregisters a connection.
    /// </summary>
    void UnregisterConnection(Guid connectionId);

    /// <summary>
    /// Gets a connection by ID.
    /// </summary>
    IClientConnection? GetConnection(Guid connectionId);

    /// <summary>
    /// Gets all connections.
    /// </summary>
    IEnumerable<IClientConnection> GetAllConnections();

    /// <summary>
    /// Gets the total connection count.
    /// </summary>
    int GetConnectionCount();

    /// <summary>
    /// Gets connections for a channel.
    /// </summary>
    IEnumerable<IClientConnection> GetChannelConnections(string channelName);

    /// <summary>
    /// Closes a connection.
    /// </summary>
    ValueTask CloseConnectionAsync(Guid connectionId, string reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a client connection.
/// </summary>
public interface IClientConnection
{
    /// <summary>
    /// Gets the unique connection identifier.
    /// </summary>
    Guid ConnectionId { get; }

    /// <summary>
    /// Gets whether the connection is active.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets whether the connection is using TLS.
    /// </summary>
    bool IsSecure { get; }

    /// <summary>
    /// Gets the client certificate fingerprint (if any).
    /// </summary>
    string? CertificateFingerprint { get; }

    /// <summary>
    /// Gets the remote endpoint.
    /// </summary>
    System.Net.EndPoint? RemoteEndPoint { get; }

    /// <summary>
    /// Sends data to the client.
    /// </summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the connection.
    /// </summary>
    ValueTask CloseAsync(CancellationToken cancellationToken = default);
}
