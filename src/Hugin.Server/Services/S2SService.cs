using System.Net;
using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Hugin.Network.S2S;
using Hugin.Protocol.S2S;
using Hugin.Security;
using Hugin.Server.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hugin.Server.Services;

/// <summary>
/// Background service that manages S2S connections.
/// </summary>
public sealed class S2SService : BackgroundService
{
    private readonly HuginConfiguration _config;
    private readonly IS2SConnectionManager _connectionManager;
    private readonly IServerLinkManager _linkManager;
    private readonly S2SMessageDispatcher _dispatcher;
    private readonly S2SConnector _connector;
    private readonly TlsConfiguration? _tlsConfig;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<S2SService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly List<S2SListener> _listeners = new();

    /// <summary>
    /// Creates a new S2S service.
    /// </summary>
    public S2SService(
        IOptions<HuginConfiguration> config,
        IS2SConnectionManager connectionManager,
        IServerLinkManager linkManager,
        S2SMessageDispatcher dispatcher,
        S2SConnector connector,
        TlsConfiguration? tlsConfig,
        IServiceScopeFactory scopeFactory,
        ILogger<S2SService> logger,
        ILoggerFactory loggerFactory)
    {
        _config = config.Value;
        _connectionManager = connectionManager;
        _linkManager = linkManager;
        _dispatcher = dispatcher;
        _connector = connector;
        _tlsConfig = tlsConfig;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start S2S listeners
        foreach (var listenerConfig in _config.Network.ServerListeners)
        {
            var endpoint = new IPEndPoint(
                IPAddress.Parse(listenerConfig.Address),
                listenerConfig.Port);

            var listener = new S2SListener(
                endpoint,
                listenerConfig.Tls ? _tlsConfig : null,
                _connectionManager,
                _loggerFactory.CreateLogger<S2SListener>(),
                _loggerFactory);

            listener.ConnectionAccepted += OnConnectionAcceptedAsync;

            await listener.StartAsync(stoppingToken);
            _listeners.Add(listener);

            _logger.LogInformation("S2S listener started on {Endpoint} (TLS: {Tls})",
                endpoint, listenerConfig.Tls);
        }

        // Connect to configured servers (from config file)
        foreach (var serverConfig in _config.Network.LinkedServers.Where(s => s.AutoConnect))
        {
            _ = ConnectToServerFromConfigAsync(serverConfig, stoppingToken);
        }

        // Connect to database-configured servers with auto-connect
        await ConnectToAutoConnectServersAsync(stoppingToken);

        // Wait until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private async Task ConnectToAutoConnectServersAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetService<IServerLinkRepository>();
        if (repository is null)
        {
            return;
        }

        var autoConnectServers = await repository.GetAutoConnectAsync(cancellationToken);
        foreach (var serverLink in autoConnectServers)
        {
            _ = ConnectToServerFromDatabaseAsync(serverLink, cancellationToken);
        }
    }

    private async Task ConnectToServerFromConfigAsync(LinkedServerConfiguration serverConfig, CancellationToken cancellationToken)
    {
        var connection = await _connector.ConnectWithRetryAsync(
            serverConfig.Host,
            serverConfig.Port,
            maxRetries: 5,
            retryDelaySeconds: 30,
            cancellationToken);

        if (connection is not null)
        {
            _dispatcher.RegisterConnection(connection);

            // Send handshake
            await SendOutgoingHandshakeAsync(connection, serverConfig.Password, cancellationToken);
        }
    }

    private async Task ConnectToServerFromDatabaseAsync(ServerLinkEntity serverLink, CancellationToken cancellationToken)
    {
        var connection = await _connector.ConnectWithRetryAsync(
            serverLink.Host,
            serverLink.Port,
            maxRetries: 5,
            retryDelaySeconds: 30,
            cancellationToken);

        if (connection is not null)
        {
            _dispatcher.RegisterConnection(connection);

            // Send handshake
            await SendOutgoingHandshakeAsync(connection, serverLink.SendPassword, cancellationToken);

            // Update last connected time
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetService<IServerLinkRepository>();
            if (repository is not null)
            {
                await repository.UpdateLastConnectedAsync(serverLink.Id, DateTimeOffset.UtcNow, cancellationToken);
            }
        }
    }

    private async ValueTask OnConnectionAcceptedAsync(IS2SConnection connection)
    {
        _dispatcher.RegisterConnection(connection);
        _logger.LogInformation("S2S connection accepted from {RemoteEndPoint}", connection.RemoteEndPoint);
    }

    private async Task SendOutgoingHandshakeAsync(
        IS2SConnection connection,
        string password,
        CancellationToken cancellationToken)
    {
        var localSid = _config.Server.Sid;
        var localName = _config.Server.Name;

        // PASS <password> TS 6 <sid>
        await connection.SendLineAsync($"PASS {password} TS 6 {localSid}", cancellationToken);

        // CAPAB <capabilities>
        await connection.SendLineAsync("CAPAB :QS ENCAP", cancellationToken);

        // SERVER <name> <hopcount> :<description>
        await connection.SendLineAsync($"SERVER {localName} 1 :{_config.Server.Description}", cancellationToken);

        _logger.LogDebug("Sent handshake to connection {ConnectionId}", connection.ConnectionId);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping S2S service");

        // Close all S2S connections
        foreach (var connection in _connectionManager.GetAllConnections())
        {
            await _connectionManager.CloseConnectionAsync(connection.ConnectionId, "Server shutting down", cancellationToken);
        }

        // Stop all listeners
        foreach (var listener in _listeners)
        {
            await listener.DisposeAsync();
        }
        _listeners.Clear();

        await base.StopAsync(cancellationToken);
    }
}
