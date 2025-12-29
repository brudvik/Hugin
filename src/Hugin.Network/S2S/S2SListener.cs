using System.Net;
using System.Net.Sockets;
using Hugin.Security;
using Microsoft.Extensions.Logging;

namespace Hugin.Network.S2S;

/// <summary>
/// Listens for incoming S2S connections from other servers.
/// </summary>
public sealed class S2SListener : IAsyncDisposable
{
    private readonly IPEndPoint _endpoint;
    private readonly TlsConfiguration? _tlsConfig;
    private readonly IS2SConnectionManager _connectionManager;
    private readonly ILogger<S2SListener> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private Socket? _listenerSocket;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Event raised when a new S2S connection is accepted.
    /// </summary>
    public event Func<IS2SConnection, ValueTask>? ConnectionAccepted;

    /// <summary>
    /// Creates a new S2S listener.
    /// </summary>
    public S2SListener(
        IPEndPoint endpoint,
        TlsConfiguration? tlsConfig,
        IS2SConnectionManager connectionManager,
        ILogger<S2SListener> logger,
        ILoggerFactory loggerFactory)
    {
        _endpoint = endpoint;
        _tlsConfig = tlsConfig;
        _connectionManager = connectionManager;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Starts listening for incoming S2S connections.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _listenerSocket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        _listenerSocket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        _listenerSocket.Bind(_endpoint);
        _listenerSocket.Listen(128);

        _logger.LogInformation("S2S listener started on {Endpoint}", _endpoint);

        _ = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var socket = await _listenerSocket!.AcceptAsync(cancellationToken);
                _logger.LogInformation("Accepted S2S connection from {RemoteEndPoint}", socket.RemoteEndPoint);

                _ = HandleConnectionAsync(socket, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting S2S connection");
            }
        }
    }

    private async Task HandleConnectionAsync(Socket socket, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await S2SConnection.CreateFromAcceptedSocketAsync(
                socket,
                _tlsConfig,
                _loggerFactory.CreateLogger<S2SConnection>(),
                cancellationToken);

            _connectionManager.RegisterConnection(connection.ConnectionId, connection);

            connection.Disconnected += OnConnectionDisconnected;

            if (ConnectionAccepted is not null)
            {
                await ConnectionAccepted(connection);
            }

            // Start reading from the connection
            _ = connection.StartReadingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to establish S2S connection from {RemoteEndPoint}", socket.RemoteEndPoint);
            socket.Dispose();
        }
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

    /// <summary>
    /// Stops the listener.
    /// </summary>
    public async Task StopAsync()
    {
        _cts?.Cancel();

        if (_listenerSocket is not null)
        {
            _listenerSocket.Close();
            _listenerSocket.Dispose();
            _listenerSocket = null;
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }
}
