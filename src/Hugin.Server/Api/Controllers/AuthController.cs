// Hugin IRC Server - Authentication Controller
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using Hugin.Server.Api.Auth;
using Hugin.Server.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hugin.Server.Api.Controllers;

/// <summary>
/// Authentication endpoints for the admin panel.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IJwtService _jwtService;
    private readonly IAdminUserService _userService;
    private readonly ILogger<AuthController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthController"/> class.
    /// </summary>
    public AuthController(
        IJwtService jwtService,
        IAdminUserService userService,
        ILogger<AuthController> logger)
    {
        _jwtService = jwtService;
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates an admin user and returns JWT tokens.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _userService.AuthenticateAsync(request.Username, request.Password, cancellationToken);

        if (user == null)
        {
            _logger.LogWarning("Failed login attempt for user: {Username} from {IP}", 
                request.Username, 
                HttpContext.Connection.RemoteIpAddress);

            return Unauthorized(ApiResponse.Fail("Invalid username or password"));
        }

        var (token, refreshToken, expiresIn) = _jwtService.GenerateTokens(user.Id, user.Username, user.Roles);

        _logger.LogInformation("User {Username} logged in from {IP}", 
            user.Username, 
            HttpContext.Connection.RemoteIpAddress);

        return Ok(ApiResponse<LoginResponse>.Ok(new LoginResponse
        {
            Token = token,
            ExpiresIn = expiresIn,
            RefreshToken = refreshToken,
            DisplayName = user.DisplayName,
            Roles = user.Roles
        }));
    }

    /// <summary>
    /// Refreshes an access token using a refresh token.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var userId = _jwtService.ValidateRefreshToken(request.RefreshToken);

        if (userId == null)
        {
            return Unauthorized(ApiResponse.Fail("Invalid or expired refresh token"));
        }

        var user = await _userService.GetByIdAsync(userId, cancellationToken);

        if (user == null || !user.IsEnabled)
        {
            return Unauthorized(ApiResponse.Fail("User not found or disabled"));
        }

        // Revoke old refresh token
        _jwtService.RevokeRefreshToken(request.RefreshToken);

        // Generate new tokens
        var (token, refreshToken, expiresIn) = _jwtService.GenerateTokens(user.Id, user.Username, user.Roles);

        return Ok(ApiResponse<LoginResponse>.Ok(new LoginResponse
        {
            Token = token,
            ExpiresIn = expiresIn,
            RefreshToken = refreshToken,
            DisplayName = user.DisplayName,
            Roles = user.Roles
        }));
    }

    /// <summary>
    /// Logs out the current user (revokes refresh token).
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public IActionResult Logout([FromBody] RefreshTokenRequest? request)
    {
        if (request?.RefreshToken != null)
        {
            _jwtService.RevokeRefreshToken(request.RefreshToken);
        }

        _logger.LogInformation("User {Username} logged out", User.Identity?.Name);

        return Ok(ApiResponse.Ok("Logged out successfully"));
    }

    /// <summary>
    /// Gets the current user's profile.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<AdminUserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(ApiResponse.Fail("Invalid token"));
        }

        var user = await _userService.GetByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound(ApiResponse.Fail("User not found"));
        }

        return Ok(ApiResponse<AdminUserDto>.Ok(new AdminUserDto
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Roles = user.Roles,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt
        }));
    }
}

/// <summary>
/// Admin user DTO for API responses.
/// </summary>
public sealed class AdminUserDto
{
    /// <summary>
    /// User ID.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Username.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// Display name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Email address.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// User roles.
    /// </summary>
    public required string[] Roles { get; init; }

    /// <summary>
    /// When the user was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Last login time.
    /// </summary>
    public DateTimeOffset? LastLoginAt { get; init; }
}
