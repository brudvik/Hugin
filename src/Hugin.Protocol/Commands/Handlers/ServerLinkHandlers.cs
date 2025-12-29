using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the CONNECT command.
/// Instructs the server to connect to another server.
/// This is an operator-only command.
/// </summary>
/// <remarks>
/// RFC 2812: CONNECT target [ port [ remote ] ]
/// </remarks>
public sealed class ConnectHandler : CommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "CONNECT";

    /// <inheritdoc />
    public override int MinimumParameters => 1;

    /// <inheritdoc />
    public override bool RequiresRegistration => true;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var user = context.User;

        // CONNECT is operator-only
        if (!user.Modes.HasFlag(UserMode.Operator))
        {
            await context.ReplyAsync(
                IrcNumerics.NoPrivileges(context.ServerName, user.Nickname!.Value),
                cancellationToken);
            return;
        }

        var targetServer = context.Message.Parameters[0];
        var port = 6667; // Default IRC port

        if (context.Message.Parameters.Count > 1)
        {
            if (!int.TryParse(context.Message.Parameters[1], out port) || port <= 0 || port > 65535)
            {
                port = 6667;
            }
        }

        // Check if target is a configured linked server
        // In a full implementation, this would:
        // 1. Look up the target in configured linked servers
        // 2. Initiate an S2S connection
        // 3. Begin the handshake process

        // For now, we send an informational notice
        var notice = IrcMessage.CreateWithSource(
            context.ServerName,
            "NOTICE",
            user.Nickname!.Value,
            $"Attempting to connect to {targetServer} on port {port}...");

        await context.ReplyAsync(notice, cancellationToken);

        // If a remote server was specified, forward the command
        if (context.Message.Parameters.Count > 2)
        {
            var remoteServer = context.Message.Parameters[2];
            // In a full implementation, this would route the CONNECT to the remote server
            var remoteNotice = IrcMessage.CreateWithSource(
                context.ServerName,
                "NOTICE",
                user.Nickname!.Value,
                $"CONNECT request forwarded to {remoteServer}");
            await context.ReplyAsync(remoteNotice, cancellationToken);
        }
    }
}

/// <summary>
/// Handles the LINKS command.
/// Lists all servers linked to the network.
/// </summary>
/// <remarks>
/// RFC 2812: LINKS [ [ remote ] mask ]
/// </remarks>
public sealed class LinksHandler : CommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "LINKS";

    /// <inheritdoc />
    public override int MinimumParameters => 0;

    /// <inheritdoc />
    public override bool RequiresRegistration => true;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var user = context.User;
        var nick = user.Nickname!.Value;

        string mask = "*";
        string? remoteServer = null;

        // Parse parameters: LINKS [remote] [mask]
        if (context.Message.Parameters.Count == 1)
        {
            // Could be either remote or mask
            // If it contains wildcards, it's a mask
            var param = context.Message.Parameters[0];
            if (param.Contains('*') || param.Contains('?'))
            {
                mask = param;
            }
            else
            {
                remoteServer = param;
            }
        }
        else if (context.Message.Parameters.Count >= 2)
        {
            remoteServer = context.Message.Parameters[0];
            mask = context.Message.Parameters[1];
        }

        // If a remote server was specified, forward the request
        if (remoteServer != null && !remoteServer.Equals(context.ServerName, StringComparison.OrdinalIgnoreCase))
        {
            // In a full implementation, this would route to the remote server
            await context.ReplyAsync(
                IrcNumerics.NoSuchServer(context.ServerName, nick, remoteServer),
                cancellationToken);
            return;
        }

        // Always include this server first
        await context.ReplyAsync(
            IrcNumerics.Links(context.ServerName, nick, mask, context.ServerName, 0, "Hugin IRC Server"),
            cancellationToken);

        // In a full implementation, this would iterate through all linked servers
        // from the IServerLinkManager and include them in the response

        // End of LINKS
        await context.ReplyAsync(
            IrcNumerics.EndOfLinks(context.ServerName, nick, mask),
            cancellationToken);
    }
}

