using System.Collections.Concurrent;
using Hugin.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Hugin.Protocol.S2S;

/// <summary>
/// Implementation of the S2S handshake manager.
/// </summary>
public sealed class S2SHandshakeManager : IS2SHandshakeManager
{
    private readonly ConcurrentDictionary<Guid, HandshakeState> _states = new();
    private readonly ServerId _localServerId;
    private readonly string _serverDescription;
    private readonly Func<Guid, S2SMessage, CancellationToken, ValueTask> _sendMessageAsync;
    private readonly ILogger<S2SHandshakeManager> _logger;

    /// <summary>
    /// Creates a new S2S handshake manager.
    /// </summary>
    /// <param name="localServerId">The local server ID.</param>
    /// <param name="serverDescription">The server description.</param>
    /// <param name="sendMessageAsync">Function to send messages to a connection.</param>
    /// <param name="logger">The logger.</param>
    public S2SHandshakeManager(
        ServerId localServerId,
        string serverDescription,
        Func<Guid, S2SMessage, CancellationToken, ValueTask> sendMessageAsync,
        ILogger<S2SHandshakeManager> logger)
    {
        _localServerId = localServerId;
        _serverDescription = serverDescription;
        _sendMessageAsync = sendMessageAsync;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask InitiateHandshakeAsync(Guid connectionId, string password, CancellationToken cancellationToken = default)
    {
        var state = new HandshakeState(connectionId, isOutgoing: true)
        {
            ExpectedPassword = password
        };

        if (!_states.TryAdd(connectionId, state))
        {
            _logger.LogWarning("Handshake state already exists for connection {ConnectionId}", connectionId);
            return;
        }

        _logger.LogInformation("Initiating S2S handshake on connection {ConnectionId}", connectionId);

        // Send PASS
        var passMsg = S2SMessage.Create("PASS", password, "TS", "6", _localServerId.Sid);
        await _sendMessageAsync(connectionId, passMsg, cancellationToken);
        state.PassSent = true;

        // Send CAPAB
        var capabilities = string.Join(" ", S2SCapabilities.Defaults);
        var capabMsg = S2SMessage.Create("CAPAB", capabilities);
        await _sendMessageAsync(connectionId, capabMsg, cancellationToken);
        state.CapabSent = true;

        // Send SERVER
        var serverMsg = S2SMessage.Create(
            "SERVER",
            _localServerId.Name,
            "1",           // Hop count (we are 1 hop away from ourselves)
            _serverDescription);
        await _sendMessageAsync(connectionId, serverMsg, cancellationToken);
        state.ServerSent = true;
    }

    /// <inheritdoc />
    public async ValueTask<HandshakeResult> ProcessHandshakeMessageAsync(
        Guid connectionId, 
        S2SMessage message, 
        CancellationToken cancellationToken = default)
    {
        // Get or create state for incoming connections
        var state = _states.GetOrAdd(connectionId, id => new HandshakeState(id, isOutgoing: false));

        var command = message.Command.ToUpperInvariant();

        switch (command)
        {
            case "PASS":
                return await HandlePassAsync(state, message, cancellationToken);

            case "CAPAB":
                return HandleCapab(state, message);

            case "SERVER":
                return await HandleServerAsync(state, message, cancellationToken);

            case "ERROR":
                var errorMsg = message.Parameters.Count > 0 ? message.Parameters[0] : "Unknown error";
                _logger.LogError("Received ERROR during handshake: {Error}", errorMsg);
                state.ErrorMessage = errorMsg;
                return HandshakeResult.Failed;

            default:
                _logger.LogWarning("Unexpected command during handshake: {Command}", command);
                return HandshakeResult.InProgress;
        }
    }

    /// <inheritdoc />
    public bool IsHandshaking(Guid connectionId)
    {
        return _states.ContainsKey(connectionId);
    }

    /// <inheritdoc />
    public HandshakeState? GetState(Guid connectionId)
    {
        return _states.TryGetValue(connectionId, out var state) ? state : null;
    }

    /// <inheritdoc />
    public void RemoveState(Guid connectionId)
    {
        _states.TryRemove(connectionId, out _);
    }

    private async ValueTask<HandshakeResult> HandlePassAsync(
        HandshakeState state, 
        S2SMessage message,
        CancellationToken cancellationToken)
    {
        if (message.Parameters.Count < 1)
        {
            state.ErrorMessage = "PASS requires at least 1 parameter";
            return HandshakeResult.Failed;
        }

        state.ReceivedPassword = message.Parameters[0];
        state.PassReceived = true;

        // If we have an expected password, verify it matches
        if (state.ExpectedPassword != null && state.ReceivedPassword != state.ExpectedPassword)
        {
            _logger.LogWarning("Password mismatch during handshake for {ConnectionId}", state.ConnectionId);
            state.ErrorMessage = "Password mismatch";

            // Send ERROR and close
            var errorMsg = S2SMessage.Create("ERROR", "Closing Link: Password mismatch");
            await _sendMessageAsync(state.ConnectionId, errorMsg, cancellationToken);

            return HandshakeResult.Failed;
        }

        // If this is an incoming connection and we haven't sent our messages yet, do so now
        if (!state.IsOutgoing && !state.PassSent)
        {
            // We need to respond, but we need the link password from configuration
            // This would typically be looked up from the linked server config
            _logger.LogDebug("Incoming connection, waiting for configuration to respond");
        }

        _logger.LogDebug("PASS received for connection {ConnectionId}", state.ConnectionId);
        return CheckComplete(state);
    }

    private HandshakeResult HandleCapab(HandshakeState state, S2SMessage message)
    {
        if (message.Parameters.Count > 0)
        {
            var capabilities = message.Parameters[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var cap in capabilities)
            {
                state.ReceivedCapabilities.Add(cap);
            }
        }

        state.CapabReceived = true;
        _logger.LogDebug("CAPAB received with {Count} capabilities for connection {ConnectionId}", 
            state.ReceivedCapabilities.Count, state.ConnectionId);

        return CheckComplete(state);
    }

    private async ValueTask<HandshakeResult> HandleServerAsync(
        HandshakeState state, 
        S2SMessage message,
        CancellationToken cancellationToken)
    {
        if (message.Parameters.Count < 2)
        {
            state.ErrorMessage = "SERVER requires at least 2 parameters";
            return HandshakeResult.Failed;
        }

        state.RemoteServerName = message.Parameters[0];
        
        // SERVER can have different formats:
        // SERVER name hopcount :description
        // SERVER name hopcount SID :description
        if (message.Parameters.Count >= 4)
        {
            state.RemoteSid = message.Parameters[2];
            state.RemoteDescription = message.Parameters[3];
        }
        else if (message.Parameters.Count >= 3)
        {
            state.RemoteDescription = message.Parameters[2];
        }
        else
        {
            state.RemoteDescription = message.Parameters[1];
        }

        state.ServerReceived = true;
        _logger.LogInformation("SERVER received: {ServerName} (SID: {Sid})", 
            state.RemoteServerName, state.RemoteSid ?? "unknown");

        return CheckComplete(state);
    }

    private HandshakeResult CheckComplete(HandshakeState state)
    {
        if (state.IsComplete)
        {
            _logger.LogInformation("Handshake complete for connection {ConnectionId} with server {ServerName}",
                state.ConnectionId, state.RemoteServerName);
            return HandshakeResult.Complete;
        }

        return HandshakeResult.InProgress;
    }
}
