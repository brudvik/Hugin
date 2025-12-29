using System.Collections.Concurrent;
using System.Text;
using Hugin.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Hugin.Network.S2S;

/// <summary>
/// Interface for managing server-to-server connections.
/// </summary>
public interface IS2SConnectionManager
{
    /// <summary>
    /// Registers a new S2S connection.
    /// </summary>
    void RegisterConnection(Guid connectionId, IS2SConnection connection);

    /// <summary>
    /// Unregisters an S2S connection.
    /// </summary>
    void UnregisterConnection(Guid connectionId);

    /// <summary>
    /// Gets an S2S connection by its connection ID.
    /// </summary>
    IS2SConnection? GetConnection(Guid connectionId);

    /// <summary>
    /// Gets an S2S connection by the remote server ID.
    /// </summary>
    IS2SConnection? GetConnectionByServerId(ServerId serverId);

    /// <summary>
    /// Gets all active S2S connections.
    /// </summary>
    IEnumerable<IS2SConnection> GetAllConnections();

    /// <summary>
    /// Gets the number of active S2S connections.
    /// </summary>
    int GetConnectionCount();

    /// <summary>
    /// Sends a message to a specific server connection.
    /// </summary>
    ValueTask SendToConnectionAsync(Guid connectionId, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts a message to all connected servers.
    /// </summary>
    ValueTask BroadcastAsync(string message, Guid? exceptConnectionId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes a specific S2S connection.
    /// </summary>
    ValueTask CloseConnectionAsync(Guid connectionId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Associates a connection with a server ID after successful handshake.
    /// </summary>
    void AssociateServerId(Guid connectionId, ServerId serverId);
}

/// <summary>
/// Manages server-to-server connections.
/// </summary>
public sealed class S2SConnectionManager : IS2SConnectionManager
{
    private readonly ConcurrentDictionary<Guid, IS2SConnection> _connections = new();
    private readonly ConcurrentDictionary<string, Guid> _serverIdToConnection = new();
    private readonly ILogger<S2SConnectionManager> _logger;

    /// <summary>
    /// Creates a new S2S connection manager.
    /// </summary>
    public S2SConnectionManager(ILogger<S2SConnectionManager> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void RegisterConnection(Guid connectionId, IS2SConnection connection)
    {
        _connections[connectionId] = connection;
        _logger.LogInformation("Registered S2S connection {ConnectionId} from {RemoteEndPoint}",
            connectionId, connection.RemoteEndPoint);
    }

    /// <inheritdoc />
    public void UnregisterConnection(Guid connectionId)
    {
        if (_connections.TryRemove(connectionId, out var connection))
        {
            // Remove server ID mapping if exists
            if (connection.RemoteServerId is not null)
            {
                _serverIdToConnection.TryRemove(connection.RemoteServerId.Sid, out _);
            }

            _logger.LogInformation("Unregistered S2S connection {ConnectionId}", connectionId);
        }
    }

    /// <inheritdoc />
    public IS2SConnection? GetConnection(Guid connectionId)
    {
        return _connections.GetValueOrDefault(connectionId);
    }

    /// <inheritdoc />
    public IS2SConnection? GetConnectionByServerId(ServerId serverId)
    {
        if (_serverIdToConnection.TryGetValue(serverId.Sid, out var connectionId))
        {
            return GetConnection(connectionId);
        }
        return null;
    }

    /// <inheritdoc />
    public IEnumerable<IS2SConnection> GetAllConnections()
    {
        return _connections.Values;
    }

    /// <inheritdoc />
    public int GetConnectionCount()
    {
        return _connections.Count;
    }

    /// <inheritdoc />
    public async ValueTask SendToConnectionAsync(Guid connectionId, string message, CancellationToken cancellationToken = default)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
        {
            try
            {
                await connection.SendLineAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send to S2S connection {ConnectionId}", connectionId);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask BroadcastAsync(string message, Guid? exceptConnectionId = null, CancellationToken cancellationToken = default)
    {
        var data = Encoding.UTF8.GetBytes(message + "\r\n");
        var tasks = new List<Task>();

        foreach (var connection in _connections.Values)
        {
            if (exceptConnectionId.HasValue && connection.ConnectionId == exceptConnectionId.Value)
            {
                continue;
            }

            tasks.Add(SendSafeAsync(connection, data, cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    /// <inheritdoc />
    public async ValueTask CloseConnectionAsync(Guid connectionId, string reason, CancellationToken cancellationToken = default)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
        {
            try
            {
                await connection.SendLineAsync($"ERROR :Closing Link: {reason}", cancellationToken);
            }
            catch
            {
                // Ignore send errors
            }

            await connection.CloseAsync(cancellationToken);
            UnregisterConnection(connectionId);
        }
    }

    /// <inheritdoc />
    public void AssociateServerId(Guid connectionId, ServerId serverId)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
        {
            connection.RemoteServerId = serverId;
            _serverIdToConnection[serverId.Sid] = connectionId;
            _logger.LogInformation("Associated S2S connection {ConnectionId} with server {ServerId} ({ServerName})",
                connectionId, serverId.Sid, serverId.Name);
        }
    }

    private async Task SendSafeAsync(IS2SConnection connection, byte[] data, CancellationToken cancellationToken)
    {
        try
        {
            await connection.SendAsync(data, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send to S2S connection {ConnectionId}", connection.ConnectionId);
        }
    }
}
