using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Hugin.Core.Interfaces;
using Hugin.Security;
using Microsoft.Extensions.Logging;

namespace Hugin.Network;

/// <summary>
/// Represents a client connection using System.IO.Pipelines for high-performance I/O.
/// </summary>
public sealed class ClientConnection : IClientConnection, IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly Stream _stream;
    private readonly PipeReader _reader;
    private readonly PipeWriter _writer;
    private readonly ILogger<ClientConnection> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Gets the unique connection identifier.
    /// </summary>
    public Guid ConnectionId { get; }

    /// <summary>
    /// Gets whether the connection is still active.
    /// </summary>
    public bool IsActive => !_disposed && _socket.Connected;

    /// <summary>
    /// Gets whether the connection is using TLS.
    /// </summary>
    public bool IsSecure { get; }

    /// <summary>
    /// Gets the client certificate fingerprint, if any.
    /// </summary>
    public string? CertificateFingerprint { get; }

    /// <summary>
    /// Gets the remote endpoint of the connection.
    /// </summary>
    public EndPoint? RemoteEndPoint => _socket.RemoteEndPoint;

    /// <summary>
    /// Event raised when a complete IRC line is received.
    /// </summary>
    public event Func<ClientConnection, string, ValueTask>? LineReceived;

    /// <summary>
    /// Event raised when the connection is closed.
    /// </summary>
    public event Func<ClientConnection, Exception?, ValueTask>? Disconnected;

    private ClientConnection(
        Guid connectionId,
        Socket socket,
        Stream stream,
        bool isSecure,
        string? certificateFingerprint,
        ILogger<ClientConnection> logger)
    {
        ConnectionId = connectionId;
        _socket = socket;
        _stream = stream;
        IsSecure = isSecure;
        CertificateFingerprint = certificateFingerprint;
        _logger = logger;

        var pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: 65536,
            resumeWriterThreshold: 32768,
            useSynchronizationContext: false));

        _reader = PipeReader.Create(_stream);
        _writer = PipeWriter.Create(_stream);
    }

    /// <summary>
    /// Creates a new connection from an accepted socket.
    /// </summary>
    public static async Task<ClientConnection> CreateAsync(
        Socket socket,
        TlsConfiguration? tlsConfig,
        ILogger<ClientConnection> logger,
        CancellationToken cancellationToken = default)
    {
        var connectionId = Guid.NewGuid();
        Stream stream = new NetworkStream(socket, ownsSocket: false);
        bool isSecure = false;
        string? fingerprint = null;

        // Upgrade to TLS if configured
        if (tlsConfig?.Certificate is not null)
        {
            var sslStream = new SslStream(
                stream,
                leaveInnerStreamOpen: false,
                tlsConfig.CertificateValidationCallback);

            try
            {
                await sslStream.AuthenticateAsServerAsync(
                    tlsConfig.CreateServerOptions(),
                    cancellationToken);

                isSecure = true;
                stream = sslStream;

                // Get client certificate fingerprint if provided
                if (sslStream.RemoteCertificate is X509Certificate2 clientCert)
                {
                    fingerprint = Hugin.Security.CertificateFingerprint.GetSha256Fingerprint(clientCert);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TLS handshake failed for {RemoteEndPoint}", socket.RemoteEndPoint);
                await sslStream.DisposeAsync();
                throw;
            }
        }

        return new ClientConnection(connectionId, socket, stream, isSecure, fingerprint, logger);
    }

    /// <summary>
    /// Starts reading from the connection.
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection {ConnectionId} read error", ConnectionId);
            await RaiseDisconnectedAsync(ex);
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await _reader.ReadAsync(cancellationToken);
            var buffer = result.Buffer;

            while (TryReadLine(ref buffer, out var line))
            {
                await ProcessLineAsync(line);
            }

            _reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
            {
                break;
            }
        }

        await RaiseDisconnectedAsync(null);
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out string line)
    {
        // Look for \r\n or \n
        var reader = new SequenceReader<byte>(buffer);

        if (reader.TryReadTo(out ReadOnlySpan<byte> span, (byte)'\n'))
        {
            // Remove trailing \r if present
            if (span.Length > 0 && span[^1] == '\r')
            {
                span = span[..^1];
            }

            line = Encoding.UTF8.GetString(span);
            buffer = buffer.Slice(reader.Position);
            return true;
        }

        line = string.Empty;
        return false;
    }

    private async ValueTask ProcessLineAsync(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        // Enforce maximum line length
        if (line.Length > 4096)
        {
            _logger.LogWarning("Connection {ConnectionId} sent line exceeding maximum length", ConnectionId);
            return;
        }

        if (LineReceived is not null)
        {
            await LineReceived(this, line);
        }
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _writer.WriteAsync(data, cancellationToken);
            await _writer.FlushAsync(cancellationToken);
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
        if (_disposed)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        await SendAsync(bytes, cancellationToken);
    }

    public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        _cts.Cancel();

        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch
        {
            // Ignore shutdown errors
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();

        await _reader.CompleteAsync();
        await _writer.CompleteAsync();
        await _stream.DisposeAsync();

        try
        {
            _socket.Close();
            _socket.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }

        _cts.Dispose();
        _writeLock.Dispose();
    }
}
