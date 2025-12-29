// Hugin IRC Server - SignalR Admin Hub
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Hugin.Server.Api.Hubs;

/// <summary>
/// SignalR hub for real-time admin panel updates.
/// Provides live server statistics, log streaming, and event notifications.
/// </summary>
[Authorize]
public sealed class AdminHub : Hub
{
    private readonly ILogger<AdminHub> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminHub"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public AdminHub(ILogger<AdminHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Called when a client connects to the hub.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var username = Context.User?.Identity?.Name ?? "Unknown";
        _logger.LogInformation("Admin client connected: {ConnectionId} ({Username})", 
            Context.ConnectionId, username);
        
        await Groups.AddToGroupAsync(Context.ConnectionId, "admins");
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a client disconnects from the hub.
    /// </summary>
    /// <param name="exception">Exception if disconnection was due to an error.</param>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var username = Context.User?.Identity?.Name ?? "Unknown";
        
        if (exception != null)
        {
            _logger.LogWarning(exception, "Admin client disconnected with error: {ConnectionId} ({Username})", 
                Context.ConnectionId, username);
        }
        else
        {
            _logger.LogInformation("Admin client disconnected: {ConnectionId} ({Username})", 
                Context.ConnectionId, username);
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "admins");
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribe to log stream for a specific log level or all logs.
    /// </summary>
    /// <param name="minLevel">Minimum log level to receive (Trace, Debug, Info, Warning, Error, Critical).</param>
    public async Task SubscribeToLogs(string minLevel = "Information")
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"logs:{minLevel}");
        _logger.LogDebug("Client {ConnectionId} subscribed to logs at level {Level}", 
            Context.ConnectionId, minLevel);
    }

    /// <summary>
    /// Unsubscribe from log stream.
    /// </summary>
    /// <param name="minLevel">Log level to unsubscribe from.</param>
    public async Task UnsubscribeFromLogs(string minLevel = "Information")
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"logs:{minLevel}");
        _logger.LogDebug("Client {ConnectionId} unsubscribed from logs at level {Level}", 
            Context.ConnectionId, minLevel);
    }

    /// <summary>
    /// Subscribe to real-time statistics updates.
    /// </summary>
    public async Task SubscribeToStats()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "stats");
        _logger.LogDebug("Client {ConnectionId} subscribed to stats", Context.ConnectionId);
    }

    /// <summary>
    /// Unsubscribe from statistics updates.
    /// </summary>
    public async Task UnsubscribeFromStats()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "stats");
        _logger.LogDebug("Client {ConnectionId} unsubscribed from stats", Context.ConnectionId);
    }

    /// <summary>
    /// Subscribe to user connection/disconnection events.
    /// </summary>
    public async Task SubscribeToUserEvents()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "user-events");
        _logger.LogDebug("Client {ConnectionId} subscribed to user events", Context.ConnectionId);
    }

    /// <summary>
    /// Unsubscribe from user events.
    /// </summary>
    public async Task UnsubscribeFromUserEvents()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "user-events");
        _logger.LogDebug("Client {ConnectionId} unsubscribed from user events", Context.ConnectionId);
    }
}
