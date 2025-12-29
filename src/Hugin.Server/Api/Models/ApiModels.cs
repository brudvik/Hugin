// Hugin IRC Server - API Data Transfer Objects
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using System.ComponentModel.DataAnnotations;

namespace Hugin.Server.Api.Models;

#region Authentication

/// <summary>
/// Login request for admin authentication.
/// </summary>
public sealed class LoginRequest
{
    /// <summary>
    /// Administrator username.
    /// </summary>
    [Required]
    [StringLength(50, MinimumLength = 1)]
    public required string Username { get; init; }

    /// <summary>
    /// Administrator password.
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 8)]
    public required string Password { get; init; }
}

/// <summary>
/// Login response with JWT token.
/// </summary>
public sealed class LoginResponse
{
    /// <summary>
    /// JWT access token.
    /// </summary>
    public required string Token { get; init; }

    /// <summary>
    /// Token expiration time in seconds.
    /// </summary>
    public required int ExpiresIn { get; init; }

    /// <summary>
    /// Refresh token for obtaining new access tokens.
    /// </summary>
    public string? RefreshToken { get; init; }

    /// <summary>
    /// Administrator display name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Administrator roles/permissions.
    /// </summary>
    public required string[] Roles { get; init; }
}

/// <summary>
/// Token refresh request.
/// </summary>
public sealed class RefreshTokenRequest
{
    /// <summary>
    /// The refresh token.
    /// </summary>
    [Required]
    public required string RefreshToken { get; init; }
}

#endregion

#region Server Status

/// <summary>
/// Server status overview.
/// </summary>
public sealed class ServerStatusDto
{
    /// <summary>
    /// Server name.
    /// </summary>
    public required string ServerName { get; init; }

    /// <summary>
    /// Network name.
    /// </summary>
    public required string NetworkName { get; init; }

    /// <summary>
    /// Server version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Server uptime.
    /// </summary>
    public required TimeSpan Uptime { get; init; }

    /// <summary>
    /// Whether the server is running.
    /// </summary>
    public required bool IsRunning { get; init; }

    /// <summary>
    /// Current number of connected users.
    /// </summary>
    public required int ConnectedUsers { get; init; }

    /// <summary>
    /// Current number of channels.
    /// </summary>
    public required int ChannelCount { get; init; }

    /// <summary>
    /// Current number of operators online.
    /// </summary>
    public required int OperatorsOnline { get; init; }

    /// <summary>
    /// Server statistics.
    /// </summary>
    public required ServerStatisticsDto Statistics { get; init; }
}

/// <summary>
/// Server statistics.
/// </summary>
public sealed class ServerStatisticsDto
{
    /// <summary>
    /// Total connections since server start.
    /// </summary>
    public required long TotalConnections { get; init; }

    /// <summary>
    /// Total messages processed.
    /// </summary>
    public required long TotalMessages { get; init; }

    /// <summary>
    /// Peak concurrent users.
    /// </summary>
    public required int PeakUsers { get; init; }

    /// <summary>
    /// Memory usage in bytes.
    /// </summary>
    public required long MemoryUsageBytes { get; init; }

    /// <summary>
    /// CPU usage percentage.
    /// </summary>
    public required double CpuUsagePercent { get; init; }

    /// <summary>
    /// Messages per second (average).
    /// </summary>
    public required double MessagesPerSecond { get; init; }
}

#endregion

#region Configuration

/// <summary>
/// Server configuration DTO.
/// </summary>
public sealed class ServerConfigDto
{
    /// <summary>
    /// Server name (FQDN).
    /// </summary>
    [Required]
    [StringLength(253, MinimumLength = 1)]
    public required string ServerName { get; init; }

    /// <summary>
    /// Network name.
    /// </summary>
    [Required]
    [StringLength(50, MinimumLength = 1)]
    public required string NetworkName { get; init; }

    /// <summary>
    /// Server description (MOTD headline).
    /// </summary>
    [StringLength(500)]
    public string? Description { get; init; }

    /// <summary>
    /// Administrator email.
    /// </summary>
    [EmailAddress]
    public string? AdminEmail { get; init; }

    /// <summary>
    /// Maximum users allowed.
    /// </summary>
    [Range(1, 100000)]
    public int MaxUsers { get; init; } = 10000;

