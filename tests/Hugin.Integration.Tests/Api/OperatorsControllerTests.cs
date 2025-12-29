// Hugin IRC Server - Operators Controller Tests
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
/// Unit tests for the OperatorsController.
/// </summary>
public sealed class OperatorsControllerTests
{
    private readonly Mock<ILogger<OperatorsController>> _loggerMock;
    private readonly OperatorsController _controller;

    public OperatorsControllerTests()
    {
        _loggerMock = new Mock<ILogger<OperatorsController>>();
        _controller = new OperatorsController(_loggerMock.Object);
    }

    [Fact]
    public void GetOperatorsReturnsEmptyArrayInitially()
    {
        // Act
        var result = _controller.GetOperators();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<OperatorDto[]>>().Subject;
        
        response.Success.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data.Should().BeEmpty();
    }

    [Fact]
    public void GetOperatorByNameReturnsNotFoundForUnknownOperator()
    {
        // Act
        var result = _controller.GetOperator("Unknown");

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void CreateOperatorReturnsCreatedResult()
    {
        // Arrange
        var request = new OperatorRequest
        {
            Name = "TestOper",
            OperClass = "local",
            Hostmasks = new[] { "*@example.com" }
        };

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var claims = new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "admin"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin")
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "TestAuth");
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(identity);
        
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Act
        var result = _controller.CreateOperator(request);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var response = createdResult.Value.Should().BeOfType<ApiResponse<OperatorDto>>().Subject;
        
        response.Success.Should().BeTrue();
        response.Data!.Name.Should().Be("TestOper");
        response.Data.OperClass.Should().Be("local");
    }
}
