using System.Collections.Concurrent;

namespace Hugin.Core.Metrics;

/// <summary>
/// Pre-defined IRC server metrics.
/// </summary>
/// <remarks>
/// These metrics follow Prometheus naming conventions and provide
/// comprehensive observability into the IRC server's operation.
/// </remarks>
public sealed class IrcMetrics
{
    /// <summary>
    /// Histogram buckets for channel member counts.
    /// </summary>
    private static readonly double[] ChannelMemberBuckets = [1.0, 5.0, 10.0, 25.0, 50.0, 100.0, 250.0, 500.0, 1000.0];

    /// <summary>
    /// Histogram buckets for message sizes in bytes.
    /// </summary>
    private static readonly double[] MessageSizeBuckets = [64.0, 128.0, 256.0, 512.0, 1024.0, 2048.0, 4096.0];

    /// <summary>
    /// Histogram buckets for command duration in seconds.
    /// </summary>
    private static readonly double[] CommandDurationBuckets = [0.0001, 0.0005, 0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1.0];

    private readonly MetricsCollector _collector;

    // Rate tracking for real-time stats (sliding window)
    private readonly ConcurrentQueue<(DateTime Time, int Count)> _messageWindow = new();
    private readonly ConcurrentQueue<(DateTime Time, long Bytes, bool Incoming)> _bytesWindow = new();
    private readonly TimeSpan _windowDuration = TimeSpan.FromSeconds(10);
    private long _totalMessagesReceived;
    private long _totalMessagesSent;
    private long _totalBytesIn;
    private long _totalBytesOut;
    private int _pendingOperations;

    // Connection metrics
    private readonly Counter _connectionsTotal;
    private readonly Counter _connectionsClosed;
    private readonly Gauge _connectionsActive;
    private readonly Gauge _connectionsTls;
    private readonly Gauge _connectionsWebSocket;

    // User metrics
    private readonly Gauge _usersRegistered;
    private readonly Gauge _usersInvisible;
    private readonly Gauge _usersOperators;
    private readonly Gauge _usersAway;

    // Channel metrics
    private readonly Gauge _channelsTotal;
    private readonly Gauge _channelMembersTotal;
    private readonly Histogram _channelMemberCount;

    // Message metrics
    private readonly Counter _messagesReceived;
    private readonly Counter _messagesSent;
    private readonly Counter _messagesByCommand;
    private readonly Histogram _messageSizeBytes;

    // Command metrics
    private readonly Counter _commandsProcessed;
    private readonly Counter _commandErrors;
    private readonly Histogram _commandDuration;

    // Authentication metrics
    private readonly Counter _authAttempts;
    private readonly Counter _authSuccesses;
    private readonly Counter _authFailures;

    // Rate limiting metrics
    private readonly Counter _rateLimitHits;

