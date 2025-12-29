using System.Globalization;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Hugin.Protocol.S2S.Services;

/// <summary>
/// Manages network services (NickServ, ChanServ, etc).
/// </summary>
public interface IServicesManager
{
    /// <summary>
    /// Gets all registered services.
    /// </summary>
    IEnumerable<INetworkService> Services { get; }

    /// <summary>
    /// Gets a service by nickname.
    /// </summary>
    INetworkService? GetService(string nickname);

    /// <summary>
    /// Registers a network service.
    /// </summary>
    void RegisterService(INetworkService service);

    /// <summary>
    /// Handles a message directed to a service.
    /// </summary>
    ValueTask HandleServiceMessageAsync(
        string targetUid,
        string sourceUid,
        string sourceNick,
        string? sourceAccount,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a NOTICE from a service to a user.
    /// </summary>
    ValueTask SendNoticeAsync(string fromUid, string toUid, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a PRIVMSG from a service to a target.
    /// </summary>
    ValueTask SendPrivmsgAsync(string fromUid, string target, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Introduces all services to the network (during burst).
    /// </summary>
    ValueTask IntroduceServicesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of services manager.
/// </summary>
public sealed class ServicesManager : IServicesManager
{
    private readonly Dictionary<string, INetworkService> _services = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServerLinkManager _linkManager;
    private readonly ServerId _localServerId;
    private readonly ILogger<ServicesManager> _logger;

    /// <summary>
    /// Creates a new services manager.
    /// </summary>
    /// <param name="linkManager">Server link manager for broadcasting to network.</param>
    /// <param name="localServerId">Local server ID.</param>
    /// <param name="networkServices">Collection of network services to register.</param>
    /// <param name="logger">Logger instance.</param>
    public ServicesManager(
        IServerLinkManager linkManager,
        ServerId localServerId,
        IEnumerable<INetworkService> networkServices,
        ILogger<ServicesManager> logger)
    {
        _linkManager = linkManager;
        _localServerId = localServerId;
        _logger = logger;

        // Register all injected services
        foreach (var service in networkServices)
        {
            RegisterService(service);
        }
    }

    /// <inheritdoc />
    public IEnumerable<INetworkService> Services => _services.Values;

    /// <inheritdoc />
    public INetworkService? GetService(string nickname)
    {
        return _services.TryGetValue(nickname, out var service) ? service : null;
    }

    /// <inheritdoc />
    public void RegisterService(INetworkService service)
    {
        _services[service.Nickname] = service;
        _logger.LogInformation("Registered network service: {Nickname} ({Uid})", service.Nickname, service.Uid);
    }

    /// <inheritdoc />
    public async ValueTask HandleServiceMessageAsync(
        string targetUid,
        string sourceUid,
        string sourceNick,
        string? sourceAccount,
        string message,
        CancellationToken cancellationToken = default)
    {
        // Find service by UID
        var service = _services.Values.FirstOrDefault(s => s.Uid == targetUid);
        if (service is null)
        {
            _logger.LogDebug("No service found for UID {TargetUid}", targetUid);
            return;
        }

        var context = new ServiceMessageContext(sourceUid, sourceNick, sourceAccount, message, this);

        try
        {
            await service.HandleMessageAsync(context, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling service message for {Service}: {Message}",
                service.Nickname, message);
            await SendNoticeAsync(service.Uid, sourceUid,
                "An error occurred processing your request. Please try again later.", cancellationToken);
        }
    }

    /// <inheritdoc />
    public async ValueTask SendNoticeAsync(string fromUid, string toUid, string message, CancellationToken cancellationToken = default)
    {
        var s2sMessage = S2SMessage.CreateWithSource(fromUid, "NOTICE", toUid, message);
        await _linkManager.BroadcastAsync(s2sMessage, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask SendPrivmsgAsync(string fromUid, string target, string message, CancellationToken cancellationToken = default)
    {
        var s2sMessage = S2SMessage.CreateWithSource(fromUid, "PRIVMSG", target, message);
        await _linkManager.BroadcastAsync(s2sMessage, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask IntroduceServicesAsync(CancellationToken cancellationToken = default)
    {
        var sid = _localServerId.Sid;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var service in _services.Values)
        {
            // UID <nickname> <hopcount> <nick_ts> <modes> <ident> <host> <ip> <uid> :<realname>
            // Services get +S mode (service) typically
            var uidMessage = S2SMessage.CreateWithSource(
                sid,
                "UID",
                service.Nickname,
                "1",
                timestamp.ToString(CultureInfo.InvariantCulture),
                "+S",
                service.Ident,
                service.Host,
                "0",
                service.Uid,
                service.Realname);

            await _linkManager.BroadcastAsync(uidMessage, cancellationToken: cancellationToken);

            _logger.LogDebug("Introduced service {Nickname} to network", service.Nickname);
        }
    }
}
