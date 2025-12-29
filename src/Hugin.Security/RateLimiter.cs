using System.Collections.Concurrent;
using System.Net;

namespace Hugin.Security;

/// <summary>
/// Token bucket rate limiter for connection and command throttling.
/// </summary>
public sealed class RateLimiter : IDisposable
{
    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new();
    private readonly RateLimitConfiguration _config;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    /// <summary>
    /// Creates a new rate limiter.
    /// </summary>
    /// <param name="config">The rate limit configuration.</param>
    public RateLimiter(RateLimitConfiguration config)
    {
        _config = config;
        _cleanupTimer = new Timer(CleanupExpiredBuckets, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Attempts to consume a token for connection attempts.
    /// </summary>
    public bool TryConsumeConnection(IPAddress address)
    {
        var key = $"conn:{GetAddressKey(address)}";
        var bucket = _buckets.GetOrAdd(key, _ => new TokenBucket(
            _config.ConnectionsPerSecond,
            _config.ConnectionBurstSize));

        return bucket.TryConsume();
    }

    /// <summary>
    /// Attempts to consume a token for command execution.
    /// </summary>
    public bool TryConsumeCommand(Guid connectionId, string command)
    {
        var key = $"cmd:{connectionId}";
        var bucket = _buckets.GetOrAdd(key, _ => new TokenBucket(
            _config.CommandsPerSecond,
            _config.CommandBurstSize));

        return bucket.TryConsume();
    }

    /// <summary>
    /// Attempts to consume tokens for message sending.
    /// </summary>
    public bool TryConsumeMessage(Guid connectionId, int byteCount)
    {
        var key = $"msg:{connectionId}";
        var bucket = _buckets.GetOrAdd(key, _ => new TokenBucket(
            _config.MessagesPerSecond,
            _config.MessageBurstSize));

        return bucket.TryConsume();
    }

    /// <summary>
    /// Checks if an address is currently throttled.
    /// </summary>
    public bool IsThrottled(IPAddress address)
    {
        var key = $"conn:{GetAddressKey(address)}";
        if (_buckets.TryGetValue(key, out var bucket))
        {
            return !bucket.HasTokens;
        }
        return false;
    }

    /// <summary>
    /// Gets remaining tokens for a connection.
    /// </summary>
    public int GetRemainingTokens(Guid connectionId)
    {
        var key = $"cmd:{connectionId}";
        if (_buckets.TryGetValue(key, out var bucket))
        {
            return (int)bucket.CurrentTokens;
        }
        return _config.CommandBurstSize;
    }

    private static string GetAddressKey(IPAddress address)
    {
        // For IPv6, use /64 prefix
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            // Zero out the last 8 bytes (host part)
            for (int i = 8; i < 16; i++)
            {
                bytes[i] = 0;
            }
            return new IPAddress(bytes).ToString();
        }
        return address.ToString();
    }

    private void CleanupExpiredBuckets(object? state)
    {
        var expiredKeys = _buckets
            .Where(kvp => kvp.Value.IsExpired)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _buckets.TryRemove(key, out _);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cleanupTimer.Dispose();
    }
}

/// <summary>
/// Token bucket implementation for rate limiting.
/// </summary>
public sealed class TokenBucket
{
    private readonly double _refillRate; // tokens per second
    private readonly double _capacity;
    private double _tokens;
    private DateTimeOffset _lastRefill;
    private readonly object _lock = new();

    /// <summary>
    /// Gets the current number of tokens in the bucket.
    /// </summary>
    public double CurrentTokens => _tokens;

    /// <summary>
    /// Gets whether the bucket has at least one token available.
    /// </summary>
    public bool HasTokens => _tokens >= 1.0;

    /// <summary>
    /// Gets whether the bucket has expired (no activity for 5 minutes).
    /// </summary>
    public bool IsExpired => (DateTimeOffset.UtcNow - _lastRefill).TotalMinutes > 5;

    /// <summary>
    /// Creates a new token bucket.
    /// </summary>
    /// <param name="tokensPerSecond">Rate at which tokens are refilled.</param>
    /// <param name="burstSize">Maximum capacity of the bucket.</param>
    public TokenBucket(double tokensPerSecond, int burstSize)
    {
        _refillRate = tokensPerSecond;
        _capacity = burstSize;
        _tokens = burstSize;
        _lastRefill = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Attempts to consume tokens from the bucket.
    /// </summary>
    /// <param name="tokens">Number of tokens to consume.</param>
    /// <returns>True if tokens were available and consumed; otherwise false.</returns>
    public bool TryConsume(double tokens = 1.0)
    {
        lock (_lock)
        {
            Refill();

            if (_tokens >= tokens)
            {
                _tokens -= tokens;
                return true;
            }

            return false;
        }
    }

    private void Refill()
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = (now - _lastRefill).TotalSeconds;
        _lastRefill = now;

        _tokens = Math.Min(_capacity, _tokens + (elapsed * _refillRate));
    }
}

/// <summary>
/// Configuration for rate limiting.
/// </summary>
public sealed class RateLimitConfiguration
{
    /// <summary>
    /// Maximum connections per second from a single IP.
    /// </summary>
    public double ConnectionsPerSecond { get; set; } = 2;

    /// <summary>
    /// Maximum burst of connections from a single IP.
    /// </summary>
    public int ConnectionBurstSize { get; set; } = 5;

    /// <summary>
    /// Maximum commands per second per connection.
    /// </summary>
    public double CommandsPerSecond { get; set; } = 5;

    /// <summary>
    /// Maximum burst of commands per connection.
    /// </summary>
    public int CommandBurstSize { get; set; } = 20;

    /// <summary>
    /// Maximum messages per second per connection.
    /// </summary>
    public double MessagesPerSecond { get; set; } = 3;

    /// <summary>
    /// Maximum burst of messages per connection.
    /// </summary>
    public int MessageBurstSize { get; set; } = 10;

    /// <summary>
    /// IP addresses exempt from rate limiting.
    /// </summary>
    public HashSet<string> ExemptAddresses { get; set; } = new() { "127.0.0.1", "::1" };
}
