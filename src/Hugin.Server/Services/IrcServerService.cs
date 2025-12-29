using System.Net;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Hugin.Network;
using Hugin.Protocol;
using Hugin.Protocol.Commands;
using Hugin.Security;
using Hugin.Server.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hugin.Server.Services;

/// <summary>
/// The main IRC server service.
/// </summary>
public sealed class IrcServerService : BackgroundService
{
    private readonly HuginConfiguration _config;
    private readonly IUserRepository _users;
    private readonly IChannelRepository _channels;
    private readonly ConnectionManager _connectionManager;
    private readonly MessageBroker _messageBroker;
    private readonly CommandRegistry _commandRegistry;
    private readonly RateLimiter _rateLimiter;
    private readonly HostCloaker _cloaker;
    private readonly TlsConfiguration _tlsConfig;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IrcServerService> _logger;
    private readonly List<Network.TcpListener> _listeners = new();
    private readonly ServerId _serverId;
    private readonly DateTimeOffset _startTime;

    /// <summary>
    /// Creates a new IRC server service.
    /// </summary>
    /// <param name="config">Server configuration options.</param>
    /// <param name="users">User repository for connection tracking.</param>
    /// <param name="channels">Channel repository.</param>
    /// <param name="connectionManager">Connection manager for tracking clients.</param>
    /// <param name="messageBroker">Message broker for routing messages.</param>
    /// <param name="commandRegistry">Command handler registry.</param>
    /// <param name="rateLimiter">Rate limiter for throttling.</param>
    /// <param name="cloaker">Host cloaker for privacy.</param>
    /// <param name="tlsConfig">TLS configuration for secure connections.</param>
    /// <param name="serviceProvider">Service provider for dependency resolution.</param>
    /// <param name="logger">Logger instance.</param>
    public IrcServerService(
        IOptions<HuginConfiguration> config,
        IUserRepository users,
        IChannelRepository channels,
        ConnectionManager connectionManager,
        MessageBroker messageBroker,
        CommandRegistry commandRegistry,
        RateLimiter rateLimiter,
        HostCloaker cloaker,
        TlsConfiguration tlsConfig,
        IServiceProvider serviceProvider,
        ILogger<IrcServerService> logger)
    {
        _config = config.Value;
        _users = users;
        _channels = channels;
        _connectionManager = connectionManager;
        _messageBroker = messageBroker;
        _commandRegistry = commandRegistry;
        _rateLimiter = rateLimiter;
        _cloaker = cloaker;
        _tlsConfig = tlsConfig;
        _serviceProvider = serviceProvider;
        _logger = logger;

        _serverId = ServerId.Create(_config.Server.Sid, _config.Server.Name);
        _startTime = DateTimeOffset.UtcNow;

        _commandRegistry.RegisterBuiltInHandlers();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Hugin IRC Server v{Version}", GetVersion());

        // Start listeners
        foreach (var listenerConfig in _config.Network.Listeners)
        {
            try
            {
                var endpoint = new IPEndPoint(
                    IPAddress.Parse(listenerConfig.Address),
                    listenerConfig.Port);

                var tlsConfig = listenerConfig.Tls ? _tlsConfig : null;

                var listener = new Network.TcpListener(
                    endpoint,
                    tlsConfig,
                    _rateLimiter,
                    (ILoggerFactory)_serviceProvider.GetService(typeof(ILoggerFactory))!);

                listener.ConnectionAccepted += OnConnectionAcceptedAsync;
                listener.Start();
                _listeners.Add(listener);

                _logger.LogInformation(
                    "Listening on {Endpoint} ({Protocol})",
                    endpoint,
                    listenerConfig.Tls ? "TLS" : "Plain");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start listener on {Address}:{Port}",
                    listenerConfig.Address, listenerConfig.Port);
            }
        }

        if (_listeners.Count == 0)
        {
            _logger.LogCritical("No listeners started. Server cannot accept connections.");
            return;
        }

        _logger.LogInformation("Server started successfully with {Count} listener(s)", _listeners.Count);

