// Hugin IRC Server - SignalR User Event Adapter
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using Hugin.Core.Interfaces;
using Hugin.Server.Api.Hubs;
using Hugin.Server.Api.Models;

namespace Hugin.Server.Services;

/// <summary>
/// Adapter that bridges the <see cref="IUserEventNotifier"/> interface 
/// to the SignalR-based <see cref="IAdminHubService"/>.
/// </summary>
public sealed class SignalRUserEventNotifier : IUserEventNotifier
{
    private readonly IAdminHubService _adminHubService;
    private readonly ILogger<SignalRUserEventNotifier> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalRUserEventNotifier"/> class.
    /// </summary>
    /// <param name="adminHubService">The SignalR admin hub service.</param>
    /// <param name="logger">Logger instance.</param>
    public SignalRUserEventNotifier(IAdminHubService adminHubService, ILogger<SignalRUserEventNotifier> logger)
    {
        _adminHubService = adminHubService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask OnUserConnectedAsync(string nickname, string hostname, string? userId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var userEvent = new UserEventDto
            {
                EventType = "Connected",
                Timestamp = DateTime.UtcNow,
                Nickname = nickname,
                Hostname = hostname,
                UserId = userId
            };

            await _adminHubService.BroadcastUserEventAsync(userEvent, cancellationToken);
            _logger.LogDebug("Broadcasted connect event for {Nickname}", nickname);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast user connected event for {Nickname}", nickname);
        }
    }

    /// <inheritdoc/>
    public async ValueTask OnUserDisconnectedAsync(string nickname, string hostname, string? reason = null, string? userId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var userEvent = new UserEventDto
            {
                EventType = "Disconnected",
                Timestamp = DateTime.UtcNow,
                Nickname = nickname,
                Hostname = hostname,
                UserId = userId,
                Details = reason
            };

            await _adminHubService.BroadcastUserEventAsync(userEvent, cancellationToken);
            _logger.LogDebug("Broadcasted disconnect event for {Nickname}", nickname);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast user disconnected event for {Nickname}", nickname);
        }
    }

    /// <inheritdoc/>
    public async ValueTask OnNickChangeAsync(string oldNickname, string newNickname, string hostname, string? userId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var userEvent = new UserEventDto
            {
                EventType = "NickChange",
                Timestamp = DateTime.UtcNow,
                Nickname = newNickname,
                Hostname = hostname,
                UserId = userId,
                Details = $"Previously: {oldNickname}"
            };

            await _adminHubService.BroadcastUserEventAsync(userEvent, cancellationToken);
            _logger.LogDebug("Broadcasted nick change event: {OldNick} -> {NewNick}", oldNickname, newNickname);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast nick change event for {OldNickname} -> {NewNickname}", oldNickname, newNickname);
        }
    }

    /// <inheritdoc/>
    public async ValueTask OnUserJoinAsync(string nickname, string channel, string hostname, string? userId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var userEvent = new UserEventDto
            {
                EventType = "Join",
                Timestamp = DateTime.UtcNow,
                Nickname = nickname,
                Hostname = hostname,
                UserId = userId,
                Channel = channel
            };

            await _adminHubService.BroadcastUserEventAsync(userEvent, cancellationToken);
            _logger.LogDebug("Broadcasted join event for {Nickname} to {Channel}", nickname, channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast user join event for {Nickname} to {Channel}", nickname, channel);
        }
    }

    /// <inheritdoc/>
    public async ValueTask OnUserPartAsync(string nickname, string channel, string hostname, string? reason = null, string? userId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var userEvent = new UserEventDto
            {
                EventType = "Part",
                Timestamp = DateTime.UtcNow,
                Nickname = nickname,
                Hostname = hostname,
                UserId = userId,
                Channel = channel,
                Details = reason
            };

            await _adminHubService.BroadcastUserEventAsync(userEvent, cancellationToken);
            _logger.LogDebug("Broadcasted part event for {Nickname} from {Channel}", nickname, channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast user part event for {Nickname} from {Channel}", nickname, channel);
        }
    }
}
