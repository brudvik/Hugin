using System.Collections.Concurrent;
using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.S2S;

/// <summary>
/// Manages server-to-server links.
/// </summary>
public sealed class ServerLinkManager : IServerLinkManager
{
    private readonly ConcurrentDictionary<string, LinkedServer> _serversBySid = new();
    private readonly ConcurrentDictionary<string, LinkedServer> _serversByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Guid> _directConnections = new();
    private readonly IMessageBroker _broker;
    private readonly ServerId _localServerId;

    /// <summary>
    /// Creates a new server link manager.
    /// </summary>
    /// <param name="broker">The message broker for sending S2S messages.</param>
    /// <param name="localServerId">The local server's ID.</param>
    public ServerLinkManager(IMessageBroker broker, ServerId localServerId)
    {
        _broker = broker;
        _localServerId = localServerId;
    }

    /// <inheritdoc />
    public IEnumerable<LinkedServer> DirectLinks =>
        _directConnections.Keys
            .Select(sid => _serversBySid.TryGetValue(sid, out var server) ? server : null)
            .Where(s => s != null)
            .Cast<LinkedServer>();

    /// <inheritdoc />
    public IEnumerable<LinkedServer> AllServers => _serversBySid.Values;

    /// <inheritdoc />
    public event EventHandler<ServerLinkedEventArgs>? ServerLinked;

    /// <inheritdoc />
    public event EventHandler<ServerUnlinkedEventArgs>? ServerUnlinked;

    /// <inheritdoc />
    public LinkedServer? GetBySid(string sid) =>
        _serversBySid.TryGetValue(sid, out var server) ? server : null;

    /// <inheritdoc />
    public LinkedServer? GetByName(string name) =>
        _serversByName.TryGetValue(name, out var server) ? server : null;

    /// <inheritdoc />
    public void AddDirectLink(LinkedServer server, Guid connectionId)
    {
        _serversBySid[server.Id.Sid] = server;
        _serversByName[server.Id.Name] = server;
        _directConnections[server.Id.Sid] = connectionId;

        ServerLinked?.Invoke(this, new ServerLinkedEventArgs(server, isDirect: true));
    }

    /// <inheritdoc />
    public void AddRemoteServer(LinkedServer server)
    {
        _serversBySid[server.Id.Sid] = server;
        _serversByName[server.Id.Name] = server;

        ServerLinked?.Invoke(this, new ServerLinkedEventArgs(server, isDirect: false));
    }

    /// <inheritdoc />
    public IEnumerable<LinkedServer> RemoveServer(ServerId serverId)
    {
        var removed = new List<LinkedServer>();

        if (_serversBySid.TryRemove(serverId.Sid, out var server))
        {
            _serversByName.TryRemove(serverId.Name, out _);
            _directConnections.TryRemove(serverId.Sid, out _);
            removed.Add(server);

            // Remove all servers that were learned through this server
            var cascadeRemoved = _serversBySid.Values
                .Where(s => s.LearnedFrom?.Sid == serverId.Sid)
                .ToList();

            foreach (var cascadeServer in cascadeRemoved)
            {
                _serversBySid.TryRemove(cascadeServer.Id.Sid, out _);
                _serversByName.TryRemove(cascadeServer.Id.Name, out _);
                removed.Add(cascadeServer);
            }

            ServerUnlinked?.Invoke(this, new ServerUnlinkedEventArgs(
                server, "Connection lost", cascadeRemoved));
        }

        return removed;
    }

    /// <inheritdoc />
    public Guid? GetConnectionId(ServerId serverId) =>
        _directConnections.TryGetValue(serverId.Sid, out var connId) ? connId : null;

    /// <inheritdoc />
    public ServerId? GetRouteTo(ServerId targetServerId)
    {
        // If directly connected, return the target itself
        if (_directConnections.ContainsKey(targetServerId.Sid))
        {
            return targetServerId;
        }

        // Find the server that told us about the target
        if (_serversBySid.TryGetValue(targetServerId.Sid, out var server) && server.LearnedFrom != null)
        {
            // Recursively find the route
            return GetRouteTo(server.LearnedFrom);
        }

        return null;
    }

    /// <inheritdoc />
    public async ValueTask SendToServerAsync(
        string targetSid,
        S2SMessage message,
        CancellationToken cancellationToken = default)
    {
        var server = GetBySid(targetSid);
        if (server != null)
        {
            await SendToServerAsync(server.Id, message, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async ValueTask SendToServerAsync(
        ServerId targetServerId,
        S2SMessage message,
        CancellationToken cancellationToken = default)
    {
        var route = GetRouteTo(targetServerId);
        if (route == null)
        {
            return; // No route to server
        }

        var connectionId = GetConnectionId(route);
        if (connectionId.HasValue)
        {
            await _broker.SendToConnectionAsync(connectionId.Value, message.ToString(), cancellationToken);
        }
    }

    /// <inheritdoc />
    public async ValueTask BroadcastAsync(
        S2SMessage message,
        ServerId? exceptServerId = null,
        CancellationToken cancellationToken = default)
    {
        var messageStr = message.ToString();

        foreach (var (sid, connectionId) in _directConnections)
        {
            if (exceptServerId != null && sid == exceptServerId.Sid)
            {
                continue;
            }

            await _broker.SendToConnectionAsync(connectionId, messageStr, cancellationToken);
        }
    }
}
