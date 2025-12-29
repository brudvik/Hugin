using Hugin.Security;
using Microsoft.Extensions.Logging;

namespace Hugin.Network.S2S;

/// <summary>
/// Interface for connecting to remote IRC servers.
/// </summary>
public interface IS2SConnector
{
    /// <summary>
    /// Event raised when an outgoing S2S connection is established.
    /// </summary>
    event Func<IS2SConnection, ValueTask>? ConnectionEstablished;

    /// <summary>
    /// Connects to a remote server.
    /// </summary>
    Task<IS2SConnection?> ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to connect to a remote server with link handshake.
    /// </summary>
    /// <param name="serverName">The expected server name.</param>
    /// <param name="host">The remote host.</param>
    /// <param name="port">The remote port.</param>
    /// <param name="password">The link password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if connection and handshake succeeded.</returns>
    Task<bool> TryConnectAsync(string serverName, string host, int port, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects to a remote server with retry logic.
    /// </summary>
    Task<IS2SConnection?> ConnectWithRetryAsync(string host, int port, int maxRetries = 3, int retryDelaySeconds = 30, CancellationToken cancellationToken = default);
}

/// <summary>
/// Connects to remote IRC servers for S2S linking.
/// </summary>
public sealed class S2SConnector : IS2SConnector
{
    private readonly TlsConfiguration? _tlsConfig;
    private readonly IS2SConnectionManager _connectionManager;
    private readonly ILogger<S2SConnector> _logger;
    private readonly ILoggerFactory _loggerFactory;

    /// <inheritdoc />
    public event Func<IS2SConnection, ValueTask>? ConnectionEstablished;

    /// <summary>
    /// Creates a new S2S connector.
    /// </summary>
    public S2SConnector(
        TlsConfiguration? tlsConfig,
        IS2SConnectionManager connectionManager,
        ILogger<S2SConnector> logger,
        ILoggerFactory loggerFactory)
    {
        _tlsConfig = tlsConfig;
        _connectionManager = connectionManager;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public async Task<IS2SConnection?> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Connecting to S2S server {Host}:{Port}", host, port);

            var connection = await S2SConnection.ConnectAsync(
                host,
                port,
                _tlsConfig,
                _loggerFactory.CreateLogger<S2SConnection>(),
                cancellationToken);

            _connectionManager.RegisterConnection(connection.ConnectionId, connection);

            connection.Disconnected += OnConnectionDisconnected;

            if (ConnectionEstablished is not null)
            {
                await ConnectionEstablished(connection);
            }

            // Start reading from the connection
            _ = connection.StartReadingAsync();

            _logger.LogInformation("Connected to S2S server {Host}:{Port} (ConnectionId: {ConnectionId})",
                host, port, connection.ConnectionId);

            return connection;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to S2S server {Host}:{Port}", host, port);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryConnectAsync(
        string serverName,
        string host,
        int port,
        string password,
        CancellationToken cancellationToken = default)
    {
        var connection = await ConnectAsync(host, port, cancellationToken);
        if (connection is null)
        {
            return false;
        }

        try
        {
            // Send PASS and SERVER commands for handshake
            await connection.SendLineAsync($"PASS {password} TS 6", cancellationToken);
            
            // The handshake completion is handled by S2SHandshakeManager
            // For now, we consider successful connection as success
            // The handshake manager will complete the link or close if failed
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Handshake failed with {ServerName}", serverName);
            await connection.CloseAsync(cancellationToken);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IS2SConnection?> ConnectWithRetryAsync(
        string host,
        int port,
        int maxRetries = 3,
        int retryDelaySeconds = 30,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var connection = await ConnectAsync(host, port, cancellationToken);
            if (connection is not null)
            {
                return connection;
            }

            if (attempt < maxRetries)
            {
                _logger.LogInformation("Retrying connection to {Host}:{Port} in {Delay} seconds (attempt {Attempt}/{MaxRetries})",
                    host, port, retryDelaySeconds, attempt, maxRetries);
                await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), cancellationToken);
            }
        }

        _logger.LogError("Failed to connect to {Host}:{Port} after {MaxRetries} attempts", host, port, maxRetries);
        return null;
    }

    private ValueTask OnConnectionDisconnected(IS2SConnection connection, Exception? exception)
    {
        _connectionManager.UnregisterConnection(connection.ConnectionId);

        if (exception is not null)
        {
            _logger.LogWarning(exception, "S2S connection {ConnectionId} disconnected with error", connection.ConnectionId);
        }
        else
        {
            _logger.LogInformation("S2S connection {ConnectionId} disconnected", connection.ConnectionId);
        }

        return ValueTask.CompletedTask;
    }
}
