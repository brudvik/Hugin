using Hugin.Core.Entities;
using Hugin.Core.ValueObjects;
using Hugin.Protocol.S2S;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hugin.Network.S2S;

/// <summary>
/// Configuration for S2S reconnection behavior.
/// </summary>
public sealed class S2SReconnectConfiguration
{
    /// <summary>
    /// Gets or sets whether automatic reconnection is enabled.
    /// </summary>
    public bool EnableAutoReconnect { get; set; } = true;

    /// <summary>
    /// Gets or sets the initial delay before attempting reconnection (in seconds).
    /// </summary>
    public int InitialDelaySeconds { get; set; } = 15;

    /// <summary>
    /// Gets or sets the maximum delay between reconnection attempts (in seconds).
    /// </summary>
    public int MaxDelaySeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets the backoff multiplier for exponential backoff.
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets the maximum number of reconnection attempts (0 = infinite).
    /// </summary>
    public int MaxReconnectAttempts { get; set; }
}

/// <summary>
/// Manages netsplit detection and automatic reconnection for S2S links.
/// </summary>
public sealed class NetsplitHandler : IDisposable
{
    private readonly IServerLinkManager _linkManager;
    private readonly IS2SConnector _connector;
    private readonly S2SReconnectConfiguration _config;
    private readonly ILogger<NetsplitHandler> _logger;
    private readonly Dictionary<string, ReconnectState> _reconnectStates = new();
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Event raised when a netsplit is detected.
    /// </summary>
    public event EventHandler<NetsplitEventArgs>? NetsplitDetected;

    /// <summary>
    /// Event raised when a netsplit is healed (servers reconnected).
    /// </summary>
    public event EventHandler<NetsplitEventArgs>? NetsplitHealed;

    /// <summary>
    /// Creates a new netsplit handler.
    /// </summary>
    public NetsplitHandler(
        IServerLinkManager linkManager,
        IS2SConnector connector,
        S2SReconnectConfiguration config,
        ILogger<NetsplitHandler> logger)
    {
        _linkManager = linkManager;
        _connector = connector;
        _config = config;
        _logger = logger;

        // Subscribe to server link events
        _linkManager.ServerUnlinked += OnServerUnlinked;
        _linkManager.ServerLinked += OnServerLinked;
    }

    private void OnServerUnlinked(object? sender, ServerUnlinkedEventArgs args)
    {
        var server = args.Server;
        var reason = args.Reason;
        var cascadeRemoved = args.CascadeUnlinked?.ToList() ?? new List<LinkedServer>();

        _logger.LogWarning("Netsplit detected: {ServerName} ({ServerSid}) disconnected: {Reason}. " +
                          "{CascadeCount} servers also lost.",
            server.Id.Name, server.Id.Sid, reason, cascadeRemoved.Count);

        // Raise netsplit event
        NetsplitDetected?.Invoke(this, new NetsplitEventArgs(
            server,
            cascadeRemoved,
            reason,
            DateTimeOffset.UtcNow));

        // Only attempt reconnection for direct links with AutoConnect
        if (server.IsDirect && _config.EnableAutoReconnect)
        {
            ScheduleReconnect(server);
        }
    }

    private void OnServerLinked(object? sender, ServerLinkedEventArgs args)
    {
        var server = args.Server;

        // Cancel any pending reconnect for this server
        lock (_lock)
        {
            if (_reconnectStates.TryGetValue(server.Id.Name, out var state))
            {
                state.Cancel();
                _reconnectStates.Remove(server.Id.Name);

                _logger.LogInformation("Netsplit healed: {ServerName} ({ServerSid}) reconnected after {Attempts} attempts.",
                    server.Id.Name, server.Id.Sid, state.Attempts);

                NetsplitHealed?.Invoke(this, new NetsplitEventArgs(
                    server,
                    new List<LinkedServer>(),
                    "Reconnected",
                    DateTimeOffset.UtcNow));
            }
        }
    }

    private void ScheduleReconnect(LinkedServer server)
    {
        lock (_lock)
        {
            if (_reconnectStates.ContainsKey(server.Id.Name))
            {
                // Already reconnecting
                return;
            }

            var state = new ReconnectState(server, _config.InitialDelaySeconds);
            _reconnectStates[server.Id.Name] = state;

            _ = ReconnectLoopAsync(state, _cts.Token);
        }
    }

