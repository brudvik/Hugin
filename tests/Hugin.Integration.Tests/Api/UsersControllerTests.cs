// Hugin IRC Server - Users Controller Tests
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using FluentAssertions;
using Hugin.Core.Interfaces;
using Hugin.Persistence;
using Hugin.Server.Api.Controllers;
using Hugin.Server.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hugin.Integration.Tests.Api;

/// <summary>
/// Unit tests for the UsersController.
/// </summary>
public sealed class UsersControllerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IChannelRepository> _channelRepositoryMock;
    private readonly Mock<IMessageBroker> _messageBrokerMock;
    private readonly Mock<IConnectionManager> _connectionManagerMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<UsersController>> _loggerMock;
    private readonly UsersController _controller;

    public UsersControllerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _channelRepositoryMock = new Mock<IChannelRepository>();
        _messageBrokerMock = new Mock<IMessageBroker>();
        _connectionManagerMock = new Mock<IConnectionManager>();
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<UsersController>>();
        
        // Setup default returns
        _userRepositoryMock.Setup(x => x.GetAll()).Returns(Array.Empty<Hugin.Core.Entities.User>());
        _channelRepositoryMock.Setup(x => x.GetAll()).Returns(Array.Empty<Hugin.Core.Entities.Channel>());
        
        _controller = new UsersController(
            _userRepositoryMock.Object,
            _channelRepositoryMock.Object,
            _messageBrokerMock.Object,
            _connectionManagerMock.Object,
            _configurationMock.Object,
            null!,  // DbContext not needed for these unit tests
            _loggerMock.Object);
    }

    [Fact]
    public void GetUsersReturnsPagedResult()
    {
        // Act
        var result = _controller.GetUsers(1, 10, null, null, null);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<PagedResult<UserDto>>>().Subject;
        
        response.Success.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Page.Should().Be(1);
        response.Data.PageSize.Should().Be(10);
    }

    [Fact]
    public void GetUsersWithSearchParameterReturnsResult()
    {
        // Act
        var result = _controller.GetUsers(1, 10, "admin", null, null);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<PagedResult<UserDto>>>().Subject;
        
        response.Success.Should().BeTrue();
    }

    [Fact]
    public void GetUsersWithChannelFilterReturnsResult()
    {
        // Act
        var result = _controller.GetUsers(1, 10, null, "#test", null);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<PagedResult<UserDto>>>().Subject;
        
        response.Success.Should().BeTrue();
    }

    [Fact]
    public void GetUsersWithOperatorFilterReturnsResult()
    {
        // Act
        var result = _controller.GetUsers(1, 10, null, null, true);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<PagedResult<UserDto>>>().Subject;
        
        response.Success.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 25)]
    [InlineData(1, 50)]
    public void GetUsersWithDifferentPaginationReturnsResult(int page, int pageSize)
    {
        // Act
        var result = _controller.GetUsers(page, pageSize, null, null, null);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<PagedResult<UserDto>>>().Subject;
        
        response.Success.Should().BeTrue();
        response.Data!.Page.Should().Be(page);
        response.Data.PageSize.Should().Be(pageSize);
    }
}
