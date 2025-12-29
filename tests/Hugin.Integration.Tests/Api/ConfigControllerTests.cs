// Hugin IRC Server - Config Controller Tests
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using FluentAssertions;
using Hugin.Server.Api.Controllers;
using Hugin.Server.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hugin.Integration.Tests.Api;

/// <summary>
/// Unit tests for the ConfigController.
/// </summary>
public sealed class ConfigControllerTests
{
    private readonly Mock<ILogger<ConfigController>> _loggerMock;
    private readonly ConfigController _controller;

    public ConfigControllerTests()
    {
        _loggerMock = new Mock<ILogger<ConfigController>>();
        
        // Use InMemoryCollection for proper IConfiguration behavior
        var configData = new Dictionary<string, string?>
        {
            ["Hugin:Server:Name"] = "irc.test.com",
            ["Hugin:Server:NetworkName"] = "TestNet",
            ["Hugin:Server:Description"] = "Test IRC Server",
            ["Hugin:Server:AdminEmail"] = "admin@test.com",
            ["Hugin:Limits:MaxUsers"] = "10000",
            ["Hugin:Limits:MaxChannelsPerUser"] = "50",
            ["Hugin:Limits:MaxNickLength"] = "30",
            ["Hugin:Limits:MaxChannelLength"] = "50",
            ["Hugin:Limits:MaxTopicLength"] = "390",
            ["Hugin:Ports:Tls"] = "6697",
            ["Hugin:Ports:WebSocket"] = "8443",
            ["Hugin:Ports:Admin"] = "9443",
            ["Hugin:RateLimits:ConnectionsPerMinute"] = "10",
            ["Hugin:RateLimits:MessagesPerSecond"] = "5",
            ["Hugin:RateLimits:JoinsPerMinute"] = "10",
            ["Hugin:RateLimits:NickChangesPerMinute"] = "3",
            ["Hugin:RateLimits:PrivmsgsPerSecond"] = "3"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
        
        _controller = new ConfigController(configuration, _loggerMock.Object);
    }

    [Fact]
    public void GetConfigurationReturnsCurrentConfiguration()
    {
        // Act
        var result = _controller.GetConfiguration();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<ServerConfigDto>>().Subject;
        
        response.Success.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.ServerName.Should().Be("irc.test.com");
        response.Data.NetworkName.Should().Be("TestNet");
    }

    [Fact]
    public void GetConfigurationUsesDefaultValuesWhenNotConfigured()
    {
        // Arrange - use empty configuration
        var emptyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var controller = new ConfigController(emptyConfig, _loggerMock.Object);

        // Act
        var result = controller.GetConfiguration();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<ServerConfigDto>>().Subject;
        
        response.Success.Should().BeTrue();
        response.Data!.ServerName.Should().Be("irc.example.com"); // Default value
    }

    [Fact]
    public async Task GetMotdReturnsMotdContent()
    {
        // Act
        var result = await _controller.GetMotd(CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<MotdDto>>().Subject;
        
        response.Success.Should().BeTrue();
        response.Data.Should().NotBeNull();
    }

    [Fact]
    public void GetRateLimitsReturnsRateLimitConfiguration()
    {
        // Act
        var result = _controller.GetRateLimits();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<RateLimitConfigDto>>().Subject;
        
        response.Success.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.ConnectionsPerMinute.Should().Be(10);
        response.Data.MessagesPerSecond.Should().Be(5);
    }
}