    /// <summary>
    /// Creates a new IRC metrics instance.
    /// </summary>
    public IrcMetrics(MetricsCollector collector)
    {
        _collector = collector;

        // Initialize connection metrics
        _connectionsTotal = collector.GetCounter(
            "hugin_connections_total",
            "Total number of client connections accepted");
        _connectionsClosed = collector.GetCounter(
            "hugin_connections_closed_total",
            "Total number of client connections closed",
            "reason");
        _connectionsActive = collector.GetGauge(
            "hugin_connections_active",
            "Number of currently active connections");
        _connectionsTls = collector.GetGauge(
            "hugin_connections_tls",
            "Number of connections using TLS");
        _connectionsWebSocket = collector.GetGauge(
            "hugin_connections_websocket",
            "Number of WebSocket connections");

        // Initialize user metrics
        _usersRegistered = collector.GetGauge(
            "hugin_users_registered",
            "Number of registered (connected) users");
        _usersInvisible = collector.GetGauge(
            "hugin_users_invisible",
            "Number of users with +i mode");
        _usersOperators = collector.GetGauge(
            "hugin_users_operators",
            "Number of IRC operators");
        _usersAway = collector.GetGauge(
            "hugin_users_away",
            "Number of users marked as away");

        // Initialize channel metrics
        _channelsTotal = collector.GetGauge(
            "hugin_channels_total",
            "Total number of active channels");
        _channelMembersTotal = collector.GetGauge(
            "hugin_channel_members_total",
            "Total channel memberships across all channels");
        _channelMemberCount = collector.GetHistogram(
            "hugin_channel_member_count",
            "Distribution of channel member counts",
            ChannelMemberBuckets);

        // Initialize message metrics
        _messagesReceived = collector.GetCounter(
            "hugin_messages_received_total",
            "Total number of IRC messages received");
        _messagesSent = collector.GetCounter(
            "hugin_messages_sent_total",
            "Total number of IRC messages sent");
        _messagesByCommand = collector.GetCounter(
            "hugin_messages_by_command_total",
            "Messages received by command type",
            "command");
        _messageSizeBytes = collector.GetHistogram(
            "hugin_message_size_bytes",
            "Size of IRC messages in bytes",
            MessageSizeBuckets);

        // Initialize command metrics
        _commandsProcessed = collector.GetCounter(
            "hugin_commands_processed_total",
            "Total number of commands processed",
            "command");
        _commandErrors = collector.GetCounter(
            "hugin_command_errors_total",
            "Total number of command processing errors",
            "command", "error_type");
        _commandDuration = collector.GetHistogram(
            "hugin_command_duration_seconds",
            "Command processing duration in seconds",
            CommandDurationBuckets,
            "command");

        // Initialize authentication metrics
        _authAttempts = collector.GetCounter(
            "hugin_auth_attempts_total",
            "Total authentication attempts",
            "mechanism");
        _authSuccesses = collector.GetCounter(
            "hugin_auth_successes_total",
            "Total successful authentications",
            "mechanism");
        _authFailures = collector.GetCounter(
            "hugin_auth_failures_total",
            "Total failed authentications",
            "mechanism", "reason");

        // Initialize rate limiting metrics
        _rateLimitHits = collector.GetCounter(
            "hugin_rate_limit_hits_total",
            "Total rate limit hits",
            "type");
    }

    // Connection methods
    
    /// <summary>Records a new connection.</summary>
    public void ConnectionAccepted() => _connectionsTotal.Inc();
    
    /// <summary>Records a connection closure.</summary>
    public void ConnectionClosed(string reason) => _connectionsClosed.Inc(reason);
    
    /// <summary>Sets the active connection count.</summary>
    public void SetActiveConnections(int count) => _connectionsActive.Set(count);
    
    /// <summary>Sets the TLS connection count.</summary>
    public void SetTlsConnections(int count) => _connectionsTls.Set(count);
    
    /// <summary>Sets the WebSocket connection count.</summary>
    public void SetWebSocketConnections(int count) => _connectionsWebSocket.Set(count);

    // User methods
    
    /// <summary>Sets the registered user count.</summary>
    public void SetRegisteredUsers(int count) => _usersRegistered.Set(count);
    
    /// <summary>Sets the invisible user count.</summary>
    public void SetInvisibleUsers(int count) => _usersInvisible.Set(count);
    
    /// <summary>Sets the operator count.</summary>
    public void SetOperators(int count) => _usersOperators.Set(count);
    
    /// <summary>Sets the away user count.</summary>
    public void SetAwayUsers(int count) => _usersAway.Set(count);

    // Channel methods
    
    /// <summary>Sets the channel count.</summary>
    public void SetChannelCount(int count) => _channelsTotal.Set(count);
    
    /// <summary>Sets the total channel membership count.</summary>
    public void SetTotalChannelMembers(int count) => _channelMembersTotal.Set(count);
    
    /// <summary>Records a channel's member count.</summary>
    public void ObserveChannelSize(int memberCount) => _channelMemberCount.Observe(memberCount);

    // Message methods
    