    private async Task ReconnectLoopAsync(ReconnectState state, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !state.IsCancelled)
        {
            // Check max attempts
            if (_config.MaxReconnectAttempts > 0 && state.Attempts >= _config.MaxReconnectAttempts)
            {
                _logger.LogWarning("Max reconnection attempts ({Max}) reached for {ServerName}. Giving up.",
                    _config.MaxReconnectAttempts, state.Server.Id.Name);
                
                lock (_lock)
                {
                    _reconnectStates.Remove(state.Server.Id.Name);
                }
                return;
            }

            _logger.LogInformation("Attempting to reconnect to {ServerName} in {Delay} seconds (attempt {Attempt})...",
                state.Server.Id.Name, state.CurrentDelaySeconds, state.Attempts + 1);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(state.CurrentDelaySeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (state.IsCancelled)
            {
                return;
            }

            state.Attempts++;

            try
            {
                // Attempt connection
                // Note: The actual connection config would need to be retrieved from configuration
                var success = await _connector.TryConnectAsync(
                    state.Server.Id.Name,
                    state.Server.Id.Name, // Host would be from config
                    6900, // Port would be from config
                    "link-password", // Password would be from config
                    cancellationToken);

                if (success)
                {
                    _logger.LogInformation("Successfully reconnected to {ServerName}.", state.Server.Id.Name);
                    // The ServerLinked event will clean up the state
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reconnect to {ServerName}.", state.Server.Id.Name);
            }

            // Exponential backoff
            state.CurrentDelaySeconds = (int)Math.Min(
                state.CurrentDelaySeconds * _config.BackoffMultiplier,
                _config.MaxDelaySeconds);
        }
    }

    /// <summary>
    /// Cancels reconnection attempts for a specific server.
    /// </summary>
    public void CancelReconnect(string serverName)
    {
        lock (_lock)
        {
            if (_reconnectStates.TryGetValue(serverName, out var state))
            {
                state.Cancel();
                _reconnectStates.Remove(serverName);
                _logger.LogInformation("Cancelled reconnection attempts for {ServerName}.", serverName);
            }
        }
    }

    /// <summary>
    /// Gets the current reconnection status for all servers.
    /// </summary>
    public IEnumerable<ReconnectStatus> GetReconnectStatus()
    {
        lock (_lock)
        {
            return _reconnectStates.Values
                .Select(s => new ReconnectStatus(
                    s.Server.Id.Name,
                    s.Attempts,
                    s.CurrentDelaySeconds,
                    s.StartedAt))
                .ToList();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _linkManager.ServerUnlinked -= OnServerUnlinked;
        _linkManager.ServerLinked -= OnServerLinked;
    }

    private sealed class ReconnectState : IDisposable
    {
        public LinkedServer Server { get; }
        public int Attempts { get; set; }
        public int CurrentDelaySeconds { get; set; }
        public DateTimeOffset StartedAt { get; }
        public bool IsCancelled { get; private set; }
        private readonly CancellationTokenSource _cts = new();

        public ReconnectState(LinkedServer server, int initialDelaySeconds)
        {
            Server = server;
            CurrentDelaySeconds = initialDelaySeconds;
            StartedAt = DateTimeOffset.UtcNow;
        }

        public void Cancel()
        {
            IsCancelled = true;
            _cts.Cancel();
        }

        public void Dispose()
        {
            _cts.Dispose();
        }
    }
}

/// <summary>
/// Status of a reconnection attempt.
/// </summary>
/// <param name="ServerName">The server name.</param>
/// <param name="Attempts">Number of reconnection attempts so far.</param>
/// <param name="NextAttemptSeconds">Seconds until next attempt.</param>
/// <param name="StartedAt">When reconnection attempts started.</param>
public record ReconnectStatus(
    string ServerName,
    int Attempts,
    int NextAttemptSeconds,
    DateTimeOffset StartedAt);

/// <summary>
/// Event arguments for netsplit events.
/// </summary>
public sealed class NetsplitEventArgs : EventArgs
{
    /// <summary>
    /// Gets the server that was disconnected.
    /// </summary>
    public LinkedServer Server { get; }

    /// <summary>
    /// Gets servers that were also lost due to the split (cascade).
    /// </summary>
    public IReadOnlyList<LinkedServer> CascadeLostServers { get; }

    /// <summary>
    /// Gets the reason for the netsplit.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets when the netsplit occurred.
    /// </summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>
    /// Creates new netsplit event arguments.
    /// </summary>
    public NetsplitEventArgs(
        LinkedServer server,
        IReadOnlyList<LinkedServer> cascadeLostServers,
        string reason,
        DateTimeOffset occurredAt)
    {
        Server = server;
        CascadeLostServers = cascadeLostServers;
        Reason = reason;
        OccurredAt = occurredAt;
    }
}
