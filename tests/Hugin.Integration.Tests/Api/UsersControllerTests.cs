// Hugin IRC Server - Users Controller Tests
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
/// Unit tests for the UsersController.
/// </summary>
public sealed class UsersControllerTests
{
    private readonly Mock<ILogger<UsersController>> _loggerMock;
    private readonly UsersController _controller;

    public UsersControllerTests()
    {
        _loggerMock = new Mock<ILogger<UsersController>>();
        _controller = new UsersController(_loggerMock.Object);
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
