// Hugin IRC Server - JWT Authentication Service
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Hugin.Server.Api.Auth;

/// <summary>
/// Configuration for JWT authentication.
/// </summary>
public sealed class JwtConfiguration
{
    /// <summary>
    /// Secret key for signing tokens (min 32 characters).
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Token issuer.
    /// </summary>
    public string Issuer { get; set; } = "Hugin";

    /// <summary>
    /// Token audience.
    /// </summary>
    public string Audience { get; set; } = "HuginAdmin";

    /// <summary>
    /// Access token expiration in minutes.
    /// </summary>
    public int AccessTokenExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// Refresh token expiration in days.
    /// </summary>
    public int RefreshTokenExpirationDays { get; set; } = 7;
}

/// <summary>
/// Service for JWT token generation and validation.
/// </summary>
public interface IJwtService
{
    /// <summary>
    /// Generates access and refresh tokens for a user.
    /// </summary>
    (string AccessToken, string RefreshToken, int ExpiresIn) GenerateTokens(string userId, string username, string[] roles);

    /// <summary>
    /// Validates a refresh token and returns the user ID if valid.
    /// </summary>
    string? ValidateRefreshToken(string refreshToken);

    /// <summary>
    /// Revokes a refresh token.
    /// </summary>
    void RevokeRefreshToken(string refreshToken);

    /// <summary>
    /// Gets the signing key for token validation.
    /// </summary>
    SymmetricSecurityKey GetSigningKey();
}

/// <summary>
/// JWT token service implementation.
/// </summary>
public sealed class JwtService : IJwtService
{
    private readonly JwtConfiguration _config;
    private readonly ILogger<JwtService> _logger;
    private readonly Dictionary<string, RefreshTokenInfo> _refreshTokens = new();
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="JwtService"/> class.
    /// </summary>
    public JwtService(JwtConfiguration config, ILogger<JwtService> logger)
    {
        _config = config;
        _logger = logger;

        // Generate a secure key if not provided
        if (string.IsNullOrEmpty(_config.SecretKey) || _config.SecretKey.Length < 32)
        {
            _config.SecretKey = GenerateSecureKey();
            _logger.LogWarning("JWT secret key was empty or too short. Generated a new secure key.");
        }
    }

    /// <inheritdoc />
    public (string AccessToken, string RefreshToken, int ExpiresIn) GenerateTokens(
        string userId, 
        string username, 
        string[] roles)
    {
        var key = GetSigningKey();
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.UniqueName, username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var expiration = DateTime.UtcNow.AddMinutes(_config.AccessTokenExpirationMinutes);
        var expiresIn = _config.AccessTokenExpirationMinutes * 60;

        var token = new JwtSecurityToken(
            issuer: _config.Issuer,
            audience: _config.Audience,
            claims: claims,
            expires: expiration,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        var refreshToken = GenerateRefreshToken(userId);

        return (accessToken, refreshToken, expiresIn);
    }

    /// <inheritdoc />
    public string? ValidateRefreshToken(string refreshToken)
    {
        lock (_lock)
        {
            if (_refreshTokens.TryGetValue(refreshToken, out var info))
            {
                if (info.ExpiresAt > DateTimeOffset.UtcNow && !info.IsRevoked)
                {
                    return info.UserId;
                }

                // Token expired or revoked, remove it
                _refreshTokens.Remove(refreshToken);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public void RevokeRefreshToken(string refreshToken)
    {
        lock (_lock)
        {
            if (_refreshTokens.TryGetValue(refreshToken, out var info))
            {
                info.IsRevoked = true;
            }
        }
    }

    /// <inheritdoc />
    public SymmetricSecurityKey GetSigningKey()
    {
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.SecretKey));
    }

    private string GenerateRefreshToken(string userId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        lock (_lock)
        {
            // Clean up expired tokens
            var expired = _refreshTokens
                .Where(kvp => kvp.Value.ExpiresAt < DateTimeOffset.UtcNow)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expired)
            {
                _refreshTokens.Remove(key);
            }

            _refreshTokens[token] = new RefreshTokenInfo
            {
                UserId = userId,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(_config.RefreshTokenExpirationDays),
                IsRevoked = false
            };
        }

        return token;
    }

    private static string GenerateSecureKey()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }

