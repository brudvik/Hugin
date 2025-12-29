using System.Net;
using System.Net.Sockets;
using Hugin.Security;
using Microsoft.Extensions.Logging;

namespace Hugin.Network;

/// <summary>
/// TCP listener for IRC client connections.
/// </summary>
public sealed class TcpListener : IAsyncDisposable
{
    private readonly IPEndPoint _endpoint;
    private readonly TlsConfiguration? _tlsConfig;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger<TcpListener> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    /// <summary>
    /// Event raised when a new connection is accepted.
    /// </summary>
    public event Func<ClientConnection, Task>? ConnectionAccepted;

    /// <summary>
    /// Gets whether the listener is currently accepting connections.
    /// </summary>
    public bool IsListening => _socket?.IsBound == true;

    /// <summary>
    /// Gets the local endpoint the listener is bound to.
    /// </summary>
    public IPEndPoint? LocalEndPoint => _socket?.LocalEndPoint as IPEndPoint;

    /// <summary>
    /// Creates a new TCP listener.
    /// </summary>
    /// <param name="endpoint">The endpoint to listen on.</param>
    /// <param name="tlsConfig">TLS configuration for secure connections.</param>
    /// <param name="rateLimiter">Rate limiter for connection throttling.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    public TcpListener(
        IPEndPoint endpoint,
        TlsConfiguration? tlsConfig,
        RateLimiter rateLimiter,
        ILoggerFactory loggerFactory)
    {
        _endpoint = endpoint;
        _tlsConfig = tlsConfig;
        _rateLimiter = rateLimiter;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<TcpListener>();
    }

    /// <summary>
    /// Starts listening for connections.
    /// </summary>
    public void Start()
    {
        if (_socket is not null)
        {
            throw new InvalidOperationException("Listener is already started");
        }

        _socket = new Socket(_endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        // Disable Nagle's algorithm for lower latency
        _socket.NoDelay = true;

        _socket.Bind(_endpoint);
        _socket.Listen(128);

        _cts = new CancellationTokenSource();
        _acceptTask = AcceptLoopAsync(_cts.Token);

        _logger.LogInformation(
            "Listening on {Endpoint} ({Protocol})",
            _endpoint,
            _tlsConfig?.Certificate is not null ? "TLS" : "Plain");
    }

    /// <summary>
    /// Stops listening for connections.
    /// </summary>
    public async Task StopAsync()
    {
        if (_socket is null)
        {
            return;
        }

        _cts?.Cancel();

        try
        {
            _socket.Close();
        }
        catch
        {
            // Ignore close errors
        }

        if (_acceptTask is not null)
        {
            try
            {
                await _acceptTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _socket = null;
        _cts?.Dispose();
        _cts = null;
        _acceptTask = null;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var clientSocket = await _socket!.AcceptAsync(cancellationToken);
                _ = HandleConnectionAsync(clientSocket, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting connection");
            }
        }
    }

    private async Task HandleConnectionAsync(Socket clientSocket, CancellationToken cancellationToken)
    {
        var remoteEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;

        try
        {
            // Rate limit check
            if (remoteEndPoint is not null && !_rateLimiter.TryConsumeConnection(remoteEndPoint.Address))
            {
                _logger.LogWarning("Rate limited connection from {Address}", remoteEndPoint.Address);
                clientSocket.Close();
                return;
            }

            var connection = await ClientConnection.CreateAsync(
                clientSocket,
                _tlsConfig,
                _loggerFactory.CreateLogger<ClientConnection>(),
                cancellationToken);

            _logger.LogDebug(
                "Accepted connection {ConnectionId} from {RemoteEndPoint}",
                connection.ConnectionId,
                remoteEndPoint);

            if (ConnectionAccepted is not null)
            {
                await ConnectionAccepted(connection);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to establish connection from {RemoteEndPoint}", remoteEndPoint);
            try
            {
                clientSocket.Close();
            }
            catch
            {
                // Ignore
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