    /// <summary>
    /// Maximum channels per user.
    /// </summary>
    [Range(1, 1000)]
    public int MaxChannelsPerUser { get; init; } = 50;

    /// <summary>
    /// Maximum nickname length.
    /// </summary>
    [Range(9, 50)]
    public int MaxNickLength { get; init; } = 30;

    /// <summary>
    /// Maximum channel name length.
    /// </summary>
    [Range(50, 200)]
    public int MaxChannelLength { get; init; } = 50;

    /// <summary>
    /// Maximum topic length.
    /// </summary>
    [Range(100, 1000)]
    public int MaxTopicLength { get; init; } = 390;

    /// <summary>
    /// Port configuration.
    /// </summary>
    public required PortConfigDto Ports { get; init; }

    /// <summary>
    /// TLS configuration.
    /// </summary>
    public required TlsConfigDto Tls { get; init; }
}

/// <summary>
/// Port configuration.
/// </summary>
public sealed class PortConfigDto
{
    /// <summary>
    /// TLS port (default: 6697).
    /// </summary>
    [Range(1, 65535)]
    public int TlsPort { get; init; } = 6697;

    /// <summary>
    /// WebSocket port (default: 8443).
    /// </summary>
    [Range(1, 65535)]
    public int WebSocketPort { get; init; } = 8443;

    /// <summary>
    /// Admin panel port (default: 9443).
    /// </summary>
    [Range(1, 65535)]
    public int AdminPort { get; init; } = 9443;

    /// <summary>
    /// Plaintext port (disabled by default, 0 = disabled).
    /// </summary>
    [Range(0, 65535)]
    public int PlaintextPort { get; init; } = 0;
}

/// <summary>
/// TLS configuration.
/// </summary>
public sealed class TlsConfigDto
{
    /// <summary>
    /// Path to certificate file (PFX/P12).
    /// </summary>
    public string? CertificatePath { get; init; }

    /// <summary>
    /// Certificate password (write-only, never returned).
    /// </summary>
    public string? CertificatePassword { get; init; }

    /// <summary>
    /// Whether a valid certificate is configured.
    /// </summary>
    public bool HasValidCertificate { get; init; }

    /// <summary>
    /// Certificate subject name.
    /// </summary>
    public string? CertificateSubject { get; init; }

    /// <summary>
    /// Certificate expiry date.
    /// </summary>
    public DateTimeOffset? CertificateExpiry { get; init; }

    /// <summary>
    /// Whether to use Let's Encrypt auto-renewal.
    /// </summary>
    public bool UseLetsEncrypt { get; init; }

    /// <summary>
    /// Let's Encrypt email for certificate notifications.
    /// </summary>
    [EmailAddress]
    public string? LetsEncryptEmail { get; init; }
}

#endregion

#region Setup Wizard

/// <summary>
/// Setup wizard step information.
/// </summary>
public sealed class SetupStepDto
{
    /// <summary>
    /// Current step number (1-based).
    /// </summary>
    public required int CurrentStep { get; init; }

    /// <summary>
    /// Total number of steps.
    /// </summary>
    public required int TotalSteps { get; init; }

    /// <summary>
    /// Step title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Step description.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Whether this step is complete.
    /// </summary>
    public required bool IsComplete { get; init; }

    /// <summary>
    /// Whether setup is fully complete.
    /// </summary>
    public required bool SetupComplete { get; init; }
}

/// <summary>
/// Setup wizard state.
/// </summary>
public sealed class SetupStateDto
{
    /// <summary>
    /// Whether initial setup has been completed.
    /// </summary>
    public required bool IsConfigured { get; init; }

    /// <summary>
    /// Current setup step if in progress.
    /// </summary>
    public SetupStepDto? CurrentStep { get; init; }

    /// <summary>
    /// All setup steps.
    /// </summary>
    public required SetupStepDto[] Steps { get; init; }
}

/// <summary>
/// Server configuration for setup wizard.
/// </summary>
public sealed class SetupServerRequest
{
    /// <summary>
    /// Server name (FQDN).
    /// </summary>
    [Required]
    [StringLength(253, MinimumLength = 1)]
    [RegularExpression(@"^[a-zA-Z0-9][a-zA-Z0-9\-\.]*[a-zA-Z0-9]$")]
    public required string ServerName { get; init; }

