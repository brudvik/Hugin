using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Hugin.Core.ValueObjects;
using Hugin.Security;
using Microsoft.Extensions.Logging;

namespace Hugin.Network.S2S;

/// <summary>
/// Represents a server-to-server connection using System.IO.Pipelines for high-performance I/O.
/// </summary>
public sealed class S2SConnection : IS2SConnection
{
    private readonly Socket _socket;
    private readonly Stream _stream;
    private readonly PipeReader _reader;
    private readonly PipeWriter _writer;
    private readonly ILogger<S2SConnection> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    /// <inheritdoc />
    public Guid ConnectionId { get; }

    /// <inheritdoc />
    public bool IsActive => !_disposed && _socket.Connected;

    /// <inheritdoc />
    public bool IsSecure { get; }

    /// <inheritdoc />
    public EndPoint? RemoteEndPoint => _socket.RemoteEndPoint;

    /// <inheritdoc />
    public ServerId? RemoteServerId { get; set; }

    /// <inheritdoc />
    public bool IsOutgoing { get; }

    /// <summary>
    /// Gets the client certificate fingerprint, if any.
    /// </summary>
    public string? CertificateFingerprint { get; }

    /// <inheritdoc />
    public event Func<IS2SConnection, string, ValueTask>? LineReceived;

    /// <inheritdoc />
    public event Func<IS2SConnection, Exception?, ValueTask>? Disconnected;

    private S2SConnection(
        Guid connectionId,
        Socket socket,
        Stream stream,
        bool isSecure,
        bool isOutgoing,
        string? certificateFingerprint,
        ILogger<S2SConnection> logger)
    {
        ConnectionId = connectionId;
        _socket = socket;
        _stream = stream;
        IsSecure = isSecure;
        IsOutgoing = isOutgoing;
        CertificateFingerprint = certificateFingerprint;
        _logger = logger;

        _reader = PipeReader.Create(_stream);
        _writer = PipeWriter.Create(_stream);
    }

    /// <summary>
    /// Creates a new S2S connection from an accepted socket (incoming connection).
    /// </summary>
    public static async Task<S2SConnection> CreateFromAcceptedSocketAsync(
        Socket socket,
        TlsConfiguration? tlsConfig,
        ILogger<S2SConnection> logger,
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
                logger.LogWarning(ex, "S2S TLS handshake failed for {RemoteEndPoint}", socket.RemoteEndPoint);
                await sslStream.DisposeAsync();
                throw;
            }
        }

        return new S2SConnection(connectionId, socket, stream, isSecure, isOutgoing: false, fingerprint, logger);
    }

    /// <summary>
    /// Creates a new S2S connection by connecting to a remote server (outgoing connection).
    /// </summary>
    public static async Task<S2SConnection> ConnectAsync(
        string host,
        int port,
        TlsConfiguration? tlsConfig,
        ILogger<S2SConnection> logger,
        CancellationToken cancellationToken = default)
    {
        var connectionId = Guid.NewGuid();
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

        try
        {
            await socket.ConnectAsync(host, port, cancellationToken);

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
                    var clientOptions = new SslClientAuthenticationOptions
                    {
                        TargetHost = host,
                        ClientCertificates = new X509CertificateCollection { tlsConfig.Certificate },
                        RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
                        {
                            // For S2S, we typically verify against known server fingerprints
                            // For now, accept all certificates (should be configurable)
                            return true;
                        }
                    };

                    await sslStream.AuthenticateAsClientAsync(clientOptions, cancellationToken);

                    isSecure = true;
                    stream = sslStream;

                    if (sslStream.RemoteCertificate is X509Certificate2 serverCert)
                    {
                        fingerprint = Hugin.Security.CertificateFingerprint.GetSha256Fingerprint(serverCert);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "S2S TLS handshake failed connecting to {Host}:{Port}", host, port);
                    await sslStream.DisposeAsync();
                    socket.Dispose();
                    throw;
                }
            }

            return new S2SConnection(connectionId, socket, stream, isSecure, isOutgoing: true, fingerprint, logger);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
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
            _logger.LogWarning(ex, "S2S connection {ConnectionId} read error", ConnectionId);
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

        // S2S messages can be longer than client messages
        if (line.Length > 8192)
        {
            _logger.LogWarning("S2S connection {ConnectionId} sent line exceeding maximum length", ConnectionId);
            return;
        }

        if (LineReceived is not null)
        {
            await LineReceived(this, line);
        }
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async ValueTask SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogDebug("S2S [{ConnectionId}] >> {Line}", ConnectionId, line);
        var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        await SendAsync(bytes, cancellationToken);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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
