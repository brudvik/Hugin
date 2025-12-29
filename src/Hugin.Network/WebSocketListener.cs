using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Hugin.Security;
using Microsoft.Extensions.Logging;

namespace Hugin.Network;

/// <summary>
/// WebSocket listener for IRC client connections via HTTP upgrade.
/// </summary>
/// <remarks>
/// This listener accepts HTTP connections and upgrades them to WebSocket
/// connections for web-based IRC clients like KiwiIRC, The Lounge, etc.
/// The WebSocket path is typically /webirc or configurable.
/// </remarks>
public sealed partial class WebSocketListener : IAsyncDisposable
{
    private readonly HttpListener _httpListener;
    private readonly TlsConfiguration? _tlsConfig;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger<WebSocketListener> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _path;
    private readonly string[] _allowedOrigins;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    /// <summary>
    /// Event raised when a new WebSocket connection is accepted.
    /// </summary>
    public event Func<WebSocketConnection, Task>? ConnectionAccepted;

    /// <summary>
    /// Gets whether the listener is currently accepting connections.
    /// </summary>
    public bool IsListening => _httpListener.IsListening;

    /// <summary>
    /// Creates a new WebSocket listener.
    /// </summary>
    /// <param name="port">The port to listen on.</param>
    /// <param name="path">The WebSocket path (e.g., "/webirc").</param>
    /// <param name="useTls">Whether to use HTTPS/WSS.</param>
    /// <param name="tlsConfig">TLS configuration for secure connections.</param>
    /// <param name="rateLimiter">Rate limiter for connection throttling.</param>
    /// <param name="allowedOrigins">Allowed CORS origins (empty for all).</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    public WebSocketListener(
        int port,
        string path,
        bool useTls,
        TlsConfiguration? tlsConfig,
        RateLimiter rateLimiter,
        string[] allowedOrigins,
        ILoggerFactory loggerFactory)
    {
        _tlsConfig = tlsConfig;
        _rateLimiter = rateLimiter;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<WebSocketListener>();
        _path = path.StartsWith('/') ? path : "/" + path;
        _allowedOrigins = allowedOrigins;

        _httpListener = new HttpListener();
        var scheme = useTls ? "https" : "http";
        _httpListener.Prefixes.Add($"{scheme}://+:{port}{_path}/");
    }

    /// <summary>
    /// Starts listening for WebSocket connections.
    /// </summary>
    public void Start()
    {
        if (_httpListener.IsListening)
        {
            throw new InvalidOperationException("WebSocket listener is already started");
        }

        _httpListener.Start();
        _cts = new CancellationTokenSource();
        _acceptTask = AcceptLoopAsync(_cts.Token);

        _logger.LogInformation(
            "WebSocket listening on port {Port} at path {Path}",
            _httpListener.Prefixes.First(),
            _path);
    }

    /// <summary>
    /// Stops listening for connections.
    /// </summary>
    public async Task StopAsync()
    {
        if (!_httpListener.IsListening)
        {
            return;
        }

        _cts?.Cancel();

        try
        {
            _httpListener.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping WebSocket listener");
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
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _httpListener.IsListening)
        {
            try
            {
                var context = await _httpListener.GetContextAsync();

                // Handle in background
                _ = HandleRequestAsync(context, cancellationToken);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error accepting WebSocket connection");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // Check if this is a WebSocket request
            if (!request.IsWebSocketRequest)
            {
                // Return a simple info page for non-WebSocket requests
                response.StatusCode = 200;
                response.ContentType = "text/plain";
                var infoBytes = Encoding.UTF8.GetBytes("Hugin IRC Server - WebSocket endpoint\r\nConnect using a WebSocket client.");
                await response.OutputStream.WriteAsync(infoBytes, cancellationToken);
                response.Close();
                return;
            }

            // Get remote IP for rate limiting
            var remoteIp = request.RemoteEndPoint?.Address;

            // Check rate limit
            if (remoteIp is not null && !_rateLimiter.TryConsumeConnection(remoteIp))
            {
                _logger.LogWarning("WebSocket connection from {RemoteIp} rate limited", remoteIp);
                response.StatusCode = 429;
                response.Close();
                return;
            }

            // Check CORS origin if configured
            if (_allowedOrigins.Length > 0)
            {
                var origin = request.Headers["Origin"];
                if (origin is not null && !IsOriginAllowed(origin))
                {
                    _logger.LogWarning("WebSocket connection from {Origin} blocked by CORS", origin);
                    response.StatusCode = 403;
                    response.Close();
                    return;
                }
            }

            // Accept the WebSocket connection
            var wsContext = await context.AcceptWebSocketAsync(
                subProtocol: null,
                keepAliveInterval: TimeSpan.FromSeconds(30));

            var connectionId = Guid.NewGuid();
            var webSocket = wsContext.WebSocket;

            var connection = new WebSocketConnection(
                connectionId,
                webSocket,
                request.RemoteEndPoint,
                request.IsSecureConnection,
                certificateFingerprint: null, // Client certs not typically used with WebSocket
                _loggerFactory.CreateLogger<WebSocketConnection>());

            _logger.LogInformation(
                "WebSocket connection {ConnectionId} accepted from {RemoteEndPoint}",
                connectionId,
                request.RemoteEndPoint);

            // Raise the connection accepted event
            if (ConnectionAccepted is not null)
            {
                await ConnectionAccepted(connection);
            }
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "WebSocket handshake failed");
            response.StatusCode = 400;
            response.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling WebSocket request");
            response.StatusCode = 500;
            response.Close();
        }
    }

    private bool IsOriginAllowed(string origin)
    {
        foreach (var allowed in _allowedOrigins)
        {
            if (allowed == "*")
            {
                return true;
            }

            // Support wildcard subdomains (e.g., *.example.com)
            if (allowed.StartsWith("*.", StringComparison.Ordinal))
            {
                var domain = allowed[2..];
                if (origin.EndsWith(domain, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (origin.Equals(allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _httpListener.Close();
        _cts?.Dispose();
    }
}
