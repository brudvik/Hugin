using Hugin.Core.Entities;
using Hugin.Core.Interfaces;

namespace Hugin.Protocol.S2S.Commands;

/// <summary>
/// Handles ENCAP sub-commands for various S2S extensions.
/// </summary>
public sealed class EncapSubHandlers
{
    private readonly Dictionary<string, Func<S2SContext, string[], CancellationToken, ValueTask>> _handlers;

    /// <summary>
    /// Creates a new ENCAP sub-handler registry.
    /// </summary>
    public EncapSubHandlers()
    {
        _handlers = new Dictionary<string, Func<S2SContext, string[], CancellationToken, ValueTask>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["AKILL"] = HandleAkillAsync,
            ["UNAKILL"] = HandleUnakillAsync,
            ["LOGIN"] = HandleLoginAsync,
            ["LOGOUT"] = HandleLogoutAsync,
            ["CERTFP"] = HandleCertfpAsync,
            ["SASL"] = HandleSaslAsync,
            ["KLINE"] = HandleKlineAsync,
            ["UNKLINE"] = HandleUnklineAsync,
            ["XLINE"] = HandleXlineAsync,
            ["UNXLINE"] = HandleUnxlineAsync,
            ["RESV"] = HandleResvAsync,
            ["UNRESV"] = HandleUnresvAsync
        };
    }

    /// <summary>
    /// Handles an ENCAP sub-command.
    /// </summary>
    /// <param name="context">The S2S context.</param>
    /// <param name="encapCommand">The ENCAP command name.</param>
    /// <param name="parameters">The command parameters (after the command name).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the command was handled; otherwise false.</returns>
    public async ValueTask<bool> HandleAsync(
        S2SContext context,
        string encapCommand,
        string[] parameters,
        CancellationToken cancellationToken)
    {
        if (_handlers.TryGetValue(encapCommand, out var handler))
        {
            await handler(context, parameters, cancellationToken);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Handles AKILL - network-wide autokill.
    /// </summary>
    /// <remarks>
    /// Syntax: ENCAP * AKILL user@host duration setter :reason
    /// </remarks>
    private static async ValueTask HandleAkillAsync(
        S2SContext context,
        string[] parameters,
        CancellationToken cancellationToken)
    {
        if (parameters.Length < 4)
        {
            return;
        }

        var mask = parameters[0];
        var duration = parameters[1];
        var setter = parameters[2];
        var reason = parameters.Length > 3 ? parameters[3] : "No reason";

        // Parse duration and create ban
        TimeSpan? banDuration = ParseDuration(duration);
        var expiresAt = banDuration.HasValue
            ? DateTimeOffset.UtcNow.Add(banDuration.Value)
            : (DateTimeOffset?)null;

        var ban = new ServerBan(
            BanType.GLine,
            mask,
            reason,
            setter,
            DateTimeOffset.UtcNow,
            expiresAt);

        var banRepo = context.ServiceProvider.GetService(typeof(IServerBanRepository)) as IServerBanRepository;
        banRepo?.Add(ban);

        // Propagate to other servers
        await context.BroadcastAsync(context.Message, cancellationToken);
    }

    /// <summary>
    /// Handles UNAKILL - removes a network-wide autokill.
    /// </summary>
    private static async ValueTask HandleUnakillAsync(
        S2SContext context,
        string[] parameters,
        CancellationToken cancellationToken)
    {
        if (parameters.Length < 1)
        {
            return;
        }

        var mask = parameters[0];

        var banRepo = context.ServiceProvider.GetService(typeof(IServerBanRepository)) as IServerBanRepository;
        banRepo?.Remove(BanType.GLine, mask);

        // Propagate to other servers
        await context.BroadcastAsync(context.Message, cancellationToken);
    }

    /// <summary>
    /// Handles LOGIN - sets user account on the network.
    /// </summary>
    /// <remarks>
    /// Syntax: ENCAP * LOGIN uid accountname
    /// Used after SASL authentication to propagate account info.
    /// </remarks>
    private static async ValueTask HandleLoginAsync(
        S2SContext context,
        string[] parameters,
        CancellationToken cancellationToken)
    {
        if (parameters.Length < 2)
        {
            return;
        }

        var uid = parameters[0];
        var accountName = parameters[1];

        // Update local user state with account via BurstManager
        var burstManager = context.ServiceProvider.GetService(typeof(IBurstManager)) as IBurstManager;
        var user = burstManager?.GetByUid(uid);
        if (user != null)
        {
            user.SetAuthenticated(accountName);
        }

        // Propagate to other servers
        await context.BroadcastAsync(context.Message, cancellationToken);
    }

    /// <summary>
    /// Handles LOGOUT - clears user account on the network.
    /// </summary>
    private static async ValueTask HandleLogoutAsync(
        S2SContext context,
        string[] parameters,
        CancellationToken cancellationToken)
    {
        if (parameters.Length < 1)
        {
            return;
        }

        var uid = parameters[0];

        // Clear account from local user state via BurstManager
        var burstManager = context.ServiceProvider.GetService(typeof(IBurstManager)) as IBurstManager;
        var user = burstManager?.GetByUid(uid);
        if (user != null)
        {
            user.ClearAuthentication();
        }

        // Propagate to other servers
        await context.BroadcastAsync(context.Message, cancellationToken);
    }

    /// <summary>
    /// Handles CERTFP - propagates client certificate fingerprint.
    /// </summary>
    /// <remarks>
    /// Syntax: ENCAP * CERTFP uid fingerprint
    /// </remarks>
    private static async ValueTask HandleCertfpAsync(
        S2SContext context,
        string[] parameters,
        CancellationToken cancellationToken)
    {
        if (parameters.Length < 2)
        {
            return;
        }

        var uid = parameters[0];
        var fingerprint = parameters[1];

        // Store fingerprint for the user
        // Propagate to other servers
        await context.BroadcastAsync(context.Message, cancellationToken);
    }

    /// <summary>
    /// Handles SASL - SASL authentication message relay.
    /// </summary>
    /// <remarks>
    /// Syntax: ENCAP target_sid SASL source_uid target_uid mode data
    /// Used to relay SASL messages between client and services.
    /// </remarks>
    private static async ValueTask HandleSaslAsync(
        S2SContext context,
        string[] parameters,
        CancellationToken cancellationToken)
    {
        if (parameters.Length < 4)
        {
            return;
        }

        var sourceUid = parameters[0];
        var targetUid = parameters[1];
        var mode = parameters[2];
        var data = parameters.Length > 3 ? parameters[3] : string.Empty;

        // Route SASL message
        var targetSid = targetUid[..3];
        if (targetSid == context.LocalServerId.Sid)
        {
            // Handle locally - dispatch to SASL handler
        }
        else
        {
            // Forward to target server
            await context.Links.SendToServerAsync(targetSid, context.Message, cancellationToken);
        }
    }

    /// <summary>
    /// Handles KLINE - K-line propagation.
    /// </summary>
    private static async ValueTask HandleKlineAsync(
        S2SContext context,
        string[] parameters,
        CancellationToken cancellationToken)
    {
        if (parameters.Length < 4)
        {
            return;
        }

        var duration = parameters[0];
        var user = parameters[1];
        var host = parameters[2];
        var reason = parameters[3];
        var mask = $"{user}@{host}";

        TimeSpan? banDuration = ParseDuration(duration);
        var expiresAt = banDuration.HasValue
            ? DateTimeOffset.UtcNow.Add(banDuration.Value)
            : (DateTimeOffset?)null;

        var ban = new ServerBan(
            BanType.KLine,
            mask,
            reason,
            context.Message.Source ?? "unknown",
            DateTimeOffset.UtcNow,
            expiresAt);

        var banRepo = context.ServiceProvider.GetService(typeof(IServerBanRepository)) as IServerBanRepository;
        banRepo?.Add(ban);

        await context.BroadcastAsync(context.Message, cancellationToken);
    }

    /// <summary>
    /// Handles UNKLINE - removes a K-line.
    /// </summary>
    private static async ValueTask HandleUnklineAsync(
        S2SContext context,
        string[] parameters,
        CancellationToken cancellationToken)
    {
        if (parameters.Length < 2)
        {
            return;
        }

        var user = parameters[0];
        var host = parameters[1];
        var mask = $"{user}@{host}";

        var banRepo = context.ServiceProvider.GetService(typeof(IServerBanRepository)) as IServerBanRepository;
        banRepo?.Remove(BanType.KLine, mask);

        await context.BroadcastAsync(context.Message, cancellationToken);
    }

    /// <summary>
    /// Handles XLINE - extended ban/filter (gecos ban, etc).
    /// </summary>
    private static async ValueTask HandleXlineAsync(
        S2SContext context,
        string[] parameters,
        CancellationToken cancellationToken)
    {
        // X-lines are for matching against realname/gecos
        // Implementation would add to a separate filter list
        await context.BroadcastAsync(context.Message, cancellationToken);
    }

    /// <summary>
    /// Handles UNXLINE - removes an X-line.
    /// </summary>
    private static async ValueTask HandleUnxlineAsync(
        S2SContext context,
        string[] parameters,
        CancellationToken cancellationToken)
    {
        await context.BroadcastAsync(context.Message, cancellationToken);
    }

    /// <summary>
    /// Handles RESV - reserves (blocks) a nickname or channel.
    /// </summary>
    private static async ValueTask HandleResvAsync(
        S2SContext context,
        string[] parameters,
        CancellationToken cancellationToken)
    {
        // RESV blocks nicknames or channel names from being used
        await context.BroadcastAsync(context.Message, cancellationToken);
    }

    /// <summary>
    /// Handles UNRESV - removes a reservation.
    /// </summary>
    private static async ValueTask HandleUnresvAsync(
        S2SContext context,
        string[] parameters,
        CancellationToken cancellationToken)
    {
        await context.BroadcastAsync(context.Message, cancellationToken);
    }

    private static TimeSpan? ParseDuration(string duration)
    {
        if (duration == "0" || duration.Equals("permanent", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (duration.Length < 2)
        {
            return null;
        }

        var unit = duration[^1];
        if (!int.TryParse(duration[..^1], out var value))
        {
            return null;
        }

        return unit switch
        {
            'm' or 'M' => TimeSpan.FromMinutes(value),
            'h' or 'H' => TimeSpan.FromHours(value),
            'd' or 'D' => TimeSpan.FromDays(value),
            'w' or 'W' => TimeSpan.FromDays(value * 7),
            _ => null
        };
    }
}
