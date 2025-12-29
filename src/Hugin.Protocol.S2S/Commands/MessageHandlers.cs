namespace Hugin.Protocol.S2S.Commands;

/// <summary>
/// Handles PRIVMSG messages propagated between servers.
/// </summary>
/// <remarks>
/// Syntax: :uid PRIVMSG target :message
/// Target can be a UID (private message) or channel name.
/// </remarks>
public sealed class S2SPrivmsgHandler : S2SCommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "PRIVMSG";

    /// <inheritdoc />
    public override int MinimumParameters => 2;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default)
    {
        var sourceUid = context.Message.Source;
        if (string.IsNullOrEmpty(sourceUid))
        {
            return;
        }

        var target = context.Message.Parameters[0];
        var message = context.Message.Parameters[1];

        if (target.StartsWith('#') || target.StartsWith('&'))
        {
            // Channel message - propagate to all servers with users in the channel
            await context.BroadcastAsync(context.Message, cancellationToken);
        }
        else if (target.Length == 9)
        {
            // UID - route to the server owning this user
            var targetSid = target[..3];
            if (targetSid == context.LocalServerId.Sid)
            {
                // Local user - handle locally (by main server code)
            }
            else
            {
                // Remote user - route to their server
                await context.Links.SendToServerAsync(targetSid, context.Message, cancellationToken);
            }
        }
    }
}

/// <summary>
/// Handles NOTICE messages propagated between servers.
/// </summary>
/// <remarks>
/// Syntax: :uid NOTICE target :message
/// Target can be a UID (private notice) or channel name.
/// </remarks>
public sealed class S2SNoticeHandler : S2SCommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "NOTICE";

    /// <inheritdoc />
    public override int MinimumParameters => 2;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default)
    {
        var sourceUid = context.Message.Source;
        if (string.IsNullOrEmpty(sourceUid))
        {
            return;
        }

        var target = context.Message.Parameters[0];
        var message = context.Message.Parameters[1];

        if (target.StartsWith('#') || target.StartsWith('&'))
        {
            // Channel notice - propagate to all servers with users in the channel
            await context.BroadcastAsync(context.Message, cancellationToken);
        }
        else if (target.Length == 9)
        {
            // UID - route to the server owning this user
            var targetSid = target[..3];
            if (targetSid == context.LocalServerId.Sid)
            {
                // Local user - handle locally (by main server code)
            }
            else
            {
                // Remote user - route to their server
                await context.Links.SendToServerAsync(targetSid, context.Message, cancellationToken);
            }
        }
    }
}

/// <summary>
/// Handles ENCAP messages for extensible server communication.
/// </summary>
/// <remarks>
/// ENCAP allows servers to send arbitrary encapsulated commands.
/// Syntax: :source ENCAP target command [params...]
/// Target can be * (broadcast) or a SID.
/// </remarks>
public sealed class EncapHandler : S2SCommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "ENCAP";

    /// <inheritdoc />
    public override int MinimumParameters => 2;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default)
    {
        var target = context.Message.Parameters[0];
        var encapCommand = context.Message.Parameters[1];

        if (target == "*")
        {
            // Broadcast to all servers
            await context.BroadcastAsync(context.Message, cancellationToken);
        }
        else if (target == context.LocalServerId.Sid)
        {
            // Targeted at us - handle locally
            // Dispatch to appropriate ENCAP sub-handler
        }
        else
        {
            // Route to specific server
            await context.Links.SendToServerAsync(target, context.Message, cancellationToken);
        }
    }
}
