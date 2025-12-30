using System.Collections.Concurrent;
using System.Text;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Hugin.Network.S2S;
using Microsoft.Extensions.Logging;

namespace Hugin.Network;

/// <summary>
/// Manages client connections.
/// </summary>
public sealed class ConnectionManager : IConnectionManager
{
    private readonly ConcurrentDictionary<Guid, ClientConnection> _connections = new();
    private readonly ILogger<ConnectionManager> _logger;

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

    /// <summary>
    /// Gets connections for a channel by looking up members in the channel repository.
    /// </summary>
    /// <remarks>
    /// This implementation requires a channel reference. For message routing, use
    /// <see cref="IMessageBroker.SendToChannelAsync"/> which handles this internally.
    /// </remarks>
    public IEnumerable<IClientConnection> GetChannelConnections(string channelName)
    {
        // This method is kept for interface compliance but channel message routing
        // is now handled by MessageBroker using IChannelRepository
        return Enumerable.Empty<IClientConnection>();
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
    private readonly IChannelRepository _channelRepository;
    private readonly IUserRepository _userRepository;
    private readonly IS2SConnectionManager? _s2sConnectionManager;
    private readonly ILogger<MessageBroker> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageBroker"/> class.
    /// </summary>
    /// <param name="connectionManager">The connection manager.</param>
    /// <param name="channelRepository">The channel repository.</param>
    /// <param name="userRepository">The user repository.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="s2sConnectionManager">The S2S connection manager (optional).</param>
    public MessageBroker(
        ConnectionManager connectionManager, 
        IChannelRepository channelRepository, 
        IUserRepository userRepository,
        ILogger<MessageBroker> logger,
        IS2SConnectionManager? s2sConnectionManager = null)
    {
        _connectionManager = connectionManager;
        _channelRepository = channelRepository;
        _userRepository = userRepository;
        _logger = logger;
        _s2sConnectionManager = s2sConnectionManager;
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
        // Try to parse channel name and look up in repository
        if (!ChannelName.TryCreate(channelName, out var parsedName, out _))
        {
            _logger.LogWarning("Invalid channel name: {ChannelName}", channelName);
            return;
        }

        var channel = _channelRepository.GetByName(parsedName);
        if (channel is null)
        {
            _logger.LogDebug("Channel not found: {ChannelName}", channelName);
            return;
        }

        var data = Encoding.UTF8.GetBytes(message + "\r\n");
        var tasks = new List<Task>();

        foreach (var member in channel.Members.Values)
        {
            if (exceptConnectionId.HasValue && member.ConnectionId == exceptConnectionId.Value)
            {
                continue;
            }

            if (_connectionManager.GetConnection(member.ConnectionId) is ClientConnection clientConnection)
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
            if (!ChannelName.TryCreate(channelName, out var parsedName, out _))
            {
                continue;
            }

            var channel = _channelRepository.GetByName(parsedName);
            if (channel is null)
            {
                continue;
            }

            foreach (var member in channel.Members.Values)
            {
                if (exceptConnectionId.HasValue && member.ConnectionId == exceptConnectionId.Value)
                {
                    continue;
                }

                // Deduplicate
                if (!sentTo.Add(member.ConnectionId))
                {
                    continue;
                }

                if (_connectionManager.GetConnection(member.ConnectionId) is ClientConnection clientConnection)
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
        var data = Encoding.UTF8.GetBytes(message + "\r\n");
        var tasks = new List<Task>();

        // Get all operators from user repository
        var operators = _userRepository.GetAll().Where(u => u.IsOperator);
        
        foreach (var oper in operators)
        {
            if (_connectionManager.GetConnection(oper.ConnectionId) is ClientConnection clientConnection)
            {
                tasks.Add(SendSafeAsync(clientConnection, data, cancellationToken));
            }
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Sends a message to a linked server (server-to-server communication).
    /// </summary>
    /// <param name="serverId">The target server identifier (SID or server name).</param>
    /// <param name="message">The IRC message to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask SendToServerAsync(string serverId, string message, CancellationToken cancellationToken = default)
    {
        if (_s2sConnectionManager == null)
        {
            _logger.LogDebug("S2S not available, cannot route message to server {ServerId}", serverId);
            return;
        }

        // Find connection by SID or server name by iterating all connections
        IS2SConnection? connection = null;
        foreach (var conn in _s2sConnectionManager.GetAllConnections())
        {
            if (conn.RemoteServerId?.Sid.Equals(serverId, StringComparison.OrdinalIgnoreCase) == true ||
                conn.RemoteServerId?.Name.Equals(serverId, StringComparison.OrdinalIgnoreCase) == true)
            {
                connection = conn;
                break;
            }
        }

        if (connection == null)
        {
            _logger.LogWarning("No S2S connection found for server {ServerId}", serverId);
            return;
        }

        try
        {
            await _s2sConnectionManager.SendToConnectionAsync(connection.ConnectionId, message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send S2S message to {ServerId}", serverId);
        }
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