    /// <summary>
    /// Network name.
    /// </summary>
    [Required]
    [StringLength(50, MinimumLength = 1)]
    public required string NetworkName { get; init; }

    /// <summary>
    /// Server description.
    /// </summary>
    [StringLength(500)]
    public string? Description { get; init; }

    /// <summary>
    /// Administrator email.
    /// </summary>
    [EmailAddress]
    public string? AdminEmail { get; init; }
}

/// <summary>
/// TLS setup request.
/// </summary>
public sealed class SetupTlsRequest
{
    /// <summary>
    /// TLS setup method.
    /// </summary>
    [Required]
    public required TlsSetupMethod Method { get; init; }

    /// <summary>
    /// Certificate file content (base64) for Upload method.
    /// </summary>
    public string? CertificateBase64 { get; init; }

    /// <summary>
    /// Certificate password for Upload method.
    /// </summary>
    public string? CertificatePassword { get; init; }

    /// <summary>
    /// Email for Let's Encrypt method.
    /// </summary>
    [EmailAddress]
    public string? LetsEncryptEmail { get; init; }

    /// <summary>
    /// Domain names for Let's Encrypt.
    /// </summary>
    public string[]? LetsEncryptDomains { get; init; }
}

/// <summary>
/// TLS setup method.
/// </summary>
public enum TlsSetupMethod
{
    /// <summary>
    /// Upload existing certificate.
    /// </summary>
    Upload,

    /// <summary>
    /// Use Let's Encrypt auto-renewal.
    /// </summary>
    LetsEncrypt,

    /// <summary>
    /// Generate self-signed certificate (development only).
    /// </summary>
    SelfSigned,

    /// <summary>
    /// Skip TLS setup (not recommended).
    /// </summary>
    Skip
}

/// <summary>
/// Database setup request.
/// </summary>
public sealed class SetupDatabaseRequest
{
    /// <summary>
    /// Database host.
    /// </summary>
    [Required]
    public required string Host { get; init; }

    /// <summary>
    /// Database port.
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; init; } = 5432;

    /// <summary>
    /// Database name.
    /// </summary>
    [Required]
    public required string Database { get; init; }

    /// <summary>
    /// Database username.
    /// </summary>
    [Required]
    public required string Username { get; init; }

    /// <summary>
    /// Database password.
    /// </summary>
    [Required]
    public required string Password { get; init; }

    /// <summary>
    /// Whether to use SSL.
    /// </summary>
    public bool UseSsl { get; init; } = true;
}

/// <summary>
/// Database connection test result.
/// </summary>
public sealed class DatabaseTestResultDto
{
    /// <summary>
    /// Whether connection was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// PostgreSQL version if connected.
    /// </summary>
    public string? PostgresVersion { get; init; }

    /// <summary>
    /// Whether database exists.
    /// </summary>
    public bool DatabaseExists { get; init; }

    /// <summary>
    /// Whether tables need to be created.
    /// </summary>
    public bool NeedsMigration { get; init; }
}

/// <summary>
/// Admin user setup request.
/// </summary>
public sealed class SetupAdminRequest
{
    /// <summary>
    /// Administrator username.
    /// </summary>
    [Required]
    [StringLength(50, MinimumLength = 3)]
    [RegularExpression(@"^[a-zA-Z][a-zA-Z0-9_\-]*$")]
    public required string Username { get; init; }

    /// <summary>
    /// Administrator password.
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 12)]
    public required string Password { get; init; }

    /// <summary>
    /// Confirm password.
    /// </summary>
    [Required]
    [Compare(nameof(Password))]
    public required string ConfirmPassword { get; init; }

    /// <summary>
    /// Administrator email.
    /// </summary>
    [Required]
    [EmailAddress]
    public required string Email { get; init; }

    /// <summary>
    /// IRC operator name (for server oper access).
    /// </summary>
    [StringLength(50, MinimumLength = 1)]
    public string? IrcOperName { get; init; }
}

#endregion

#region Users

/// <summary>
/// Connected user information.
/// </summary>
public sealed class UserDto
{
    /// <summary>
    /// User's nickname.
    /// </summary>
    public required string Nickname { get; init; }

    /// <summary>
    /// User's username (ident).
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// User's hostname (cloaked).
    /// </summary>
    public required string Hostname { get; init; }

