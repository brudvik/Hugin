// Hugin IRC Server - Serilog Sink for SignalR Log Streaming
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using System.Globalization;
using Hugin.Server.Api.Models;
using Serilog.Core;
using Serilog.Events;

namespace Hugin.Server.Api.Hubs;

/// <summary>
/// Serilog sink that broadcasts log events to SignalR clients.
/// </summary>
public sealed class SignalRSerilogSink : ILogEventSink
{
    private readonly IServiceProvider _serviceProvider;
    private readonly int _maxBufferSize;
    private readonly Queue<LogEntryDto> _buffer;
    private readonly object _bufferLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalRSerilogSink"/> class.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving dependencies.</param>
    /// <param name="maxBufferSize">Maximum number of log entries to buffer.</param>
    public SignalRSerilogSink(IServiceProvider serviceProvider, int maxBufferSize = 1000)
    {
        _serviceProvider = serviceProvider;
        _maxBufferSize = maxBufferSize;
        _buffer = new Queue<LogEntryDto>(maxBufferSize);
    }

    /// <summary>
    /// Gets buffered log entries for initial load.
    /// </summary>
    /// <param name="count">Number of recent entries to retrieve.</param>
    /// <returns>Recent log entries.</returns>
    public IEnumerable<LogEntryDto> GetRecentLogs(int count = 100)
    {
        lock (_bufferLock)
        {
            return _buffer.TakeLast(count).ToList();
        }
    }

    /// <inheritdoc/>
    public void Emit(LogEvent logEvent)
    {
        var logEntry = new LogEntryDto
        {
            Id = Guid.NewGuid().ToString("N"),
            Timestamp = logEvent.Timestamp.UtcDateTime,
            Level = MapLogLevel(logEvent.Level),
            Message = logEvent.RenderMessage(CultureInfo.InvariantCulture),
            Exception = logEvent.Exception?.ToString(),
            Source = GetSourceContext(logEvent),
            Properties = GetProperties(logEvent)
        };

        // Add to buffer
        lock (_bufferLock)
        {
            if (_buffer.Count >= _maxBufferSize)
            {
                _buffer.Dequeue();
            }
            _buffer.Enqueue(logEntry);
        }

        // Broadcast to SignalR (fire and forget)
        _ = BroadcastAsync(logEntry);
    }

    private async Task BroadcastAsync(LogEntryDto logEntry)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var hubService = scope.ServiceProvider.GetService<IAdminHubService>();
            if (hubService != null)
            {
                await hubService.BroadcastLogAsync(logEntry);
            }
        }
        catch
        {
            // Ignore errors during broadcast - logging should not fail
        }
    }

    private static string MapLogLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => "Trace",
        LogEventLevel.Debug => "Debug",
        LogEventLevel.Information => "Information",
        LogEventLevel.Warning => "Warning",
        LogEventLevel.Error => "Error",
        LogEventLevel.Fatal => "Critical",
        _ => "Information"
    };

    private static string? GetSourceContext(LogEvent logEvent)
    {
        if (logEvent.Properties.TryGetValue("SourceContext", out var value) && 
            value is ScalarValue scalar)
        {
            var context = scalar.Value?.ToString();
            // Shorten namespace for display
            if (context != null && context.Contains('.'))
            {
                var parts = context.Split('.');
                return parts[^1]; // Return just the class name
            }
            return context;
        }
        return null;
    }

    private static Dictionary<string, string>? GetProperties(LogEvent logEvent)
    {
        var result = new Dictionary<string, string>();
        
        foreach (var property in logEvent.Properties)
        {
            if (property.Key == "SourceContext")
            {
                continue;
            }
            
            result[property.Key] = property.Value switch
            {
                ScalarValue scalar => scalar.Value?.ToString() ?? "null",
                SequenceValue sequence => $"[{string.Join(", ", sequence.Elements)}]",
                StructureValue structure => $"{{{string.Join(", ", structure.Properties.Select(p => $"{p.Name}: {p.Value}"))}}}",
                DictionaryValue dict => $"{{{string.Join(", ", dict.Elements.Select(e => $"{e.Key}: {e.Value}"))}}}",
                _ => property.Value.ToString(null, CultureInfo.InvariantCulture)
            };
        }

        return result.Count > 0 ? result : null;
    }
}