/// <summary>
/// Handles the TRACE command.
/// Traces the route to a target or lists all connections.
/// </summary>
/// <remarks>
/// RFC 2812: TRACE [target]
/// If no target is given, traces all connections to this server.
/// If a target is given, traces the route to that target.
/// </remarks>
public sealed class TraceHandler : CommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "TRACE";

    /// <inheritdoc />
    public override int MinimumParameters => 0;

    /// <inheritdoc />
    public override bool RequiresRegistration => true;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var user = context.User;
        var nick = user.Nickname!.Value;

        string? target = context.Message.Parameters.Count > 0 
            ? context.Message.Parameters[0] 
            : null;

        // If target is a server and not us, forward the request
        if (target != null && 
            !target.Equals(context.ServerName, StringComparison.OrdinalIgnoreCase) &&
            target.Contains('.'))
        {
            // In a full implementation, this would route to the target server
            // For now, report that we can't reach it if we have no links
            await context.ReplyAsync(
                IrcNumerics.NoSuchServer(context.ServerName, nick, target),
                cancellationToken);
            return;
        }

        // If target is a nickname, trace to that user
        if (target != null && Nickname.TryCreate(target, out var targetNick, out _) && targetNick != null)
        {
            var targetUser = context.Users.GetByNickname(targetNick);
            if (targetUser != null)
            {
                // Trace to this user
                if (targetUser.Modes.HasFlag(UserMode.Operator))
                {
                    await context.ReplyAsync(
                        IrcNumerics.TraceOperator(context.ServerName, nick, "opers", targetUser.Nickname!.Value),
                        cancellationToken);
                }
                else
                {
                    await context.ReplyAsync(
                        IrcNumerics.TraceUser(context.ServerName, nick, "users", targetUser.Nickname!.Value),
                        cancellationToken);
                }
            }
            else
            {
                await context.ReplyAsync(
                    IrcNumerics.NoSuchNick(context.ServerName, nick, target),
                    cancellationToken);
                return;
            }
        }

        // If we're an operator and no specific target, list all connections
        if (target == null && user.Modes.HasFlag(UserMode.Operator))
        {
            // List all users on this server
            var allUsers = context.Users.GetAll();
            foreach (var u in allUsers.Where(u => u.Nickname != null))
            {
                if (u.Modes.HasFlag(UserMode.Operator))
                {
                    await context.ReplyAsync(
                        IrcNumerics.TraceOperator(context.ServerName, nick, "opers", u.Nickname!.Value),
                        cancellationToken);
                }
                else
                {
                    await context.ReplyAsync(
                        IrcNumerics.TraceUser(context.ServerName, nick, "users", u.Nickname!.Value),
                        cancellationToken);
                }
            }
        }

        // End of TRACE
        await context.ReplyAsync(
            IrcNumerics.TraceEnd(context.ServerName, nick, target ?? context.ServerName, "Hugin-1.0"),
            cancellationToken);
    }
}

/// <summary>
/// Handles the client-side SQUIT command.
/// Disconnects a server from the network. Operator only.
/// </summary>
/// <remarks>
/// RFC 2812: SQUIT server :comment
/// Unlike the S2S SQUIT command, this is sent by operators to request
/// a server disconnect.
/// </remarks>
public sealed class ClientSquitHandler : CommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "SQUIT";

    /// <inheritdoc />
    public override int MinimumParameters => 1;

    /// <inheritdoc />
    public override bool RequiresRegistration => true;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var user = context.User;
        var nick = user.Nickname!.Value;

        // SQUIT requires operator privileges
        if (!user.Modes.HasFlag(UserMode.Operator))
        {
            await context.ReplyAsync(
                IrcNumerics.NoPrivileges(context.ServerName, nick),
                cancellationToken);
            return;
        }

        var targetServer = context.Message.Parameters[0];
        var reason = context.Message.Parameters.Count > 1
            ? context.Message.Parameters[1]
            : $"SQUIT by {nick}";

        // Check if target is this server
        if (targetServer.Equals(context.ServerName, StringComparison.OrdinalIgnoreCase))
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    ":Cannot SQUIT self"),
                cancellationToken);
            return;
        }

        // Try to get the server link manager via the service resolver
        // This uses the IServerLinkInfo interface which should be in Core
        var linkInfo = context.ServiceProvider(typeof(Core.Interfaces.IServerLinkInfo))
            as Core.Interfaces.IServerLinkInfo;

        if (linkInfo == null)
        {
            await context.ReplyAsync(
                IrcNumerics.NoSuchServer(context.ServerName, nick, targetServer),
                cancellationToken);
            return;
        }

        if (!linkInfo.IsServerLinked(targetServer))
        {
            await context.ReplyAsync(
                IrcNumerics.NoSuchServer(context.ServerName, nick, targetServer),
                cancellationToken);
            return;
        }

        // Request disconnect via callback
        var disconnected = await linkInfo.DisconnectServerAsync(targetServer, reason, cancellationToken);

        if (disconnected)
        {
            // Send notice to operator
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $":Disconnecting server {targetServer}: {reason}"),
                cancellationToken);

            // Broadcast WALLOPS to all operators
            var wallopsMsg = IrcMessage.CreateWithSource(context.ServerName, "WALLOPS",
                $":{nick} issued SQUIT for {targetServer} ({reason})");
            
            foreach (var oper in context.Users.GetAll().Where(u => u.Modes.HasFlag(UserMode.Operator)))
            {
                await context.Broker.SendToConnectionAsync(oper.ConnectionId, wallopsMsg.ToString(), cancellationToken);
            }
        }
        else
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    $":Failed to disconnect server {targetServer}"),
                cancellationToken);
        }
    }
}