    /// <summary>
    /// User's real name (gecos).
    /// </summary>
    public required string RealName { get; init; }

    /// <summary>
    /// Account name if logged in.
    /// </summary>
    public string? Account { get; init; }

    /// <summary>
    /// Whether user is an operator.
    /// </summary>
    public required bool IsOperator { get; init; }

    /// <summary>
    /// User modes.
    /// </summary>
    public required string Modes { get; init; }

    /// <summary>
    /// Channels the user is in.
    /// </summary>
    public required string[] Channels { get; init; }

    /// <summary>
    /// Connection time.
    /// </summary>
    public required DateTimeOffset ConnectedAt { get; init; }

    /// <summary>
    /// Last activity time.
    /// </summary>
    public required DateTimeOffset LastActivity { get; init; }

    /// <summary>
    /// Whether user is away.
    /// </summary>
    public required bool IsAway { get; init; }

    /// <summary>
    /// Away message if away.
    /// </summary>
    public string? AwayMessage { get; init; }

    /// <summary>
    /// Whether connection is secure (TLS).
    /// </summary>
    public required bool IsSecure { get; init; }

    /// <summary>
    /// Real IP address (admin only).
    /// </summary>
    public string? RealIp { get; init; }
}

/// <summary>
/// Paginated list response.
/// </summary>
public sealed class PagedResult<T>
{
    /// <summary>
    /// Items in this page.
    /// </summary>
    public required T[] Items { get; init; }

    /// <summary>
    /// Total count of all items.
    /// </summary>
    public required int TotalCount { get; init; }

    /// <summary>
    /// Current page (1-based).
    /// </summary>
    public required int Page { get; init; }

    /// <summary>
    /// Page size.
    /// </summary>
    public required int PageSize { get; init; }

    /// <summary>
    /// Total pages.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

#endregion

#region Channels

/// <summary>
/// Channel information.
/// </summary>
public sealed class ChannelDto
{
    /// <summary>
    /// Channel name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Channel topic.
    /// </summary>
    public string? Topic { get; init; }

    /// <summary>
    /// Who set the topic.
    /// </summary>
    public string? TopicSetBy { get; init; }

    /// <summary>
    /// When the topic was set.
    /// </summary>
    public DateTimeOffset? TopicSetAt { get; init; }

    /// <summary>
    /// Channel modes.
    /// </summary>
    public required string Modes { get; init; }

    /// <summary>
    /// Number of users in channel.
    /// </summary>
    public required int UserCount { get; init; }

    /// <summary>
    /// When the channel was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Whether the channel is registered.
    /// </summary>
    public required bool IsRegistered { get; init; }

    /// <summary>
    /// Channel founder (if registered).
    /// </summary>
    public string? Founder { get; init; }
}

/// <summary>
/// Channel member information.
/// </summary>
public sealed class ChannelMemberDto
{
    /// <summary>
    /// User nickname.
    /// </summary>
    public required string Nickname { get; init; }

    /// <summary>
    /// Channel prefix modes (e.g., @, +).
    /// </summary>
    public required string Prefixes { get; init; }

    /// <summary>
    /// When the user joined.
    /// </summary>
    public required DateTimeOffset JoinedAt { get; init; }

    /// <summary>
    /// Whether user is away.
    /// </summary>
    public required bool IsAway { get; init; }
}

#endregion

#region Operators

/// <summary>
/// IRC Operator definition.
/// </summary>
public sealed class OperatorDto
{
    /// <summary>
    /// Operator name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Operator class/type.
    /// </summary>
    public required string OperClass { get; init; }

    /// <summary>
    /// Allowed hostmasks.
    /// </summary>
    public required string[] Hostmasks { get; init; }

    /// <summary>
    /// Whether operator is currently online.
    /// </summary>
    public required bool IsOnline { get; init; }

    /// <summary>
    /// Last seen time.
    /// </summary>
    public DateTimeOffset? LastSeen { get; init; }

    /// <summary>
    /// Permissions/flags.
    /// </summary>
    public required string[] Permissions { get; init; }
}

/// <summary>
/// Create/update operator request.
/// </summary>
public sealed class OperatorRequest
{
    /// <summary>
    /// Operator name.
    /// </summary>
    [Required]
    [StringLength(50, MinimumLength = 1)]
    public required string Name { get; init; }

