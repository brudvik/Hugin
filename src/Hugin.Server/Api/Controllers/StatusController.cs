// Hugin IRC Server - Server Status Controller
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using System.Diagnostics;
using Hugin.Server.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hugin.Server.Api.Controllers;

/// <summary>
/// Server status and statistics endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class StatusController : ControllerBase
{
    private readonly IServerStatusService _statusService;
    private readonly ILogger<StatusController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusController"/> class.
    /// </summary>
    public StatusController(
        IServerStatusService statusService,
        ILogger<StatusController> logger)
    {
        _statusService = statusService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current server status.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ServerStatusDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var status = await _statusService.GetStatusAsync(cancellationToken);
        return Ok(ApiResponse<ServerStatusDto>.Ok(status));
    }

    /// <summary>
    /// Gets server statistics.
    /// </summary>
    [HttpGet("statistics")]
    [ProducesResponseType(typeof(ApiResponse<ServerStatisticsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatistics(CancellationToken cancellationToken)
    {
        var stats = await _statusService.GetStatisticsAsync(cancellationToken);
        return Ok(ApiResponse<ServerStatisticsDto>.Ok(stats));
    }

    /// <summary>
    /// Gets system health information.
    /// </summary>
    [HttpGet("health")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HealthCheckDto), StatusCodes.Status200OK)]
    public IActionResult GetHealth()
    {
        return Ok(new HealthCheckDto
        {
            Status = "healthy",
            Timestamp = DateTimeOffset.UtcNow,
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0"
        });
    }

    /// <summary>
    /// Restarts the IRC server.
    /// </summary>
    [HttpPost("restart")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Restart(CancellationToken cancellationToken)
    {
        _logger.LogWarning("Server restart requested by {User}", User.Identity?.Name);
        await _statusService.RestartAsync(cancellationToken);
        return Ok(ApiResponse.Ok("Server restart initiated"));
    }

    /// <summary>
    /// Shuts down the IRC server.
    /// </summary>
    [HttpPost("shutdown")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Shutdown([FromQuery] string? reason, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Server shutdown requested by {User}: {Reason}", User.Identity?.Name, reason);
        await _statusService.ShutdownAsync(reason ?? "Server shutting down", cancellationToken);
        return Ok(ApiResponse.Ok("Server shutdown initiated"));
    }

    /// <summary>
    /// Reloads server configuration.
    /// </summary>
    [HttpPost("reload")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reload(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Configuration reload requested by {User}", User.Identity?.Name);
        await _statusService.ReloadConfigurationAsync(cancellationToken);
        return Ok(ApiResponse.Ok("Configuration reloaded"));
    }
}

/// <summary>
/// Health check response.
/// </summary>
public sealed class HealthCheckDto
{
    /// <summary>
    /// Health status.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Current timestamp.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Server version.
    /// </summary>
    public required string Version { get; init; }
}

/// <summary>
/// Service for server status information.
/// </summary>
public interface IServerStatusService
{
    /// <summary>
    /// Gets the current server status.
    /// </summary>
    Task<ServerStatusDto> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets server statistics.
    /// </summary>
    Task<ServerStatisticsDto> GetStatisticsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Restarts the server.
    /// </summary>
    Task RestartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Shuts down the server.
    /// </summary>
    Task ShutdownAsync(string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Reloads configuration.
    /// </summary>
    Task ReloadConfigurationAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Server status service implementation.
/// </summary>
public sealed class ServerStatusService : IServerStatusService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ServerStatusService> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private static readonly DateTimeOffset StartTime = DateTimeOffset.UtcNow;
    private static long _totalConnections;
    private static long _totalMessages;
    private static int _peakUsers;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerStatusService"/> class.
    /// </summary>
    public ServerStatusService(
        IConfiguration configuration,
        ILogger<ServerStatusService> logger,
        IHostApplicationLifetime lifetime)
    {
        _configuration = configuration;
        _logger = logger;
        _lifetime = lifetime;
    }

    /// <inheritdoc />
    public Task<ServerStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        var process = Process.GetCurrentProcess();

        var status = new ServerStatusDto
        {
            ServerName = _configuration["Hugin:Server:Name"] ?? "hugin.local",
            NetworkName = _configuration["Hugin:Server:NetworkName"] ?? "HuginNet",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Uptime = DateTimeOffset.UtcNow - StartTime,
            IsRunning = true,
            ConnectedUsers = 0, // TODO: Get from user manager
            ChannelCount = 0, // TODO: Get from channel manager
            OperatorsOnline = 0, // TODO: Get from user manager
            Statistics = new ServerStatisticsDto
            {
                TotalConnections = Interlocked.Read(ref _totalConnections),
                TotalMessages = Interlocked.Read(ref _totalMessages),
                PeakUsers = _peakUsers,
                MemoryUsageBytes = process.WorkingSet64,
                CpuUsagePercent = 0, // TODO: Calculate CPU usage
                MessagesPerSecond = 0 // TODO: Calculate messages per second
            }
        };

        return Task.FromResult(status);
    }

    /// <inheritdoc />
    public Task<ServerStatisticsDto> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        var process = Process.GetCurrentProcess();

        var stats = new ServerStatisticsDto
        {
            TotalConnections = Interlocked.Read(ref _totalConnections),
            TotalMessages = Interlocked.Read(ref _totalMessages),
            PeakUsers = _peakUsers,
            MemoryUsageBytes = process.WorkingSet64,
            CpuUsagePercent = 0,
            MessagesPerSecond = 0
        };

        return Task.FromResult(stats);
    }

    /// <inheritdoc />
    public Task RestartAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning("Server restart requested");
        // TODO: Implement graceful restart
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShutdownAsync(string reason, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Server shutdown requested: {Reason}", reason);
        _lifetime.StopApplication();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReloadConfigurationAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Configuration reload requested");
        // TODO: Implement configuration reload
        return Task.CompletedTask;
    }

    /// <summary>
    /// Records a new connection.
    /// </summary>
    public static void RecordConnection()
    {
        Interlocked.Increment(ref _totalConnections);
    }

    /// <summary>
    /// Records a processed message.
    /// </summary>
    public static void RecordMessage()
    {
        Interlocked.Increment(ref _totalMessages);
    }

    /// <summary>
    /// Updates peak user count.
    /// </summary>
    public static void UpdatePeakUsers(int currentUsers)
    {
        int current;
        while ((current = _peakUsers) < currentUsers)
        {
            Interlocked.CompareExchange(ref _peakUsers, currentUsers, current);
        }
    }
}
