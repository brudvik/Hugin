// Hugin IRC Server - Real-time Statistics Background Service
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using System.Diagnostics;
using Hugin.Core.Metrics;
using Hugin.Server.Api.Controllers;
using Hugin.Server.Api.Models;

namespace Hugin.Server.Api.Hubs;

/// <summary>
/// Background service that periodically broadcasts server statistics to SignalR clients.
/// </summary>
public sealed class StatsBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StatsBackgroundService> _logger;
    private readonly TimeSpan _broadcastInterval = TimeSpan.FromSeconds(5);
    private readonly Process _currentProcess = Process.GetCurrentProcess();

    /// <summary>
    /// Initializes a new instance of the <see cref="StatsBackgroundService"/> class.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving dependencies.</param>
    /// <param name="logger">Logger instance.</param>
    public StatsBackgroundService(IServiceProvider serviceProvider, ILogger<StatsBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Statistics broadcast service starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BroadcastStatsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error broadcasting statistics");
            }

            await Task.Delay(_broadcastInterval, stoppingToken);
        }

        _logger.LogInformation("Statistics broadcast service stopping");
    }

    private async Task BroadcastStatsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var hubService = scope.ServiceProvider.GetService<IAdminHubService>();
        var statusService = scope.ServiceProvider.GetService<IServerStatusService>();
        var metrics = scope.ServiceProvider.GetService<IrcMetrics>();

        if (hubService == null || statusService == null)
        {
            return;
        }

        _currentProcess.Refresh();
        var status = await statusService.GetStatusAsync(cancellationToken);

        var stats = new RealTimeStatsDto
        {
            Timestamp = DateTime.UtcNow,
            ConnectedUsers = status.ConnectedUsers,
            ChannelCount = status.ChannelCount,
            OperatorsOnline = status.OperatorsOnline,
            MessagesPerSecond = metrics?.GetMessagesPerSecond() ?? 0,
            BytesInPerSecond = metrics?.GetBytesPerSecond(true) ?? 0,
            BytesOutPerSecond = metrics?.GetBytesPerSecond(false) ?? 0,
            MemoryUsageMb = _currentProcess.WorkingSet64 / (1024.0 * 1024.0),
            CpuUsagePercent = GetCpuUsage(),
            ActiveConnections = status.ConnectedUsers,
            PendingOperations = metrics?.GetPendingOperations() ?? 0
        };

        await hubService.BroadcastStatsAsync(stats, cancellationToken);
    }

    private double GetCpuUsage()
    {
        // Simplified CPU usage - in production use performance counters
        try
        {
            return _currentProcess.TotalProcessorTime.TotalMilliseconds / 
                   (Environment.ProcessorCount * _currentProcess.UserProcessorTime.TotalMilliseconds + 1) * 100;
        }
        catch
        {
            return 0;
        }
    }
}
