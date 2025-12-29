using Hugin.Core.Entities;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.S2S;

/// <summary>
/// Interface for S2S command handlers.
/// </summary>
public interface IS2SCommandHandler
{
    /// <summary>
    /// Gets the command this handler processes.
    /// </summary>
    string Command { get; }

    /// <summary>
    /// Gets the minimum number of parameters required.
    /// </summary>
    int MinimumParameters { get; }

    /// <summary>
    /// Handles the S2S command.
    /// </summary>
    ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Context for S2S command execution.
/// </summary>
public sealed class S2SContext
{
    /// <summary>
    /// Gets the parsed S2S message.
    /// </summary>
    public S2SMessage Message { get; }

    /// <summary>
    /// Gets the linked server that sent this message.
    /// </summary>
    public LinkedServer SourceServer { get; }

    /// <summary>
    /// Gets the server link manager.
    /// </summary>
    public IServerLinkManager Links { get; }

    /// <summary>
    /// Gets the local server ID.
    /// </summary>
    public ServerId LocalServerId { get; }

    /// <summary>
    /// Gets the local server name.
    /// </summary>
    public string LocalServerName => LocalServerId.Name;

    /// <summary>
    /// Gets the service provider for dependency resolution.
    /// </summary>
    public IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Creates a new S2S context.
    /// </summary>
    public S2SContext(
        S2SMessage message,
        LinkedServer sourceServer,
        IServerLinkManager links,
        ServerId localServerId,
        IServiceProvider serviceProvider)
    {
        Message = message;
        SourceServer = sourceServer;
        Links = links;
        LocalServerId = localServerId;
        ServiceProvider = serviceProvider;
    }

    /// <summary>
    /// Sends a message to the source server.
    /// </summary>
    public ValueTask ReplyAsync(S2SMessage message, CancellationToken cancellationToken = default) =>
        Links.SendToServerAsync(SourceServer.Id, message, cancellationToken);

    /// <summary>
    /// Broadcasts a message to all linked servers except the source.
    /// </summary>
    public ValueTask BroadcastAsync(S2SMessage message, CancellationToken cancellationToken = default) =>
        Links.BroadcastAsync(message, SourceServer.Id, cancellationToken);

    /// <summary>
    /// Broadcasts a message to all linked servers.
    /// </summary>
    public ValueTask BroadcastToAllAsync(S2SMessage message, CancellationToken cancellationToken = default) =>
        Links.BroadcastAsync(message, null, cancellationToken);
}