    /// <summary>Records a received message.</summary>
    public void MessageReceived(string command, int sizeBytes)
    {
        _messagesReceived.Inc();
        _messagesByCommand.Inc(command);
        _messageSizeBytes.Observe(sizeBytes);
        
        // Track for rate calculation
        Interlocked.Increment(ref _totalMessagesReceived);
        _messageWindow.Enqueue((DateTime.UtcNow, 1));
        RecordBytes(sizeBytes, true);
        CleanupOldEntries();
    }
    
    /// <summary>Records a sent message.</summary>
    public void MessageSent() 
    {
        _messagesSent.Inc();
        Interlocked.Increment(ref _totalMessagesSent);
    }

    /// <summary>Records bytes transferred.</summary>
    public void RecordBytes(long bytes, bool incoming)
    {
        _bytesWindow.Enqueue((DateTime.UtcNow, bytes, incoming));
        if (incoming)
            Interlocked.Add(ref _totalBytesIn, bytes);
        else
            Interlocked.Add(ref _totalBytesOut, bytes);
    }

    /// <summary>Increments pending operations counter.</summary>
    public void IncrementPendingOperations() => Interlocked.Increment(ref _pendingOperations);

    /// <summary>Decrements pending operations counter.</summary>
    public void DecrementPendingOperations() => Interlocked.Decrement(ref _pendingOperations);

    /// <summary>Gets the current pending operations count.</summary>
    public int GetPendingOperations() => _pendingOperations;

    /// <summary>Gets the total messages received.</summary>
    public long GetTotalMessagesReceived() => Interlocked.Read(ref _totalMessagesReceived);

    /// <summary>Gets the total messages sent.</summary>
    public long GetTotalMessagesSent() => Interlocked.Read(ref _totalMessagesSent);

    /// <summary>Calculates messages per second over the sliding window.</summary>
    public double GetMessagesPerSecond()
    {
        CleanupOldEntries();
        var cutoff = DateTime.UtcNow - _windowDuration;
        var count = _messageWindow.Count(m => m.Time > cutoff);
        return count / _windowDuration.TotalSeconds;
    }

    /// <summary>Calculates bytes per second over the sliding window.</summary>
    public double GetBytesPerSecond(bool incoming)
    {
        CleanupOldEntries();
        var cutoff = DateTime.UtcNow - _windowDuration;
        var bytes = _bytesWindow.Where(b => b.Time > cutoff && b.Incoming == incoming).Sum(b => b.Bytes);
        return bytes / _windowDuration.TotalSeconds;
    }

    private void CleanupOldEntries()
    {
        var cutoff = DateTime.UtcNow - _windowDuration - TimeSpan.FromSeconds(5);
        
        while (_messageWindow.TryPeek(out var msg) && msg.Time < cutoff)
            _messageWindow.TryDequeue(out _);
        
        while (_bytesWindow.TryPeek(out var b) && b.Time < cutoff)
            _bytesWindow.TryDequeue(out _);
    }

    // Command methods
    
    /// <summary>Records a processed command with duration.</summary>
    public void CommandProcessed(string command, double durationSeconds)
    {
        _commandsProcessed.Inc(command);
        _commandDuration.Observe(durationSeconds, command);
    }
    
    /// <summary>Records a command error.</summary>
    public void CommandError(string command, string errorType)
    {
        _commandErrors.Inc(command, errorType);
    }

    // Authentication methods
    
    /// <summary>Records an authentication attempt.</summary>
    public void AuthAttempt(string mechanism) => _authAttempts.Inc(mechanism);
    
    /// <summary>Records a successful authentication.</summary>
    public void AuthSuccess(string mechanism) => _authSuccesses.Inc(mechanism);
    
    /// <summary>Records a failed authentication.</summary>
    public void AuthFailure(string mechanism, string reason) => _authFailures.Inc(mechanism, reason);

    // Rate limiting methods
    
    /// <summary>Records a rate limit hit.</summary>
    public void RateLimitHit(string type) => _rateLimitHits.Inc(type);
}
