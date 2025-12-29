// Hugin IRC Server - Status Controller Tests
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using FluentAssertions;
using Hugin.Server.Api.Controllers;
using Hugin.Server.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hugin.Integration.Tests.Api;

/// <summary>
/// Unit tests for the StatusController.
/// </summary>
public sealed class StatusControllerTests
{
    private readonly Mock<IServerStatusService> _statusServiceMock;
    private readonly Mock<ILogger<StatusController>> _loggerMock;
    private readonly StatusController _controller;

    public StatusControllerTests()
    {
        _statusServiceMock = new Mock<IServerStatusService>();
        _loggerMock = new Mock<ILogger<StatusController>>();
        _controller = new StatusController(_statusServiceMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task GetStatusReturnsServerStatus()
    {
        // Arrange
        var expectedStatus = new ServerStatusDto
        {
            ServerName = "irc.test.com",
            NetworkName = "TestNet",
            Version = "1.0.0",
            Uptime = TimeSpan.FromHours(24),
            IsRunning = true,
            ConnectedUsers = 100,
            ChannelCount = 25,
            OperatorsOnline = 3,
            Statistics = new ServerStatisticsDto
            {
                TotalConnections = 1000,
                TotalMessages = 50000,
                PeakUsers = 150,
                MemoryUsageBytes = 256 * 1024 * 1024,
                CpuUsagePercent = 15.5,
                MessagesPerSecond = 10.2
            }
        };

        _statusServiceMock
            .Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedStatus);

        // Act
        var result = await _controller.GetStatus(CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<ServerStatusDto>>().Subject;
        
        response.Success.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.ServerName.Should().Be("irc.test.com");
        response.Data.NetworkName.Should().Be("TestNet");
        response.Data.IsRunning.Should().BeTrue();
        response.Data.ConnectedUsers.Should().Be(100);
    }

    [Fact]
    public async Task GetStatisticsReturnsServerStatistics()
    {
        // Arrange
        var expectedStats = new ServerStatisticsDto
        {
            TotalConnections = 5000,
            TotalMessages = 250000,
            PeakUsers = 500,
            MemoryUsageBytes = 512 * 1024 * 1024,
            CpuUsagePercent = 25.0,
            MessagesPerSecond = 50.5
        };

        _statusServiceMock
            .Setup(x => x.GetStatisticsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedStats);

        // Act
        var result = await _controller.GetStatistics(CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<ServerStatisticsDto>>().Subject;
        
        response.Success.Should().BeTrue();
        response.Data!.TotalConnections.Should().Be(5000);
        response.Data.TotalMessages.Should().Be(250000);
        response.Data.PeakUsers.Should().Be(500);
    }

    [Fact]
    public void GetHealthReturnsHealthyStatus()
    {
        // Act
        var result = _controller.GetHealth();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var health = okResult.Value.Should().BeOfType<HealthCheckDto>().Subject;
        
        health.Status.Should().Be("healthy");
        health.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        health.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RestartRequestsServerRestart()
    {
        // Arrange
        _statusServiceMock
            .Setup(x => x.RestartAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Setup user identity
        var controller = CreateControllerWithUser("admin", "Admin");

        // Act
        var result = await controller.Restart(CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse>().Subject;
        
        response.Success.Should().BeTrue();
        _statusServiceMock.Verify(x => x.RestartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShutdownRequestsServerShutdown()
    {
        // Arrange
        _statusServiceMock
            .Setup(x => x.ShutdownAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var controller = CreateControllerWithUser("admin", "Admin");

        // Act
        var result = await controller.Shutdown(null, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse>().Subject;
        
        response.Success.Should().BeTrue();
        _statusServiceMock.Verify(x => x.ShutdownAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private StatusController CreateControllerWithUser(string username, string role)
    {
        var controller = new StatusController(_statusServiceMock.Object, _loggerMock.Object);
        
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var claims = new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, username),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role)
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "TestAuth");
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(identity);
        
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }
}
