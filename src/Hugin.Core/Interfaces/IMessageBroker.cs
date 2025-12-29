namespace Hugin.Core.Interfaces;

/// <summary>
/// Message broker for distributing messages to connections.
/// </summary>
public interface IMessageBroker
{
    /// <summary>
    /// Sends a message to a specific connection.
    /// </summary>
    ValueTask SendToConnectionAsync(Guid connectionId, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to multiple connections.
    /// </summary>
    ValueTask SendToConnectionsAsync(IEnumerable<Guid> connectionIds, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to all connections in a channel.
    /// </summary>
    ValueTask SendToChannelAsync(string channelName, string message, Guid? exceptConnectionId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to all connections in multiple channels (deduplicating).
    /// </summary>
    ValueTask SendToChannelsAsync(IEnumerable<string> channelNames, string message, Guid? exceptConnectionId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to all connections.
    /// </summary>
    ValueTask BroadcastAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to all operators.
    /// </summary>
    ValueTask SendToOperatorsAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to all connections on a specific server.
    /// </summary>
    ValueTask SendToServerAsync(string serverId, string message, CancellationToken cancellationToken = default);
}
