using System.Collections.Concurrent;
using System.Text;
using Hugin.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hugin.Network;

/// <summary>
/// Manages client connections.
/// </summary>
public sealed class ConnectionManager : IConnectionManager
{
    private readonly ConcurrentDictionary<Guid, ClientConnection> _connections = new();
    private readonly ConcurrentDictionary<string, HashSet<Guid>> _channelMembers = new();
    private readonly ILogger<ConnectionManager> _logger;
    private readonly object _channelLock = new();

    /// <summary>
    /// Creates a new connection manager.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public ConnectionManager(ILogger<ConnectionManager> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void RegisterConnection(Guid connectionId, IClientConnection connection)
    {
        if (connection is ClientConnection clientConnection)
        {
            _connections[connectionId] = clientConnection;
            _logger.LogDebug("Registered connection {ConnectionId}", connectionId);
        }
    }

    public void UnregisterConnection(Guid connectionId)
    {
        if (_connections.TryRemove(connectionId, out _))
        {
            // Remove from all channels
            lock (_channelLock)
            {
                foreach (var members in _channelMembers.Values)
                {
                    members.Remove(connectionId);
                }
            }
            _logger.LogDebug("Unregistered connection {ConnectionId}", connectionId);
        }
    }

    public IClientConnection? GetConnection(Guid connectionId)
    {
        return _connections.GetValueOrDefault(connectionId);
    }

    public IEnumerable<IClientConnection> GetAllConnections()
    {
        return _connections.Values;
    }

    public int GetConnectionCount()
    {
        return _connections.Count;
    }

    public IEnumerable<IClientConnection> GetChannelConnections(string channelName)
    {
        lock (_channelLock)
        {
            if (_channelMembers.TryGetValue(channelName, out var members))
            {
                return members
                    .Select(id => _connections.GetValueOrDefault(id))
                    .Where(c => c is not null)
                    .Cast<IClientConnection>()
                    .ToList();
            }
        }
        return Enumerable.Empty<IClientConnection>();
    }

    /// <summary>
    /// Adds a connection to a channel.
    /// </summary>
    public void JoinChannel(Guid connectionId, string channelName)
    {
        lock (_channelLock)
        {
            if (!_channelMembers.TryGetValue(channelName, out var members))
            {
                members = new HashSet<Guid>();
                _channelMembers[channelName] = members;
            }
            members.Add(connectionId);
        }
    }

    /// <summary>
    /// Removes a connection from a channel.
    /// </summary>
    public void PartChannel(Guid connectionId, string channelName)
    {
        lock (_channelLock)
        {
            if (_channelMembers.TryGetValue(channelName, out var members))
            {
                members.Remove(connectionId);
                if (members.Count == 0)
                {
                    _channelMembers.TryRemove(channelName, out _);
                }
            }
        }
    }

