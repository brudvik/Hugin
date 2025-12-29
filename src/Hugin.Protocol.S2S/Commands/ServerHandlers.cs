using System.Globalization;
using Hugin.Core.Entities;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.S2S.Commands;

/// <summary>
/// Handles the SERVER/SID command for server introduction.
/// This is the first command sent after password exchange.
/// </summary>
/// <remarks>
/// Syntax: SERVER name hopcount sid :description
/// Example: SERVER irc2.example.com 1 002 :Second IRC server
/// </remarks>
public sealed class ServerHandler : S2SCommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "SERVER";

    /// <inheritdoc />
    public override int MinimumParameters => 4;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default)
    {
        var parameters = context.Message.Parameters;
        var serverName = parameters[0];
        
        if (!int.TryParse(parameters[1], out var hopCount))
        {
            // Invalid hop count
            return;
        }

        var sid = parameters[2];
        var description = parameters.Count > 3 ? parameters[3] : serverName;

        // Validate SID format
        if (sid.Length != 3)
        {
            // Invalid SID
            await SendErrorAsync(context, "Invalid SID format", cancellationToken);
            return;
        }

        // Check for SID collision
        if (context.Links.GetBySid(sid) != null)
        {
            await SendErrorAsync(context, $"SID {sid} already in use", cancellationToken);
            return;
        }

        // Check for server name collision
        if (context.Links.GetByName(serverName) != null)
        {
            await SendErrorAsync(context, $"Server {serverName} already linked", cancellationToken);
            return;
        }

        // Determine if this is a direct link or a remote server introduction
        var serverId = ServerId.Create(sid, serverName);
        var learnedFrom = context.Message.Source != null 
            ? context.Links.GetBySid(context.Message.Source)?.Id 
            : null;

        var linkedServer = new LinkedServer(
            serverId,
            description,
            "Unknown", // Version will be set by VERSION command
            hopCount,
            learnedFrom);

        if (learnedFrom == null)
        {
            // This is a direct link - we need the connection ID from the context
            // For now, store as remote until handshake completes
            context.Links.AddRemoteServer(linkedServer);
        }
        else
        {
            context.Links.AddRemoteServer(linkedServer);
        }

        // Broadcast the new server to other links (except the source)
        var broadcastMsg = S2SMessage.CreateWithSource(
            context.LocalServerId.Sid,
            "SERVER",
            serverName,
            (hopCount + 1).ToString(CultureInfo.InvariantCulture),
            sid,
            description);

        await context.BroadcastAsync(broadcastMsg, cancellationToken);
    }

    private static async ValueTask SendErrorAsync(S2SContext context, string message, CancellationToken cancellationToken)
    {
        var errorMsg = S2SMessage.CreateWithSource(
            context.LocalServerId.Sid,
            "ERROR",
            message);
        await context.ReplyAsync(errorMsg, cancellationToken);
    }
}

/// <summary>
/// Handles the SQUIT command for server disconnection.
/// </summary>
/// <remarks>
/// Syntax: SQUIT server :reason
/// Example: SQUIT irc2.example.com :Server shutting down
/// </remarks>
public sealed class SquitHandler : S2SCommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "SQUIT";

    /// <inheritdoc />
    public override int MinimumParameters => 1;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default)
    {
        var serverName = context.Message.Parameters[0];
        var reason = context.Message.Parameters.Count > 1 
            ? context.Message.Parameters[1] 
            : "No reason given";

        var server = context.Links.GetByName(serverName);
        if (server == null)
        {
            // Unknown server - ignore
            return;
        }

        // Remove the server and get all cascade-removed servers
        var removedServers = context.Links.RemoveServer(server.Id);

        // Broadcast SQUIT to other links
        var squitMsg = S2SMessage.CreateWithSource(
            context.Message.Source ?? context.LocalServerId.Sid,
            "SQUIT",
            serverName,
            reason);

        await context.BroadcastAsync(squitMsg, cancellationToken);
    }
}

/// <summary>
/// Handles the PING command between servers.
/// </summary>
public sealed class S2SPingHandler : S2SCommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "PING";

    /// <inheritdoc />
    public override int MinimumParameters => 1;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default)
    {
        var source = context.Message.Parameters[0];
        var target = context.Message.Parameters.Count > 1 
            ? context.Message.Parameters[1] 
            : context.LocalServerId.Name;

        // If targeted at us, respond with PONG
        if (target.Equals(context.LocalServerId.Name, StringComparison.OrdinalIgnoreCase) ||
            target.Equals(context.LocalServerId.Sid, StringComparison.OrdinalIgnoreCase))
        {
            var pong = S2SMessage.CreateWithSource(
                context.LocalServerId.Sid,
                "PONG",
                context.LocalServerId.Name,
                source);

            await context.ReplyAsync(pong, cancellationToken);
        }
        else
        {
            // Forward to target server
            var targetServer = context.Links.GetByName(target) ?? context.Links.GetBySid(target);
            if (targetServer != null)
            {
                await context.Links.SendToServerAsync(targetServer.Id, context.Message, cancellationToken);
            }
        }
    }
}

/// <summary>
/// Handles the PONG command between servers.
/// </summary>
public sealed class S2SPongHandler : S2SCommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "PONG";

    /// <inheritdoc />
    public override int MinimumParameters => 1;

    /// <inheritdoc />
    public override ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default)
    {
        // PONG received - could update last ping time for the server
        // For now, we just acknowledge it
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Handles the ERROR command from a linked server.
/// </summary>
public sealed class ErrorHandler : S2SCommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "ERROR";

    /// <inheritdoc />
    public override int MinimumParameters => 1;

    /// <inheritdoc />
    public override ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default)
    {
        var errorMessage = context.Message.Parameters[0];
        
        // Log the error and potentially disconnect
        // The connection layer should handle this
        
        return ValueTask.CompletedTask;
    }
}