    private sealed class RefreshTokenInfo
    {
        public required string UserId { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
        public bool IsRevoked { get; set; }
    }
}

/// <summary>
/// Admin user for authentication.
/// </summary>
public sealed class AdminUser
{
    /// <summary>
    /// Unique user ID.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Username for login.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// Display name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Password hash (Argon2id).
    /// </summary>
    public required string PasswordHash { get; init; }

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
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Whether the account is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Service for managing admin users.
/// </summary>
public interface IAdminUserService
{
    /// <summary>
    /// Authenticates an admin user.
    /// </summary>
    Task<AdminUser?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an admin user by ID.
    /// </summary>
    Task<AdminUser?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new admin user.
    /// </summary>
    Task<AdminUser> CreateAsync(string username, string password, string email, string[] roles, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an admin user's password.
    /// </summary>
    Task UpdatePasswordAsync(string id, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all admin users.
    /// </summary>
    Task<IReadOnlyList<AdminUser>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if any admin users exist.
    /// </summary>
    Task<bool> AnyExistAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory admin user service (for initial setup, later persisted to database).
/// </summary>
public sealed class AdminUserService : IAdminUserService
{
    private readonly List<AdminUser> _users = new();
    private readonly ILogger<AdminUserService> _logger;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminUserService"/> class.
    /// </summary>
    public AdminUserService(ILogger<AdminUserService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<AdminUser?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var user = _users.FirstOrDefault(u => 
                string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase) && 
                u.IsEnabled);

            if (user == null)
            {
                _logger.LogWarning("Login attempt for unknown user: {Username}", username);
                return Task.FromResult<AdminUser?>(null);
            }

            // Verify password using Argon2id
            if (!VerifyPassword(password, user.PasswordHash))
            {
                _logger.LogWarning("Failed login attempt for user: {Username}", username);
                return Task.FromResult<AdminUser?>(null);
            }

            user.LastLoginAt = DateTimeOffset.UtcNow;
            _logger.LogInformation("User {Username} logged in successfully", username);

            return Task.FromResult<AdminUser?>(user);
        }
    }

    /// <inheritdoc />
    public Task<AdminUser?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var user = _users.FirstOrDefault(u => u.Id == id);
            return Task.FromResult(user);
        }
    }

    /// <inheritdoc />
    public Task<AdminUser> CreateAsync(string username, string password, string email, string[] roles, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_users.Any(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"User '{username}' already exists.");
            }

            var user = new AdminUser
            {
                Id = Guid.NewGuid().ToString(),
                Username = username,
                DisplayName = username,
                PasswordHash = HashPassword(password),
                Email = email,
                Roles = roles,
                CreatedAt = DateTimeOffset.UtcNow,
                IsEnabled = true
            };

            _users.Add(user);
            _logger.LogInformation("Created admin user: {Username}", username);

            return Task.FromResult(user);
        }
    }

    /// <inheritdoc />
    public Task UpdatePasswordAsync(string id, string newPassword, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var user = _users.FirstOrDefault(u => u.Id == id);
            if (user == null)
            {
                throw new InvalidOperationException($"User not found: {id}");
            }

            // Since AdminUser is immutable for most properties, we need to replace it
            var index = _users.IndexOf(user);
            _users[index] = new AdminUser
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                PasswordHash = HashPassword(newPassword),
                Email = user.Email,
                Roles = user.Roles,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                IsEnabled = user.IsEnabled
            };

            _logger.LogInformation("Updated password for user: {Username}", user.Username);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AdminUser>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<AdminUser>>(_users.ToList());
        }
    }

    /// <inheritdoc />
    public Task<bool> AnyExistAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_users.Count > 0);
        }
    }

    private static string HashPassword(string password)
    {
        // Use Argon2id for password hashing
        using var argon2 = new Konscious.Security.Cryptography.Argon2id(System.Text.Encoding.UTF8.GetBytes(password));
        argon2.Salt = RandomNumberGenerator.GetBytes(16);
        argon2.DegreeOfParallelism = 4;
        argon2.MemorySize = 65536;
        argon2.Iterations = 3;

        var hash = argon2.GetBytes(32);
        var result = new byte[16 + 32]; // salt + hash
        Buffer.BlockCopy(argon2.Salt, 0, result, 0, 16);
        Buffer.BlockCopy(hash, 0, result, 16, 32);

        return Convert.ToBase64String(result);
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        try
        {
            var stored = Convert.FromBase64String(storedHash);
            if (stored.Length != 48) return false;

            var salt = new byte[16];
            var hash = new byte[32];
            Buffer.BlockCopy(stored, 0, salt, 0, 16);
            Buffer.BlockCopy(stored, 16, hash, 0, 32);

            using var argon2 = new Konscious.Security.Cryptography.Argon2id(System.Text.Encoding.UTF8.GetBytes(password));
            argon2.Salt = salt;
            argon2.DegreeOfParallelism = 4;
            argon2.MemorySize = 65536;
            argon2.Iterations = 3;

            var computedHash = argon2.GetBytes(32);
            return CryptographicOperations.FixedTimeEquals(hash, computedHash);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Admin role definitions.
/// </summary>
public static class AdminRoles
{
    /// <summary>
    /// Full administrator access.
    /// </summary>
    public const string Admin = "Admin";

    /// <summary>
    /// Can view status and logs but not modify configuration.
    /// </summary>
    public const string Viewer = "Viewer";

    /// <summary>
    /// Can manage users and channels but not server configuration.
    /// </summary>
    public const string Moderator = "Moderator";

    /// <summary>
    /// Can manage operators and bans.
    /// </summary>
    public const string Operator = "Operator";
}