    /// <summary>
    /// Operator password.
    /// </summary>
    [StringLength(100, MinimumLength = 12)]
    public string? Password { get; init; }

    /// <summary>
    /// Operator class.
    /// </summary>
    [Required]
    public required string OperClass { get; init; }

    /// <summary>
    /// Allowed hostmasks.
    /// </summary>
    public string[]? Hostmasks { get; init; }
}

#endregion

#region Bans

/// <summary>
/// Server ban (K-line, G-line, etc.).
/// </summary>
public sealed class ServerBanDto
{
    /// <summary>
    /// Ban ID.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Ban type.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Ban mask (user@host or IP).
    /// </summary>
    public required string Mask { get; init; }

    /// <summary>
    /// Reason for ban.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Who set the ban.
    /// </summary>
    public required string SetBy { get; init; }

    /// <summary>
    /// When the ban was set.
    /// </summary>
    public required DateTimeOffset SetAt { get; init; }

    /// <summary>
    /// When the ban expires (null = permanent).
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Number of users affected.
    /// </summary>
    public required int AffectedCount { get; init; }
}

/// <summary>
/// Create ban request.
/// </summary>
public sealed class CreateBanRequest
{
    /// <summary>
    /// Ban type (KLINE, GLINE, ZLINE, etc.).
    /// </summary>
    [Required]
    public required string Type { get; init; }

    /// <summary>
    /// Ban mask.
    /// </summary>
    [Required]
    public required string Mask { get; init; }

    /// <summary>
    /// Ban reason.
    /// </summary>
    [Required]
    [StringLength(300, MinimumLength = 1)]
    public required string Reason { get; init; }

    /// <summary>
    /// Duration in seconds (null = permanent).
    /// </summary>
    [Range(0, int.MaxValue)]
    public int? DurationSeconds { get; init; }
}

#endregion

#region API Response Wrappers

/// <summary>
/// Standard API response wrapper.
/// </summary>
public sealed class ApiResponse<T>
{
    /// <summary>
    /// Whether the request was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Response data.
    /// </summary>
    public T? Data { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Validation errors if any.
    /// </summary>
    public Dictionary<string, string[]>? ValidationErrors { get; init; }

    /// <summary>
    /// Creates a success response.
    /// </summary>
#pragma warning disable CA1000 // Do not declare static members on generic types - factory pattern
    public static ApiResponse<T> Ok(T data) => new() { Success = true, Data = data };
#pragma warning restore CA1000

    /// <summary>
    /// Creates an error response.
    /// </summary>
#pragma warning disable CA1000 // Do not declare static members on generic types - factory pattern
    public static ApiResponse<T> Fail(string error) => new() { Success = false, Error = error };
#pragma warning restore CA1000
}

/// <summary>
/// API response without data.
/// </summary>
public sealed class ApiResponse
{
    /// <summary>
    /// Whether the request was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Creates a success response.
    /// </summary>
    public static ApiResponse Ok(string? message = null) => new() { Success = true, Message = message };

    /// <summary>
    /// Creates an error response.
    /// </summary>
    public static ApiResponse Fail(string error) => new() { Success = false, Error = error };
}

#endregion

#region Logging and Real-time

/// <summary>
/// Log entry DTO for real-time log streaming.
/// </summary>
public sealed class LogEntryDto
{
    /// <summary>
    /// Unique identifier for the log entry.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Timestamp when the log was created.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Log level (Trace, Debug, Information, Warning, Error, Critical).
    /// </summary>
    public required string Level { get; init; }

    /// <summary>
    /// The log message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Exception details if present.
    /// </summary>
    public string? Exception { get; init; }

    /// <summary>
    /// Source context (logger name/class).
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Additional structured properties.
    /// </summary>
    public Dictionary<string, string>? Properties { get; init; }
}

/// <summary>
/// Response containing log entries.
/// </summary>
public sealed class LogsResponse
{
    /// <summary>
    /// List of log entries.
    /// </summary>
    public required List<LogEntryDto> Entries { get; init; }

    /// <summary>
    /// Total count of entries in this response.
    /// </summary>
    public required int TotalCount { get; init; }

