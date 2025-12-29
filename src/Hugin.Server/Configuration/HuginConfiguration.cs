namespace Hugin.Server.Configuration;

/// <summary>
/// Root configuration for Hugin IRC server.
/// </summary>
public sealed class HuginConfiguration
{
    /// <summary>
    /// Server identity configuration.
    /// </summary>
    public ServerIdentity Server { get; set; } = new();

    /// <summary>
    /// Network configuration.
    /// </summary>
    public NetworkConfiguration Network { get; set; } = new();

    /// <summary>
    /// Security configuration.
    /// </summary>
    public SecurityConfiguration Security { get; set; } = new();

    /// <summary>
    /// Database configuration.
    /// </summary>
    public DatabaseConfiguration Database { get; set; } = new();

    /// <summary>
    /// Limits configuration.
    /// </summary>
    public LimitsConfiguration Limits { get; set; } = new();

    /// <summary>
    /// Logging configuration.
    /// </summary>
    public LoggingConfiguration Logging { get; set; } = new();

    /// <summary>
    /// MOTD lines.
    /// </summary>
    public List<string> Motd { get; set; } = new()
    {
        "Welcome to Hugin IRC Server",
        "A modern, secure IRC implementation",
        "",
        "Please follow the network rules.",
        "Type /MOTD to see this message again."
    };
}

public sealed class ServerIdentity
{
    /// <summary>
    /// Server name (used in messages).
    /// </summary>
    public string Name { get; set; } = "irc.hugin.local";

    /// <summary>
    /// Server ID (3 characters) for S2S.
    /// </summary>
    public string Sid { get; set; } = "001";

    /// <summary>
    /// Server description.
    /// </summary>
    public string Description { get; set; } = "Hugin IRC Server";

    /// <summary>
    /// Network name.
    /// </summary>
    public string NetworkName { get; set; } = "HuginNet";

    /// <summary>
    /// Admin name.
    /// </summary>
    public string AdminName { get; set; } = "Administrator";

    /// <summary>
    /// Admin email.
    /// </summary>
    public string AdminEmail { get; set; } = "admin@hugin.local";
}

public sealed class NetworkConfiguration
{
    /// <summary>
    /// Client listeners.
    /// </summary>
    public List<ListenerConfiguration> Listeners { get; set; } = new()
    {
        new() { Address = "0.0.0.0", Port = 6697, Tls = true }
    };

    /// <summary>
    /// S2S listeners.
    /// </summary>
    public List<ListenerConfiguration> ServerListeners { get; set; } = new();

    /// <summary>
    /// Linked servers.
    /// </summary>
    public List<LinkedServerConfiguration> LinkedServers { get; set; } = new();
}

public sealed class ListenerConfiguration
{
    public string Address { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 6697;
    public bool Tls { get; set; } = true;
}

public sealed class LinkedServerConfiguration
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 6697;
    public string Password { get; set; } = string.Empty;
    public bool AutoConnect { get; set; } = true;
}

public sealed class SecurityConfiguration
{
    /// <summary>
    /// Path to TLS certificate (PFX).
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// Certificate password (encrypted).
    /// </summary>
    public string? CertificatePassword { get; set; }

    /// <summary>
    /// Whether to generate a self-signed certificate if none provided.
    /// </summary>
    public bool GenerateSelfSignedCertificate { get; set; } = true;

    /// <summary>
    /// Require TLS for all connections.
    /// </summary>
    public bool RequireTls { get; set; } = true;

    /// <summary>
    /// Enable STS (Strict Transport Security).
    /// </summary>
    public bool EnableSts { get; set; } = true;

    /// <summary>
    /// STS duration in seconds.
    /// </summary>
    public int StsDuration { get; set; } = 31536000; // 1 year

    /// <summary>
    /// Secret for hostname cloaking.
    /// </summary>
    public string CloakSecret { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Cloak suffix.
    /// </summary>
    public string CloakSuffix { get; set; } = "hugin.cloak";

    /// <summary>
    /// Rate limiting configuration.
    /// </summary>
    public Hugin.Security.RateLimitConfiguration RateLimiting { get; set; } = new();
}

public sealed class DatabaseConfiguration
{
    /// <summary>
    /// PostgreSQL connection string.
    /// </summary>
    public string ConnectionString { get; set; } = "Host=localhost;Database=hugin;Username=hugin;Password=hugin";

    /// <summary>
    /// Whether to run migrations on startup.
    /// </summary>
    public bool RunMigrationsOnStartup { get; set; } = true;

    /// <summary>
    /// Message retention in days (0 = forever).
    /// </summary>
    public int MessageRetentionDays { get; set; } = 30;
}

public sealed class LimitsConfiguration
{
    /// <summary>
    /// Maximum nickname length.
    /// </summary>
    public int MaxNickLength { get; set; } = 30;

    /// <summary>
    /// Maximum channel name length.
    /// </summary>
    public int MaxChannelLength { get; set; } = 50;

    /// <summary>
    /// Maximum topic length.
    /// </summary>
    public int MaxTopicLength { get; set; } = 390;

    /// <summary>
    /// Maximum kick reason length.
    /// </summary>
    public int MaxKickLength { get; set; } = 255;

    /// <summary>
    /// Maximum away message length.
    /// </summary>
    public int MaxAwayLength { get; set; } = 200;

    /// <summary>
    /// Maximum channels a user can join.
    /// </summary>
    public int MaxChannels { get; set; } = 50;

    /// <summary>
    /// Maximum targets for PRIVMSG/NOTICE.
    /// </summary>
    public int MaxTargets { get; set; } = 4;

    /// <summary>
    /// Ping timeout in seconds.
    /// </summary>
    public int PingTimeout { get; set; } = 180;

    /// <summary>
    /// Registration timeout in seconds.
    /// </summary>
    public int RegistrationTimeout { get; set; } = 60;
}

public sealed class LoggingConfiguration
{
    /// <summary>
    /// Minimum log level.
    /// </summary>
    public string MinimumLevel { get; set; } = "Information";

    /// <summary>
    /// Log file path.
    /// </summary>
    public string? FilePath { get; set; } = "logs/hugin-.log";

    /// <summary>
    /// Enable console logging.
    /// </summary>
    public bool EnableConsole { get; set; } = true;
}
