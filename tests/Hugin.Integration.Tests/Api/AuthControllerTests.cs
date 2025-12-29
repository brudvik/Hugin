// Hugin IRC Server - Auth Controller Tests
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using FluentAssertions;
using Hugin.Server.Api.Auth;
using Hugin.Server.Api.Controllers;
using Hugin.Server.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using Xunit;

namespace Hugin.Integration.Tests.Api;

/// <summary>
/// Unit tests for the AuthController.
/// </summary>
public sealed class AuthControllerTests
{
    private readonly Mock<IJwtService> _jwtServiceMock;
    private readonly Mock<IAdminUserService> _userServiceMock;
    private readonly Mock<ILogger<AuthController>> _loggerMock;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _jwtServiceMock = new Mock<IJwtService>();
        _userServiceMock = new Mock<IAdminUserService>();
        _loggerMock = new Mock<ILogger<AuthController>>();
        
        _controller = new AuthController(
            _jwtServiceMock.Object,
            _userServiceMock.Object,
            _loggerMock.Object);

        // Setup HttpContext
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    [Fact]
    public async Task LoginWithValidCredentialsReturnsToken()
    {
        // Arrange
        var request = new LoginRequest { Username = "admin", Password = "password123" };
        var user = new AdminUser
        {
            Id = "user-1",
            Username = "admin",
            DisplayName = "Administrator",
            Email = "admin@example.com",
            Roles = new[] { "Admin" },
            PasswordHash = "hashed-password",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };

        _userServiceMock
            .Setup(x => x.AuthenticateAsync("admin", "password123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _jwtServiceMock
            .Setup(x => x.GenerateTokens("user-1", "admin", It.IsAny<string[]>()))
            .Returns(("access-token", "refresh-token", 3600));

        // Act
        var result = await _controller.Login(request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<LoginResponse>>().Subject;
        
        response.Success.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Token.Should().Be("access-token");
        response.Data.RefreshToken.Should().Be("refresh-token");
        response.Data.ExpiresIn.Should().Be(3600);
        response.Data.DisplayName.Should().Be("Administrator");
    }

    [Fact]
    public async Task LoginWithInvalidCredentialsReturnsUnauthorized()
    {
        // Arrange
        var request = new LoginRequest { Username = "admin", Password = "wrongpassword" };

        _userServiceMock
            .Setup(x => x.AuthenticateAsync("admin", "wrongpassword", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdminUser?)null);

        // Act
        var result = await _controller.Login(request, CancellationToken.None);

        // Assert
        var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        var response = unauthorizedResult.Value.Should().BeOfType<ApiResponse>().Subject;
        
        response.Success.Should().BeFalse();
        response.Error.Should().Contain("Invalid");
    }

    [Fact]
    public async Task LoginWithEmptyUsernameReturnsUnauthorized()
    {
        // Arrange
        var request = new LoginRequest { Username = "", Password = "password123" };

        _userServiceMock
            .Setup(x => x.AuthenticateAsync("", "password123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdminUser?)null);

        // Act
        var result = await _controller.Login(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task RefreshWithValidTokenReturnsNewTokens()
    {
        // Arrange
        var request = new RefreshTokenRequest { RefreshToken = "valid-refresh-token" };
        var user = new AdminUser
        {
            Id = "user-1",
            Username = "admin",
            DisplayName = "Administrator",
            Email = "admin@example.com",
            Roles = new[] { "Admin" },
            PasswordHash = "hashed-password",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };

        _jwtServiceMock
            .Setup(x => x.ValidateRefreshToken("valid-refresh-token"))
            .Returns("user-1");

        _userServiceMock
            .Setup(x => x.GetByIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _jwtServiceMock
            .Setup(x => x.GenerateTokens("user-1", "admin", It.IsAny<string[]>()))
            .Returns(("new-access-token", "new-refresh-token", 3600));

        // Act
        var result = await _controller.Refresh(request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<LoginResponse>>().Subject;
        
        response.Success.Should().BeTrue();
        response.Data!.Token.Should().Be("new-access-token");
    }

    [Fact]
    public async Task RefreshWithInvalidTokenReturnsUnauthorized()
    {
        // Arrange
        var request = new RefreshTokenRequest { RefreshToken = "invalid-refresh-token" };

        _jwtServiceMock
            .Setup(x => x.ValidateRefreshToken("invalid-refresh-token"))
            .Returns((string?)null);

        // Act
        var result = await _controller.Refresh(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }
}
