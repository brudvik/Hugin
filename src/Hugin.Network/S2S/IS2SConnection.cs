using System.Net;
using Hugin.Core.ValueObjects;

namespace Hugin.Network.S2S;

/// <summary>
/// Represents a server-to-server connection.
/// </summary>
public interface IS2SConnection : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique connection identifier.
    /// </summary>
    Guid ConnectionId { get; }

    /// <summary>
    /// Gets whether the connection is still active.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets whether the connection is using TLS.
    /// </summary>
    bool IsSecure { get; }

    /// <summary>
    /// Gets the remote endpoint of the connection.
    /// </summary>
    EndPoint? RemoteEndPoint { get; }

    /// <summary>
    /// Gets or sets the remote server ID (set after successful handshake).
    /// </summary>
    ServerId? RemoteServerId { get; set; }

    /// <summary>
    /// Gets whether this is an outgoing connection (we initiated it).
    /// </summary>
    bool IsOutgoing { get; }

    /// <summary>
    /// Sends raw bytes to the connected server.
    /// </summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a string line to the connected server.
    /// </summary>
    ValueTask SendLineAsync(string line, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the connection.
    /// </summary>
    ValueTask CloseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts reading from the connection.
    /// </summary>
    Task StartReadingAsync();

    /// <summary>
    /// Event raised when a complete IRC line is received.
    /// </summary>
    event Func<IS2SConnection, string, ValueTask>? LineReceived;

    /// <summary>
    /// Event raised when the connection is closed.
    /// </summary>
    event Func<IS2SConnection, Exception?, ValueTask>? Disconnected;
}