        // Keep running until stopped
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }

        _logger.LogInformation("Shutting down Hugin IRC Server");

        // Stop all listeners
        foreach (var listener in _listeners)
        {
            await listener.StopAsync();
        }
    }

    private async Task OnConnectionAcceptedAsync(ClientConnection connection)
    {
        var ipEndpoint = connection.RemoteEndPoint as IPEndPoint;
        var hostname = ipEndpoint?.Address.ToString() ?? "unknown";

        // Create user entity
        var user = new User(
            connection.ConnectionId,
            ipEndpoint?.Address ?? IPAddress.Loopback,
            hostname,
            _serverId,
            connection.IsSecure);

        // Apply hostname cloaking
        if (!string.IsNullOrEmpty(_config.Security.CloakSecret))
        {
            var cloaked = _cloaker.Cloak(hostname);
            user.SetCloakedHostname(cloaked);
        }

        _users.Add(user);
        _connectionManager.RegisterConnection(connection.ConnectionId, connection);

        // Create capability manager for this connection
        var caps = new CapabilityManager();

        // Set up event handlers
        connection.LineReceived += async (conn, line) =>
        {
            await ProcessLineAsync(conn, user, caps, line);
        };

        connection.Disconnected += async (conn, ex) =>
        {
            await HandleDisconnectAsync(conn, user, ex);
        };

        _logger.LogDebug("New connection {ConnectionId} from {Address}", connection.ConnectionId, hostname);

        // Start reading
        _ = connection.StartReadingAsync();
    }

    private async ValueTask ProcessLineAsync(ClientConnection connection, User user, CapabilityManager caps, string line)
    {
        // Redact AUTHENTICATE payloads to prevent credential leakage in logs
        var logLine = line.StartsWith("AUTHENTICATE ", StringComparison.OrdinalIgnoreCase) && line.Length > 13
            ? "AUTHENTICATE <redacted>"
            : line;
        _logger.LogTrace("← [{ConnectionId}] {Line}", connection.ConnectionId, logLine);

        if (!IrcMessage.TryParse(line, out var message))
        {
            _logger.LogWarning("Failed to parse message from {ConnectionId}: {Line}",
                connection.ConnectionId, line);
            return;
        }

        // Rate limit check
        if (!_rateLimiter.TryConsumeCommand(connection.ConnectionId, message.Command))
        {
            _logger.LogWarning("Rate limited command from {ConnectionId}", connection.ConnectionId);
            return;
        }

        var handler = _commandRegistry.GetHandler(message.Command);
        if (handler is null)
        {
            // Only send unknown command error if registered
            if (user.IsRegistered)
            {
                await _messageBroker.SendToConnectionAsync(
                    connection.ConnectionId,
                    IrcNumerics.UnknownCommand(_config.Server.Name, user.Nickname.Value, message.Command).ToString());
            }
            return;
        }

        // Check registration requirement
        if (handler.RequiresRegistration && !user.IsRegistered)
        {
            await _messageBroker.SendToConnectionAsync(
                connection.ConnectionId,
                IrcNumerics.NotRegistered(_config.Server.Name, user.Nickname?.Value ?? "*").ToString());
            return;
        }

        // Check operator requirement
        if (handler.RequiresOperator && !user.IsOperator)
        {
            await _messageBroker.SendToConnectionAsync(
                connection.ConnectionId,
                IrcNumerics.NoPrivileges(_config.Server.Name, user.Nickname.Value).ToString());
            return;
        }

        // Check minimum parameters
        if (message.Parameters.Count < handler.MinimumParameters)
        {
            await _messageBroker.SendToConnectionAsync(
                connection.ConnectionId,
                IrcNumerics.NeedMoreParams(_config.Server.Name, user.Nickname?.Value ?? "*", message.Command).ToString());
            return;
        }

        // Create command context
        var context = new CommandContext(
            message,
            user,
            connection,
            _users,
            _channels,
            _messageBroker,
            caps,
            _config.Server.Name,
            _serverId,
            type => _serviceProvider.GetService(type));

        try
        {
            await handler.HandleAsync(context);

            // Check if registration is complete
            if (!user.IsRegistered && !caps.IsNegotiating)
            {
                await CheckRegistrationCompleteAsync(connection, user, caps);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling command {Command} from {ConnectionId}",
                message.Command, connection.ConnectionId);
        }
    }

    private async ValueTask CheckRegistrationCompleteAsync(ClientConnection connection, User user, CapabilityManager caps)
    {
        if (user.RegistrationState == RegistrationState.NickAndUserReceived)
        {
            user.SetRegistrationState(RegistrationState.Registered);

            // Send welcome burst
            await SendWelcomeBurstAsync(connection, user, caps);
        }
    }

    private async ValueTask SendWelcomeBurstAsync(ClientConnection connection, User user, CapabilityManager caps)
    {
        var server = _config.Server.Name;
        var nick = user.Nickname.Value;
        var fullHost = user.Hostmask.ToString();

        // 001-004
        await SendAsync(connection, IrcNumerics.Welcome(server, nick, fullHost));
        await SendAsync(connection, IrcNumerics.YourHost(server, nick, server, GetVersion()));
        await SendAsync(connection, IrcNumerics.Created(server, nick, _startTime));
        await SendAsync(connection, IrcNumerics.MyInfo(server, nick, server, GetVersion(), "iowrs", "beIiklmnopstv"));

        // 005 ISUPPORT
        await SendISupport(connection, nick);

        // LUSERS
        await SendLusers(connection, user);

        // MOTD
        await SendMotd(connection, nick);

        _logger.LogInformation("User {Nick} ({Host}) registered from {Address}",
            nick, fullHost, connection.RemoteEndPoint);
    }

    private async ValueTask SendISupport(ClientConnection connection, string nick)
    {
        var tokens = new List<string>
        {
            $"NETWORK={_config.Server.NetworkName}",
            "CASEMAPPING=ascii",
            "CHARSET=utf-8",
            $"NICKLEN={_config.Limits.MaxNickLength}",
            $"CHANNELLEN={_config.Limits.MaxChannelLength}",
            $"TOPICLEN={_config.Limits.MaxTopicLength}",
            $"KICKLEN={_config.Limits.MaxKickLength}",
            $"AWAYLEN={_config.Limits.MaxAwayLength}",
            $"CHANLIMIT=#:{_config.Limits.MaxChannels}",
            $"MAXTARGETS={_config.Limits.MaxTargets}",
            "CHANTYPES=#&",
            "PREFIX=(qaohv)~&@%+",
            "CHANMODES=beI,k,l,cCimnpRsSt",
            "MODES=4",
            "STATUSMSG=~&@%+",
            "SAFELIST",
            "ELIST=CMNTU",
            "EXCEPTS=e",
            "INVEX=I",
            "EXTBAN=$,arz",
            "WHOX",
            "MONITOR=100",
            "UTF8ONLY"
        };

        // Split into chunks of 13 tokens (standard limit)
        foreach (var chunk in tokens.Chunk(13))
        {
            await SendAsync(connection, IrcNumerics.ISupport(_config.Server.Name, nick, chunk));
        }
    }

    private async ValueTask SendLusers(ClientConnection connection, User user)
    {
        var server = _config.Server.Name;
        var nick = user.Nickname.Value;
        var total = _users.GetCount();
        var invisible = _users.GetInvisibleCount();
        var visible = total - invisible;
        var operators = _users.GetOperatorCount();
        var channels = _channels.GetCount();
        var maxUsers = _users.GetMaxUserCount();

        await SendAsync(connection, IrcNumerics.LuserClient(server, nick, visible, invisible, 1));
        await SendAsync(connection, IrcNumerics.LuserOp(server, nick, operators));
        await SendAsync(connection, IrcNumerics.LuserChannels(server, nick, channels));
        await SendAsync(connection, IrcNumerics.LuserMe(server, nick, total, 0));
        await SendAsync(connection, IrcNumerics.LocalUsers(server, nick, total, Math.Max(total, maxUsers)));
        await SendAsync(connection, IrcNumerics.GlobalUsers(server, nick, total, Math.Max(total, maxUsers)));
    }

    private async ValueTask SendMotd(ClientConnection connection, string nick)
    {
        var server = _config.Server.Name;

        if (_config.Motd.Count == 0)
        {
            await SendAsync(connection, IrcNumerics.NoMotd(server, nick));
            return;
        }

        await SendAsync(connection, IrcNumerics.MotdStart(server, nick, server));
        foreach (var line in _config.Motd)
        {
            await SendAsync(connection, IrcNumerics.Motd(server, nick, line));
        }
        await SendAsync(connection, IrcNumerics.EndOfMotd(server, nick));
    }

    private async ValueTask HandleDisconnectAsync(ClientConnection connection, User user, Exception? ex)
    {
        _logger.LogDebug("Connection {ConnectionId} disconnected: {Reason}",
            connection.ConnectionId, ex?.Message ?? "Clean disconnect");

        // Clean up user from channels
        foreach (var channelName in user.Channels.Keys.ToList())
        {
            var channel = _channels.GetByName(channelName);
            if (channel is not null)
            {
                channel.RemoveMember(user.ConnectionId);
                _connectionManager.PartChannel(user.ConnectionId, channelName.Value);

                if (channel.IsEmpty)
                {
                    _channels.Remove(channelName);
                }
            }
        }

        _users.Remove(user.ConnectionId);
        _connectionManager.UnregisterConnection(user.ConnectionId);

        await connection.DisposeAsync();
    }

    private static async ValueTask SendAsync(ClientConnection connection, IrcMessage message)
    {
        await connection.SendLineAsync(message.ToString());
    }

    private static string GetVersion()
    {
        var assembly = typeof(IrcServerService).Assembly;
        var version = assembly.GetName().Version;
        return $"hugin-{version?.Major ?? 0}.{version?.Minor ?? 1}.{version?.Build ?? 0}";
    }
}