    public async ValueTask CloseConnectionAsync(Guid connectionId, string reason, CancellationToken cancellationToken = default)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
        {
            // Send ERROR message before closing
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
}

/// <summary>
/// Implementation of message broker for distributing IRC messages to clients.
/// </summary>
public sealed class MessageBroker : IMessageBroker
{
    private readonly ConnectionManager _connectionManager;
    private readonly ILogger<MessageBroker> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageBroker"/> class.
    /// </summary>
    /// <param name="connectionManager">The connection manager.</param>
    /// <param name="logger">The logger instance.</param>
    public MessageBroker(ConnectionManager connectionManager, ILogger<MessageBroker> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    /// <summary>
    /// Sends a message to a specific connection.
    /// </summary>
    /// <param name="connectionId">The target connection ID.</param>
    /// <param name="message">The IRC message to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask SendToConnectionAsync(Guid connectionId, string message, CancellationToken cancellationToken = default)
    {
        if (_connectionManager.GetConnection(connectionId) is ClientConnection connection)
        {
            try
            {
                await connection.SendLineAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send to connection {ConnectionId}", connectionId);
            }
        }
    }

    /// <summary>
    /// Sends a message to multiple connections.
    /// </summary>
    /// <param name="connectionIds">The target connection IDs.</param>
    /// <param name="message">The IRC message to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask SendToConnectionsAsync(IEnumerable<Guid> connectionIds, string message, CancellationToken cancellationToken = default)
    {
        var data = Encoding.UTF8.GetBytes(message + "\r\n");
        var tasks = new List<Task>();

        foreach (var connectionId in connectionIds)
        {
            if (_connectionManager.GetConnection(connectionId) is ClientConnection connection)
            {
                tasks.Add(SendSafeAsync(connection, data, cancellationToken));
            }
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Sends a message to all members of a channel.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <param name="message">The IRC message to send.</param>
    /// <param name="exceptConnectionId">Optional connection ID to exclude (e.g., the sender).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask SendToChannelAsync(string channelName, string message, Guid? exceptConnectionId = null, CancellationToken cancellationToken = default)
    {
        var data = Encoding.UTF8.GetBytes(message + "\r\n");
        var connections = _connectionManager.GetChannelConnections(channelName);
        var tasks = new List<Task>();

        foreach (var connection in connections)
        {
            if (exceptConnectionId.HasValue && connection.ConnectionId == exceptConnectionId.Value)
            {
                continue;
            }

            if (connection is ClientConnection clientConnection)
            {
                tasks.Add(SendSafeAsync(clientConnection, data, cancellationToken));
            }
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Sends a message to all members of multiple channels, deduplicating recipients.
    /// </summary>
    /// <param name="channelNames">The channel names.</param>
    /// <param name="message">The IRC message to send.</param>
    /// <param name="exceptConnectionId">Optional connection ID to exclude (e.g., the sender).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask SendToChannelsAsync(IEnumerable<string> channelNames, string message, Guid? exceptConnectionId = null, CancellationToken cancellationToken = default)
    {
        var data = Encoding.UTF8.GetBytes(message + "\r\n");
        var sentTo = new HashSet<Guid>();
        var tasks = new List<Task>();

        foreach (var channelName in channelNames)
        {
            foreach (var connection in _connectionManager.GetChannelConnections(channelName))
            {
                if (exceptConnectionId.HasValue && connection.ConnectionId == exceptConnectionId.Value)
                {
                    continue;
                }

                // Deduplicate
                if (!sentTo.Add(connection.ConnectionId))
                {
                    continue;
                }

                if (connection is ClientConnection clientConnection)
                {
                    tasks.Add(SendSafeAsync(clientConnection, data, cancellationToken));
                }
            }
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Broadcasts a message to all connected clients.
    /// </summary>
    /// <param name="message">The IRC message to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask BroadcastAsync(string message, CancellationToken cancellationToken = default)
    {
        var data = Encoding.UTF8.GetBytes(message + "\r\n");
        var tasks = new List<Task>();

        foreach (var connection in _connectionManager.GetAllConnections())
        {
            if (connection is ClientConnection clientConnection)
            {
                tasks.Add(SendSafeAsync(clientConnection, data, cancellationToken));
            }
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Sends a message to all IRC operators.
    /// </summary>
    /// <param name="message">The IRC message to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask SendToOperatorsAsync(string message, CancellationToken cancellationToken = default)
    {
        // TODO: Filter by operator status
        var data = Encoding.UTF8.GetBytes(message + "\r\n");
        var tasks = new List<Task>();

        foreach (var connection in _connectionManager.GetAllConnections())
        {
            // Would need access to user repository to check operator status
            if (connection is ClientConnection clientConnection)
            {
                tasks.Add(SendSafeAsync(clientConnection, data, cancellationToken));
            }
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Sends a message to a linked server (server-to-server communication).
    /// </summary>
    /// <param name="serverId">The target server identifier.</param>
    /// <param name="message">The IRC message to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>This method is not yet implemented pending S2S protocol support.</remarks>
    public async ValueTask SendToServerAsync(string serverId, string message, CancellationToken cancellationToken = default)
    {
        // TODO: Implement S2S routing
        await Task.CompletedTask;
    }

    /// <summary>
    /// Safely sends data to a connection, catching and logging any errors.
    /// </summary>
    /// <param name="connection">The client connection.</param>
    /// <param name="data">The raw bytes to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task SendSafeAsync(ClientConnection connection, byte[] data, CancellationToken cancellationToken)
    {
        try
        {
            await connection.SendAsync(data, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send to connection {ConnectionId}", connection.ConnectionId);
        }
    }
}