    /// <summary>
    /// Whether more entries are available.
    /// </summary>
    public required bool HasMore { get; init; }
}

/// <summary>
/// Information about a log file.
/// </summary>
public sealed class LogFileInfo
{
    /// <summary>
    /// File name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Relative path to the file.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Last modification time.
    /// </summary>
    public required DateTime LastModified { get; init; }

    /// <summary>
    /// File creation time.
    /// </summary>
    public required DateTime Created { get; init; }
}

/// <summary>
/// Response containing list of log files.
/// </summary>
public sealed class LogFilesResponse
{
    /// <summary>
    /// List of log files.
    /// </summary>
    public required List<LogFileInfo> Files { get; init; }

    /// <summary>
    /// Log directory path.
    /// </summary>
    public required string LogDirectory { get; init; }
}

/// <summary>
/// Response containing log file content.
/// </summary>
public sealed class LogFileContentResponse
{
    /// <summary>
    /// File name.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Content of the log file.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Byte offset from which content was read.
    /// </summary>
    public required long Offset { get; init; }

    /// <summary>
    /// Number of bytes read.
    /// </summary>
    public required int BytesRead { get; init; }

    /// <summary>
    /// Total file size in bytes.
    /// </summary>
    public required long TotalSize { get; init; }

    /// <summary>
    /// Whether more content is available.
    /// </summary>
    public required bool HasMore { get; init; }
}

/// <summary>
/// Response from log cleanup operation.
/// </summary>
public sealed class LogCleanupResponse
{
    /// <summary>
    /// Names of deleted files.
    /// </summary>
    public required List<string> DeletedFiles { get; init; }

    /// <summary>
    /// Number of files deleted.
    /// </summary>
    public required int DeletedCount { get; init; }

    /// <summary>
    /// Total bytes freed.
    /// </summary>
    public required long FreedBytes { get; init; }
}

/// <summary>
/// Real-time statistics DTO for WebSocket broadcasting.
/// </summary>
public sealed class RealTimeStatsDto
{
    /// <summary>
    /// Timestamp of the statistics snapshot.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Current number of connected users.
    /// </summary>
    public required int ConnectedUsers { get; init; }

    /// <summary>
    /// Current number of channels.
    /// </summary>
    public required int ChannelCount { get; init; }

    /// <summary>
    /// Current number of operators online.
    /// </summary>
    public required int OperatorsOnline { get; init; }

    /// <summary>
    /// Messages processed per second.
    /// </summary>
    public required double MessagesPerSecond { get; init; }

    /// <summary>
    /// Incoming bytes per second.
    /// </summary>
    public required double BytesInPerSecond { get; init; }

    /// <summary>
    /// Outgoing bytes per second.
    /// </summary>
    public required double BytesOutPerSecond { get; init; }

    /// <summary>
    /// Memory usage in megabytes.
    /// </summary>
    public required double MemoryUsageMb { get; init; }

    /// <summary>
    /// CPU usage percentage.
    /// </summary>
    public required double CpuUsagePercent { get; init; }

    /// <summary>
    /// Number of active connections.
    /// </summary>
    public required int ActiveConnections { get; init; }

    /// <summary>
    /// Number of pending operations.
    /// </summary>
    public required int PendingOperations { get; init; }
}

/// <summary>
/// User event for real-time notifications.
/// </summary>
public sealed class UserEventDto
{
    /// <summary>
    /// Event type (Connected, Disconnected, NickChange, Join, Part, Quit).
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// Timestamp of the event.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// User nickname.
    /// </summary>
    public required string Nickname { get; init; }

    /// <summary>
    /// User identifier.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// User's hostname (cloaked).
    /// </summary>
    public string? Hostname { get; init; }

    /// <summary>
    /// Associated channel if applicable.
    /// </summary>
    public string? Channel { get; init; }

    /// <summary>
    /// Additional details (e.g., quit message, old nickname).
    /// </summary>
    public string? Details { get; init; }
}

/// <summary>
/// Admin notification for real-time alerts.
/// </summary>
public sealed class AdminNotificationDto
{
    /// <summary>
    /// Notification type (Info, Warning, Error, Success).
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Notification title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Notification message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Timestamp of the notification.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Whether the notification should persist until dismissed.
    /// </summary>
    public bool Persistent { get; init; }

    /// <summary>
    /// Optional action URL.
    /// </summary>
    public string? ActionUrl { get; init; }
}

#endregion
