using System.Buffers;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using Hugin.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hugin.Network;

/// <summary>
/// Represents a WebSocket client connection for web-based IRC clients.
/// </summary>
/// <remarks>
/// This class wraps a WebSocket connection and provides the same interface
/// as TCP connections, allowing web clients (KiwiIRC, The Lounge, etc.)
/// to connect to the IRC server.
/// </remarks>
public sealed class WebSocketConnection : IClientConnection, IAsyncDisposable
{
    private readonly WebSocket _webSocket;
    private readonly ILogger<WebSocketConnection> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly StringBuilder _lineBuffer = new();
    private bool _disposed;

    /// <summary>
    /// Gets the unique connection identifier.
    /// </summary>
    public Guid ConnectionId { get; }

    /// <summary>
    /// Gets whether the connection is still active.
    /// </summary>
    public bool IsActive => !_disposed && _webSocket.State == WebSocketState.Open;

    /// <summary>
    /// Gets whether the connection is using TLS.
    /// Always true for WebSocket connections (wss://).
    /// </summary>
    public bool IsSecure { get; }

    /// <summary>
    /// Gets the client certificate fingerprint, if any.
    /// </summary>
    public string? CertificateFingerprint { get; }

    /// <summary>
    /// Gets the remote endpoint of the connection.
    /// </summary>
    public EndPoint? RemoteEndPoint { get; }

    /// <summary>
    /// Event raised when a complete IRC line is received.
    /// </summary>
    public event Func<WebSocketConnection, string, ValueTask>? LineReceived;

    /// <summary>
    /// Event raised when the connection is closed.
    /// </summary>
    public event Func<WebSocketConnection, Exception?, ValueTask>? Disconnected;

    /// <summary>
    /// Creates a new WebSocket connection wrapper.
    /// </summary>
    /// <param name="connectionId">The unique connection identifier.</param>
    /// <param name="webSocket">The underlying WebSocket.</param>
    /// <param name="remoteEndPoint">The remote endpoint.</param>
    /// <param name="isSecure">Whether the connection uses TLS.</param>
    /// <param name="certificateFingerprint">Client certificate fingerprint if any.</param>
    /// <param name="logger">Logger instance.</param>
    public WebSocketConnection(
        Guid connectionId,
        WebSocket webSocket,
        EndPoint? remoteEndPoint,
        bool isSecure,
        string? certificateFingerprint,
        ILogger<WebSocketConnection> logger)
    {
        ConnectionId = connectionId;
        _webSocket = webSocket;
        RemoteEndPoint = remoteEndPoint;
        IsSecure = isSecure;
        CertificateFingerprint = certificateFingerprint;
        _logger = logger;
    }

    /// <summary>
    /// Starts reading from the WebSocket connection.
    /// </summary>
    public async Task StartReadingAsync()
    {
        try
        {
            await ReadLoopAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            // Client disconnected
            _logger.LogDebug("WebSocket {ConnectionId} closed prematurely", ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WebSocket {ConnectionId} read error", ConnectionId);
            await RaiseDisconnectedAsync(ex);
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   _webSocket.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _webSocket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Goodbye",
                        cancellationToken);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await ProcessTextAsync(text, result.EndOfMessage);
                }
                // Binary messages are ignored for IRC
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        await RaiseDisconnectedAsync(null);
    }

    private async ValueTask ProcessTextAsync(string text, bool endOfMessage)
    {
        _lineBuffer.Append(text);

        if (!endOfMessage)
        {
            return;
        }

        var content = _lineBuffer.ToString();
        _lineBuffer.Clear();

        // Split by newlines (web clients may send multiple lines in one message)
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Enforce maximum line length
            if (line.Length > 4096)
            {
                _logger.LogWarning("WebSocket {ConnectionId} sent line exceeding maximum length", ConnectionId);
                continue;
            }

            if (LineReceived is not null)
            {
                await LineReceived(this, line);
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_disposed || _webSocket.State != WebSocketState.Open)
        {
            return;
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _webSocket.SendAsync(
                data,
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Sends a string line to the client.
    /// </summary>
    public async ValueTask SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        if (_disposed || _webSocket.State != WebSocketState.Open)
        {
            return;
        }

        // WebSocket clients expect \r\n line endings
        var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        await SendAsync(bytes, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        _cts.Cancel();

        try
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Connection closed",
                    cancellationToken);
            }
        }
        catch
        {
            // Ignore close errors
        }

        await DisposeAsync();
    }

    private async ValueTask RaiseDisconnectedAsync(Exception? exception)
    {
        if (Disconnected is not null)
        {
            await Disconnected(this, exception);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();

        try
        {
            _webSocket.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }

        _cts.Dispose();
        _writeLock.Dispose();
        await Task.CompletedTask;
    }
}
