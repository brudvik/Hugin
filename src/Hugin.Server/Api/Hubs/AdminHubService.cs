// Hugin IRC Server - Admin Hub Broadcasting Service
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using Hugin.Server.Api.Models;
using Microsoft.AspNetCore.SignalR;

namespace Hugin.Server.Api.Hubs;

/// <summary>
/// Service interface for broadcasting messages to admin clients via SignalR.
/// </summary>
public interface IAdminHubService
{
    /// <summary>
    /// Broadcasts a log entry to subscribed clients.
    /// </summary>
    /// <param name="logEntry">The log entry to broadcast.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task BroadcastLogAsync(LogEntryDto logEntry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts server statistics to subscribed clients.
    /// </summary>
    /// <param name="stats">The statistics to broadcast.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task BroadcastStatsAsync(RealTimeStatsDto stats, CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts a user event (connect/disconnect/nick change).
    /// </summary>
    /// <param name="userEvent">The user event details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task BroadcastUserEventAsync(UserEventDto userEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts a server notification to all admin clients.
    /// </summary>
    /// <param name="notification">The notification to broadcast.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task BroadcastNotificationAsync(AdminNotificationDto notification, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of the admin hub broadcasting service.
/// </summary>
public sealed class AdminHubService : IAdminHubService
{
    private readonly IHubContext<AdminHub> _hubContext;
    private readonly ILogger<AdminHubService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminHubService"/> class.
    /// </summary>
    /// <param name="hubContext">SignalR hub context.</param>
    /// <param name="logger">Logger instance.</param>
    public AdminHubService(IHubContext<AdminHub> hubContext, ILogger<AdminHubService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task BroadcastLogAsync(LogEntryDto logEntry, CancellationToken cancellationToken = default)
    {
        try
        {
            // Broadcast to appropriate log level groups
            var tasks = new List<Task>();

            // Critical logs go to all log subscribers
            if (logEntry.Level == "Critical")
            {
                tasks.Add(_hubContext.Clients.Groups("logs:Critical", "logs:Error", "logs:Warning", 
                    "logs:Information", "logs:Debug", "logs:Trace")
                    .SendAsync("ReceiveLog", logEntry, cancellationToken));
            }
            else if (logEntry.Level == "Error")
            {
                tasks.Add(_hubContext.Clients.Groups("logs:Error", "logs:Warning", 
                    "logs:Information", "logs:Debug", "logs:Trace")
                    .SendAsync("ReceiveLog", logEntry, cancellationToken));
            }
            else if (logEntry.Level == "Warning")
            {
                tasks.Add(_hubContext.Clients.Groups("logs:Warning", "logs:Information", 
                    "logs:Debug", "logs:Trace")
                    .SendAsync("ReceiveLog", logEntry, cancellationToken));
            }
            else if (logEntry.Level == "Information")
            {
                tasks.Add(_hubContext.Clients.Groups("logs:Information", "logs:Debug", "logs:Trace")
                    .SendAsync("ReceiveLog", logEntry, cancellationToken));
            }
            else if (logEntry.Level == "Debug")
            {
                tasks.Add(_hubContext.Clients.Groups("logs:Debug", "logs:Trace")
                    .SendAsync("ReceiveLog", logEntry, cancellationToken));
            }
            else // Trace
            {
                tasks.Add(_hubContext.Clients.Group("logs:Trace")
                    .SendAsync("ReceiveLog", logEntry, cancellationToken));
            }

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast log entry");
        }
    }

    /// <inheritdoc/>
    public async Task BroadcastStatsAsync(RealTimeStatsDto stats, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.Group("stats")
                .SendAsync("ReceiveStats", stats, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast stats");
        }
    }

    /// <inheritdoc/>
    public async Task BroadcastUserEventAsync(UserEventDto userEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.Group("user-events")
                .SendAsync("ReceiveUserEvent", userEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast user event");
        }
    }

    /// <inheritdoc/>
    public async Task BroadcastNotificationAsync(AdminNotificationDto notification, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.Group("admins")
                .SendAsync("ReceiveNotification", notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast notification");
        }
    }
}
