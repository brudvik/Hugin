using Hugin.Core.Entities;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.S2S;

/// <summary>
/// Manages server-to-server links.
/// </summary>
public interface IServerLinkManager
{
    /// <summary>
    /// Gets all directly connected servers.
    /// </summary>
    IEnumerable<LinkedServer> DirectLinks { get; }

    /// <summary>
    /// Gets all known servers (including remote servers learned via other links).
    /// </summary>
    IEnumerable<LinkedServer> AllServers { get; }

    /// <summary>
    /// Gets a server by its SID.
    /// </summary>
    LinkedServer? GetBySid(string sid);

    /// <summary>
    /// Gets a server by its name.
    /// </summary>
    LinkedServer? GetByName(string name);

    /// <summary>
    /// Adds a directly connected server.
    /// </summary>
    void AddDirectLink(LinkedServer server, Guid connectionId);

    /// <summary>
    /// Adds a remote server learned from another server.
    /// </summary>
    void AddRemoteServer(LinkedServer server);

    /// <summary>
    /// Removes a server and all servers learned through it.
    /// </summary>
    IEnumerable<LinkedServer> RemoveServer(ServerId serverId);

    /// <summary>
    /// Gets the connection ID for a directly connected server.
    /// </summary>
    Guid? GetConnectionId(ServerId serverId);

    /// <summary>
    /// Gets the route to a server (the direct link to use).
    /// </summary>
    ServerId? GetRouteTo(ServerId targetServerId);

    /// <summary>
    /// Sends a message to a specific server by SID.
    /// </summary>
    /// <param name="targetSid">The target server's SID (3 characters).</param>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SendToServerAsync(string targetSid, S2SMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to a specific server.
    /// </summary>
    ValueTask SendToServerAsync(ServerId targetServerId, S2SMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts a message to all directly connected servers.
    /// </summary>
    /// <param name="message">The message to broadcast.</param>
    /// <param name="exceptServerId">Server to exclude from broadcast (usually the source).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask BroadcastAsync(S2SMessage message, ServerId? exceptServerId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a server is linked.
    /// </summary>
    event EventHandler<ServerLinkedEventArgs>? ServerLinked;

    /// <summary>
    /// Event raised when a server is unlinked.
    /// </summary>
    event EventHandler<ServerUnlinkedEventArgs>? ServerUnlinked;
}

/// <summary>
/// Event args for server linked event.
/// </summary>
public sealed class ServerLinkedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the linked server.
    /// </summary>
    public LinkedServer Server { get; }

    /// <summary>
    /// Gets whether this is a direct connection.
    /// </summary>
    public bool IsDirect { get; }

    /// <summary>
    /// Creates new server linked event args.
    /// </summary>
    public ServerLinkedEventArgs(LinkedServer server, bool isDirect)
    {
        Server = server;
        IsDirect = isDirect;
    }
}

/// <summary>
/// Event args for server unlinked event.
/// </summary>
public sealed class ServerUnlinkedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the unlinked server.
    /// </summary>
    public LinkedServer Server { get; }

    /// <summary>
    /// Gets the reason for unlinking.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets servers that were also unlinked as a result (learned via this server).
    /// </summary>
    public IReadOnlyList<LinkedServer> CascadeUnlinked { get; }

    /// <summary>
    /// Creates new server unlinked event args.
    /// </summary>
    public ServerUnlinkedEventArgs(LinkedServer server, string reason, IReadOnlyList<LinkedServer> cascadeUnlinked)
    {
        Server = server;
        Reason = reason;
        CascadeUnlinked = cascadeUnlinked;
    }
}
