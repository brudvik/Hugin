using System.Net;
using System.Text;
using Hugin.Core.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hugin.Network;

/// <summary>
/// Configuration options for the metrics endpoint.
/// </summary>
public sealed class MetricsOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "Hugin:Metrics";

    /// <summary>
    /// Gets or sets whether the metrics endpoint is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the port to listen on.
    /// </summary>
    public int Port { get; set; } = 9090;

    /// <summary>
    /// Gets or sets the path for the metrics endpoint.
    /// </summary>
    public string Path { get; set; } = "/metrics";

    /// <summary>
    /// Gets or sets allowed IP addresses (empty for all).
    /// </summary>
    public List<string> AllowedIps { get; set; } = new();
}

/// <summary>
/// HTTP server that exposes Prometheus metrics.
/// </summary>
public sealed class MetricsServer : IHostedService, IAsyncDisposable
{
    private readonly MetricsCollector _collector;
    private readonly MetricsOptions _options;
    private readonly ILogger<MetricsServer> _logger;
    private readonly HttpListener _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    /// <summary>
    /// Creates a new metrics server.
    /// </summary>
    public MetricsServer(
        MetricsCollector collector,
        IOptions<MetricsOptions> options,
        ILogger<MetricsServer> logger)
    {
        _collector = collector;
        _options = options.Value;
        _logger = logger;
        _listener = new HttpListener();
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Metrics endpoint is disabled");
            return Task.CompletedTask;
        }

        var path = _options.Path.StartsWith('/') ? _options.Path : "/" + _options.Path;
        if (!path.EndsWith('/'))
        {
            path += "/";
        }

        _listener.Prefixes.Add($"http://+:{_options.Port}{path}");

        try
        {
            _listener.Start();
            _cts = new CancellationTokenSource();
            _listenTask = ListenAsync(_cts.Token);

            _logger.LogInformation(
                "Metrics endpoint listening on http://+:{Port}{Path}",
                _options.Port,
                _options.Path);
        }
        catch (HttpListenerException ex)
        {
            _logger.LogError(ex, "Failed to start metrics endpoint on port {Port}", _options.Port);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();

        try
        {
            _listener.Stop();
        }
        catch
        {
            // Ignore
        }

        if (_listenTask is not null)
        {
            try
            {
                await _listenTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = HandleRequestAsync(context);
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
                _logger.LogWarning(ex, "Error handling metrics request");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // Check IP allowlist if configured
            if (_options.AllowedIps.Count > 0)
            {
                var remoteIp = request.RemoteEndPoint?.Address?.ToString();
                if (remoteIp is null || !IsIpAllowed(remoteIp))
                {
                    response.StatusCode = 403;
                    response.Close();
                    return;
                }
            }

            // Only allow GET requests
            if (request.HttpMethod != "GET")
            {
                response.StatusCode = 405;
                response.Close();
                return;
            }

            // Export metrics
            var metrics = _collector.Export();
            var bytes = Encoding.UTF8.GetBytes(metrics);

            response.StatusCode = 200;
            response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
            response.ContentLength64 = bytes.Length;

            await response.OutputStream.WriteAsync(bytes);
            response.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error exporting metrics");
            response.StatusCode = 500;
            response.Close();
        }
    }

    private bool IsIpAllowed(string remoteIp)
    {
        foreach (var allowed in _options.AllowedIps)
        {
            if (allowed == "*" || allowed.Equals(remoteIp, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Check for localhost variants
            if (allowed == "localhost" &&
                (remoteIp == "127.0.0.1" || remoteIp == "::1"))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _listener.Close();
        _cts?.Dispose();
    }
}
