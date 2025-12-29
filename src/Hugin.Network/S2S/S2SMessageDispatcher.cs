using Hugin.Core.Entities;
using Hugin.Core.ValueObjects;
using Hugin.Protocol.S2S;
using Microsoft.Extensions.Logging;

namespace Hugin.Network.S2S;

/// <summary>
/// Dispatches incoming S2S messages to the appropriate handlers.
/// </summary>
public sealed class S2SMessageDispatcher
{
    private readonly IS2SConnectionManager _connectionManager;
    private readonly IServerLinkManager _linkManager;
    private readonly IS2SHandshakeManager _handshakeManager;
    private readonly Dictionary<string, IS2SCommandHandler> _handlers;
    private readonly ServerId _localServerId;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<S2SMessageDispatcher> _logger;

    /// <summary>
    /// Creates a new S2S message dispatcher.
    /// </summary>
    public S2SMessageDispatcher(
        IS2SConnectionManager connectionManager,
        IServerLinkManager linkManager,
        IS2SHandshakeManager handshakeManager,
        IEnumerable<IS2SCommandHandler> handlers,
        ServerId localServerId,
        IServiceProvider serviceProvider,
        ILogger<S2SMessageDispatcher> logger)
    {
        _connectionManager = connectionManager;
        _linkManager = linkManager;
        _handshakeManager = handshakeManager;
        _handlers = handlers.ToDictionary(h => h.Command, StringComparer.OrdinalIgnoreCase);
        _localServerId = localServerId;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Registers a connection's line received event with the dispatcher.
    /// </summary>
    public void RegisterConnection(IS2SConnection connection)
    {
        connection.LineReceived += OnLineReceivedAsync;
    }

    /// <summary>
    /// Unregisters a connection from the dispatcher.
    /// </summary>
    public void UnregisterConnection(IS2SConnection connection)
    {
        connection.LineReceived -= OnLineReceivedAsync;
    }

    private async ValueTask OnLineReceivedAsync(IS2SConnection connection, string line)
    {
        _logger.LogDebug("S2S [{ConnectionId}] << {Line}", connection.ConnectionId, line);

        if (!S2SMessage.TryParse(line, out var message) || message is null)
        {
            _logger.LogWarning("S2S [{ConnectionId}] Failed to parse: {Line}", connection.ConnectionId, line);
            return;
        }

        // Handle handshake messages first
        if (!IsHandshakeComplete(connection))
        {
            await HandleHandshakeMessageAsync(connection, message);
            return;
        }

        // Get the source server
        var sourceServer = GetSourceServer(connection, message);
        if (sourceServer is null)
        {
            _logger.LogWarning("S2S [{ConnectionId}] Unknown source server for message: {Message}",
                connection.ConnectionId, message.Command);
            return;
        }

        // Dispatch to handler
        if (_handlers.TryGetValue(message.Command, out var handler))
        {
            if (message.Parameters.Count < handler.MinimumParameters)
            {
                _logger.LogWarning("S2S [{ConnectionId}] Not enough parameters for {Command}",
                    connection.ConnectionId, message.Command);
                return;
            }

            var context = new S2SContext(message, sourceServer, _linkManager, _localServerId, _serviceProvider);

            try
            {
                await handler.HandleAsync(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "S2S [{ConnectionId}] Error handling {Command}",
                    connection.ConnectionId, message.Command);
            }
        }
        else
        {
            _logger.LogDebug("S2S [{ConnectionId}] Unhandled command: {Command}",
                connection.ConnectionId, message.Command);
        }
    }

    private static bool IsHandshakeComplete(IS2SConnection connection)
    {
        return connection.RemoteServerId is not null;
    }

    private async ValueTask HandleHandshakeMessageAsync(IS2SConnection connection, S2SMessage message)
    {
        var result = await _handshakeManager.ProcessHandshakeMessageAsync(connection.ConnectionId, message);

        switch (result)
        {
            case HandshakeResult.Complete:
                // Handshake complete - the connection now has a RemoteServerId
                _logger.LogInformation("S2S handshake complete with {ServerName} ({Sid})",
                    connection.RemoteServerId?.Name, connection.RemoteServerId?.Sid);
                break;

            case HandshakeResult.Failed:
                _logger.LogWarning("S2S handshake failed for connection {ConnectionId}", connection.ConnectionId);
                await _connectionManager.CloseConnectionAsync(connection.ConnectionId, "Handshake failed");
                break;

            case HandshakeResult.InProgress:
                // Handshake in progress, waiting for more messages
                break;
        }
    }

    private LinkedServer? GetSourceServer(IS2SConnection connection, S2SMessage message)
    {
        // If message has a source, try to find that server
        if (message.Source is not null)
        {
            // Source can be a SID (3 chars) or UID (9 chars)
            var sid = message.Source.Length >= 3 ? message.Source[..3] : message.Source;
            var server = _linkManager.GetBySid(sid);
            if (server is not null)
            {
                return server;
            }
        }

        // Fall back to the directly connected server
        if (connection.RemoteServerId is not null)
        {
            return _linkManager.GetBySid(connection.RemoteServerId.Sid);
        }

        return null;
    }
}
