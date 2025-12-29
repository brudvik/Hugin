// Hugin IRC Server - JWT Service Tests
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using FluentAssertions;
using Hugin.Server.Api.Auth;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hugin.Integration.Tests.Api;

/// <summary>
/// Unit tests for the JwtService.
/// </summary>
public sealed class JwtServiceTests
{
    private static readonly string[] AdminRoles = ["Admin"];
    private static readonly string[] UserRoles = ["User"];
    private static readonly string[] AdminOperatorRoles = ["Admin", "Operator"];
    
    private readonly JwtConfiguration _config;
    private readonly Mock<ILogger<JwtService>> _loggerMock;
    private readonly JwtService _service;

    public JwtServiceTests()
    {
        _config = new JwtConfiguration
        {
            SecretKey = "ThisIsAVerySecureSecretKeyForTestingPurposes123!",
            Issuer = "HuginTest",
            Audience = "HuginTestAdmin",
            AccessTokenExpirationMinutes = 60,
            RefreshTokenExpirationDays = 7
        };
        _loggerMock = new Mock<ILogger<JwtService>>();
        _service = new JwtService(_config, _loggerMock.Object);
    }

    [Fact]
    public void GenerateTokensReturnsValidTokens()
    {
        // Arrange
        var userId = "user-123";
        var username = "testuser";
        // Act
        var (accessToken, refreshToken, expiresIn) = _service.GenerateTokens(userId, username, AdminOperatorRoles);

        // Assert
        accessToken.Should().NotBeNullOrEmpty();
        refreshToken.Should().NotBeNullOrEmpty();
        expiresIn.Should().Be(60 * 60); // 60 minutes in seconds
        
        // JWT tokens have 3 parts separated by dots
        accessToken.Split('.').Should().HaveCount(3);
    }

    [Fact]
    public void GenerateTokensCreatesDifferentTokensForDifferentUsers()
    {
        // Act
        var (token1, refresh1, _) = _service.GenerateTokens("user-1", "user1", UserRoles);
        var (token2, refresh2, _) = _service.GenerateTokens("user-2", "user2", UserRoles);

        // Assert
        token1.Should().NotBe(token2);
        refresh1.Should().NotBe(refresh2);
    }

    [Fact]
    public void GenerateTokensCreatesDifferentRefreshTokensEachTime()
    {
        // Arrange
        var userId = "user-123";
        var username = "testuser";

        // Act
        var (_, refresh1, _) = _service.GenerateTokens(userId, username, AdminRoles);
        var (_, refresh2, _) = _service.GenerateTokens(userId, username, AdminRoles);

        // Assert
        refresh1.Should().NotBe(refresh2);
    }

    [Fact]
    public void ValidateRefreshTokenReturnsUserIdForValidToken()
    {
        // Arrange
        var userId = "user-123";
        var (_, refreshToken, _) = _service.GenerateTokens(userId, "testuser", AdminRoles);

        // Act
        var result = _service.ValidateRefreshToken(refreshToken);

        // Assert
        result.Should().Be(userId);
    }

    [Fact]
    public void ValidateRefreshTokenReturnsNullForInvalidToken()
    {
        // Act
        var result = _service.ValidateRefreshToken("invalid-refresh-token");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateRefreshTokenReturnsNullForRevokedToken()
    {
        // Arrange
        var (_, refreshToken, _) = _service.GenerateTokens("user-123", "testuser", AdminRoles);
        _service.RevokeRefreshToken(refreshToken);

        // Act
        var result = _service.ValidateRefreshToken(refreshToken);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void RevokeRefreshTokenPreventsTokenReuse()
    {
        // Arrange
        var (_, refreshToken, _) = _service.GenerateTokens("user-123", "testuser", AdminRoles);
        
        // Verify token is valid before revocation
        _service.ValidateRefreshToken(refreshToken).Should().Be("user-123");

        // Act
        _service.RevokeRefreshToken(refreshToken);

        // Assert
        _service.ValidateRefreshToken(refreshToken).Should().BeNull();
    }

    [Fact]
    public void GetSigningKeyReturnsValidKey()
    {
        // Act
        var key = _service.GetSigningKey();

        // Assert
        key.Should().NotBeNull();
        key.KeySize.Should().BeGreaterOrEqualTo(256); // Minimum for HMAC-SHA256
    }

    [Fact]
    public void ConstructorGeneratesKeyIfNotProvided()
    {
        // Arrange
        var emptyConfig = new JwtConfiguration
        {
            SecretKey = "", // Empty key
            Issuer = "Test",
            Audience = "Test"
        };
        var loggerMock = new Mock<ILogger<JwtService>>();

        // Act
        var service = new JwtService(emptyConfig, loggerMock.Object);
        var (token, _, _) = service.GenerateTokens("user-1", "user", AdminRoles);

        // Assert
        token.Should().NotBeNullOrEmpty();
        token.Split('.').Should().HaveCount(3);
    }

    [Fact]
    public void ConstructorGeneratesKeyIfTooShort()
    {
        // Arrange
        var shortKeyConfig = new JwtConfiguration
        {
            SecretKey = "short", // Too short
            Issuer = "Test",
            Audience = "Test"
        };
        var loggerMock = new Mock<ILogger<JwtService>>();

        // Act
        var service = new JwtService(shortKeyConfig, loggerMock.Object);
        var key = service.GetSigningKey();

        // Assert
        key.KeySize.Should().BeGreaterOrEqualTo(256);
    }

    [Fact]
    public void GenerateTokensWithEmptyRolesSucceeds()
    {
        // Act
        var (token, refresh, expiresIn) = _service.GenerateTokens("user-1", "user", Array.Empty<string>());

        // Assert
        token.Should().NotBeNullOrEmpty();
        refresh.Should().NotBeNullOrEmpty();
        expiresIn.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GenerateTokensWithMultipleRolesSucceeds()
    {
        // Arrange
        var roles = new[] { "Admin", "Operator", "Moderator", "Support" };

        // Act
        var (token, _, _) = _service.GenerateTokens("user-1", "superuser", roles);

        // Assert
        token.Should().NotBeNullOrEmpty();
        token.Split('.').Should().HaveCount(3);
    }
}
