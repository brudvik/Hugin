// Hugin IRC Server - Setup Wizard Controller
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Hugin.Server.Api.Auth;
using Hugin.Server.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Hugin.Server.Api.Controllers;

/// <summary>
/// Setup wizard endpoints for initial server configuration.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class SetupController : ControllerBase
{
    private readonly ISetupService _setupService;
    private readonly IAdminUserService _userService;
    private readonly ILogger<SetupController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetupController"/> class.
    /// </summary>
    public SetupController(
        ISetupService setupService,
        IAdminUserService userService,
        ILogger<SetupController> logger)
    {
        _setupService = setupService;
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current setup state.
    /// </summary>
    [HttpGet("state")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<SetupStateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetState(CancellationToken cancellationToken)
    {
        var state = await _setupService.GetSetupStateAsync(cancellationToken);
        return Ok(ApiResponse<SetupStateDto>.Ok(state));
    }

    /// <summary>
    /// Checks if setup is required (no admin users exist).
    /// </summary>
    [HttpGet("required")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<SetupRequiredDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> IsSetupRequired(CancellationToken cancellationToken)
    {
        var hasAdmins = await _userService.AnyExistAsync(cancellationToken);
        var isConfigured = await _setupService.IsConfiguredAsync(cancellationToken);

        return Ok(ApiResponse<SetupRequiredDto>.Ok(new SetupRequiredDto
        {
            SetupRequired = !hasAdmins || !isConfigured,
            HasAdminUser = hasAdmins,
            HasConfiguration = isConfigured
        }));
    }

    /// <summary>
    /// Step 1: Configure basic server settings.
    /// </summary>
    [HttpPost("server")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfigureServer([FromBody] SetupServerRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse.Fail("Invalid server configuration"));
        }

        try
        {
            await _setupService.ConfigureServerAsync(request, cancellationToken);
            _logger.LogInformation("Server configured: {ServerName} on {NetworkName}", 
                request.ServerName, request.NetworkName);

            return Ok(ApiResponse.Ok("Server configuration saved"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure server");
            return BadRequest(ApiResponse.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Step 2: Configure TLS/SSL.
    /// </summary>
    [HttpPost("tls")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TlsSetupResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfigureTls([FromBody] SetupTlsRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse.Fail("Invalid TLS configuration"));
        }

        try
        {
            var result = await _setupService.ConfigureTlsAsync(request, cancellationToken);
            return Ok(ApiResponse<TlsSetupResultDto>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure TLS");
            return BadRequest(ApiResponse.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Step 3: Configure database connection.
    /// </summary>
    [HttpPost("database")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<DatabaseTestResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfigureDatabase([FromBody] SetupDatabaseRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse.Fail("Invalid database configuration"));
        }

        try
        {
            var result = await _setupService.ConfigureDatabaseAsync(request, cancellationToken);
            return Ok(ApiResponse<DatabaseTestResultDto>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure database");
            return BadRequest(ApiResponse.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Test database connection without saving.
    /// </summary>
    [HttpPost("database/test")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<DatabaseTestResultDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestDatabase([FromBody] SetupDatabaseRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _setupService.TestDatabaseConnectionAsync(request, cancellationToken);
            return Ok(ApiResponse<DatabaseTestResultDto>.Ok(result));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DatabaseTestResultDto>.Ok(new DatabaseTestResultDto
            {
                Success = false,
                Error = ex.Message
            }));
        }
    }

    /// <summary>
    /// Step 4: Create the first admin user.
    /// </summary>
    [HttpPost("admin")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAdmin([FromBody] SetupAdminRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse.Fail("Invalid admin configuration"));
        }

        // Check if admin already exists
        var hasAdmins = await _userService.AnyExistAsync(cancellationToken);
        if (hasAdmins)
        {
            return BadRequest(ApiResponse.Fail("An admin user already exists. Please login."));
        }

        try
        {
            await _userService.CreateAsync(
                request.Username,
                request.Password,
                request.Email,
                [AdminRoles.Admin],
                cancellationToken);

            _logger.LogInformation("Created admin user: {Username}", request.Username);

            return Ok(ApiResponse.Ok("Admin user created successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create admin user");
            return BadRequest(ApiResponse.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Step 5: Finalize setup.
    /// </summary>
    [HttpPost("complete")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CompleteSetup(CancellationToken cancellationToken)
    {
        try
        {
            await _setupService.CompleteSetupAsync(cancellationToken);
            _logger.LogInformation("Setup completed successfully");

            return Ok(ApiResponse.Ok("Setup completed! You can now login to the admin panel."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete setup");
            return BadRequest(ApiResponse.Fail(ex.Message));
        }
    }
}

/// <summary>
/// DTO for setup required check.
/// </summary>
public sealed class SetupRequiredDto
{
    /// <summary>
    /// Whether setup is required.
    /// </summary>
    public required bool SetupRequired { get; init; }

    /// <summary>
    /// Whether an admin user exists.
    /// </summary>
    public required bool HasAdminUser { get; init; }

    /// <summary>
    /// Whether configuration exists.
    /// </summary>
    public required bool HasConfiguration { get; init; }
}

/// <summary>
/// TLS setup result.
/// </summary>
public sealed class TlsSetupResultDto
{
    /// <summary>
    /// Whether TLS was configured successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Certificate subject name.
    /// </summary>
    public string? CertificateSubject { get; init; }

    /// <summary>
    /// Certificate expiry date.
    /// </summary>
    public DateTimeOffset? CertificateExpiry { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Warning message (e.g., self-signed certificate).
    /// </summary>
    public string? Warning { get; init; }
}

/// <summary>
/// Service for managing server setup.
/// </summary>
public interface ISetupService
{
    /// <summary>
    /// Gets the current setup state.
    /// </summary>
    Task<SetupStateDto> GetSetupStateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Checks if the server is configured.
    /// </summary>
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Configures basic server settings.
    /// </summary>
    Task ConfigureServerAsync(SetupServerRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Configures TLS/SSL.
    /// </summary>
    Task<TlsSetupResultDto> ConfigureTlsAsync(SetupTlsRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Configures database connection.
    /// </summary>
    Task<DatabaseTestResultDto> ConfigureDatabaseAsync(SetupDatabaseRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Tests database connection without saving.
    /// </summary>
    Task<DatabaseTestResultDto> TestDatabaseConnectionAsync(SetupDatabaseRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Completes the setup process.
    /// </summary>
    Task CompleteSetupAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Setup service implementation.
/// </summary>
public sealed class SetupService : ISetupService
{
    private readonly ILogger<SetupService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _configPath;

    private bool _serverConfigured;
    private bool _tlsConfigured;
    private bool _databaseConfigured;
    private bool _setupComplete;

    private SetupServerRequest? _serverConfig;
    private SetupDatabaseRequest? _databaseConfig;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetupService"/> class.
    /// </summary>
    public SetupService(ILogger<SetupService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        // Check if already configured
        _setupComplete = File.Exists(Path.Combine(AppContext.BaseDirectory, ".setup-complete"));
    }

    /// <inheritdoc />
    public Task<SetupStateDto> GetSetupStateAsync(CancellationToken cancellationToken)
    {
        var steps = new[]
        {
            new SetupStepDto
            {
                CurrentStep = 1,
                TotalSteps = 5,
                Title = "Server Configuration",
                Description = "Set your server name, network name, and basic settings",
                IsComplete = _serverConfigured,
                SetupComplete = _setupComplete
            },
            new SetupStepDto
            {
                CurrentStep = 2,
                TotalSteps = 5,
                Title = "TLS/SSL Certificate",
                Description = "Configure secure connections with TLS",
                IsComplete = _tlsConfigured,
                SetupComplete = _setupComplete
            },
            new SetupStepDto
            {
                CurrentStep = 3,
                TotalSteps = 5,
                Title = "Database",
                Description = "Connect to PostgreSQL for persistent storage",
                IsComplete = _databaseConfigured,
                SetupComplete = _setupComplete
            },
            new SetupStepDto
            {
                CurrentStep = 4,
                TotalSteps = 5,
                Title = "Administrator",
                Description = "Create your admin account",
                IsComplete = false, // Checked separately
                SetupComplete = _setupComplete
            },
            new SetupStepDto
            {
                CurrentStep = 5,
                TotalSteps = 5,
                Title = "Complete",
                Description = "Review and finish setup",
                IsComplete = _setupComplete,
                SetupComplete = _setupComplete
            }
        };

        var currentStep = steps.FirstOrDefault(s => !s.IsComplete) ?? steps[^1];

        return Task.FromResult(new SetupStateDto
        {
            IsConfigured = _setupComplete,
            CurrentStep = currentStep,
            Steps = steps
        });
    }

    /// <inheritdoc />
    public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_setupComplete);
    }

    /// <inheritdoc />
    public Task ConfigureServerAsync(SetupServerRequest request, CancellationToken cancellationToken)
    {
        _serverConfig = request;
        _serverConfigured = true;
        _logger.LogInformation("Server configuration saved: {ServerName}", request.ServerName);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<TlsSetupResultDto> ConfigureTlsAsync(SetupTlsRequest request, CancellationToken cancellationToken)
    {
        switch (request.Method)
        {
            case TlsSetupMethod.Upload:
                if (string.IsNullOrEmpty(request.CertificateBase64))
                {
                    return new TlsSetupResultDto { Success = false, Error = "Certificate file is required" };
                }

                try
                {
                    var certBytes = Convert.FromBase64String(request.CertificateBase64);
                    var cert = new X509Certificate2(certBytes, request.CertificatePassword);

                    // Save certificate to disk
                    var certPath = Path.Combine(AppContext.BaseDirectory, "server.pfx");
                    await File.WriteAllBytesAsync(certPath, certBytes, cancellationToken);

                    _tlsConfigured = true;

                    return new TlsSetupResultDto
                    {
                        Success = true,
                        CertificateSubject = cert.Subject,
                        CertificateExpiry = cert.NotAfter
                    };
                }
                catch (Exception ex)
                {
                    return new TlsSetupResultDto { Success = false, Error = $"Invalid certificate: {ex.Message}" };
                }

            case TlsSetupMethod.SelfSigned:
                // Generate self-signed certificate
                var selfSignedCert = GenerateSelfSignedCertificate(_serverConfig?.ServerName ?? "localhost");
                var selfSignedPath = Path.Combine(AppContext.BaseDirectory, "server.pfx");
                await File.WriteAllBytesAsync(selfSignedPath, selfSignedCert.Export(X509ContentType.Pfx), cancellationToken);

                _tlsConfigured = true;

                return new TlsSetupResultDto
                {
                    Success = true,
                    CertificateSubject = selfSignedCert.Subject,
                    CertificateExpiry = selfSignedCert.NotAfter,
                    Warning = "Self-signed certificates are not trusted by clients. Use only for development."
                };

            case TlsSetupMethod.LetsEncrypt:
                // TODO: Implement Let's Encrypt integration
                return new TlsSetupResultDto 
                { 
                    Success = false, 
                    Error = "Let's Encrypt integration is not yet implemented" 
                };

            case TlsSetupMethod.Skip:
                _tlsConfigured = true;
                return new TlsSetupResultDto
                {
                    Success = true,
                    Warning = "TLS is disabled. This is not recommended for production use."
                };

            default:
                return new TlsSetupResultDto { Success = false, Error = "Unknown TLS setup method" };
        }
    }

    /// <inheritdoc />
    public async Task<DatabaseTestResultDto> ConfigureDatabaseAsync(SetupDatabaseRequest request, CancellationToken cancellationToken)
    {
        var result = await TestDatabaseConnectionAsync(request, cancellationToken);

        if (result.Success)
        {
            _databaseConfig = request;
            _databaseConfigured = true;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<DatabaseTestResultDto> TestDatabaseConnectionAsync(SetupDatabaseRequest request, CancellationToken cancellationToken)
    {
        var connectionString = BuildConnectionString(request);

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            // Get PostgreSQL version
            await using var cmd = new NpgsqlCommand("SELECT version()", connection);
            var version = await cmd.ExecuteScalarAsync(cancellationToken) as string;

            return new DatabaseTestResultDto
            {
                Success = true,
                PostgresVersion = version,
                DatabaseExists = true,
                NeedsMigration = true // TODO: Check if migrations are needed
            };
        }
        catch (PostgresException ex) when (ex.SqlState == "3D000") // Database does not exist
        {
            return new DatabaseTestResultDto
            {
                Success = true,
                DatabaseExists = false,
                NeedsMigration = true,
                Error = "Database does not exist but connection to server succeeded. It will be created."
            };
        }
        catch (Exception ex)
        {
            return new DatabaseTestResultDto
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public async Task CompleteSetupAsync(CancellationToken cancellationToken)
    {
        if (!_serverConfigured)
        {
            throw new InvalidOperationException("Server configuration is not complete");
        }

        // Generate configuration file
        var config = GenerateConfiguration();
        await File.WriteAllTextAsync(_configPath, config, cancellationToken);

        // Mark setup as complete
        var markerPath = Path.Combine(AppContext.BaseDirectory, ".setup-complete");
        await File.WriteAllTextAsync(markerPath, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);

        _setupComplete = true;
        _logger.LogInformation("Setup completed. Configuration saved to {Path}", _configPath);
    }

    private static string BuildConnectionString(SetupDatabaseRequest request)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = request.Host,
            Port = request.Port,
            Database = request.Database,
            Username = request.Username,
            Password = request.Password,
            SslMode = request.UseSsl ? SslMode.Require : SslMode.Disable
        };

        return builder.ConnectionString;
    }

    private string GenerateConfiguration()
    {
        var config = new
        {
            Hugin = new
            {
                Server = new
                {
                    Name = _serverConfig?.ServerName ?? "irc.example.com",
                    NetworkName = _serverConfig?.NetworkName ?? "HuginNet",
                    Description = _serverConfig?.Description ?? "A Hugin IRC Server",
                    AdminEmail = _serverConfig?.AdminEmail ?? ""
                },
                Database = _databaseConfig != null ? new
                {
                    ConnectionString = BuildConnectionString(_databaseConfig)
                } : null,
                Security = new
                {
                    CertificatePath = _tlsConfigured ? "server.pfx" : (string?)null
                }
            }
        };

        return System.Text.Json.JsonSerializer.Serialize(config, s_jsonOptions);
    }

    private static readonly System.Text.Json.JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true
    };

    private static X509Certificate2 GenerateSelfSignedCertificate(string subjectName)
    {
        using var rsa = System.Security.Cryptography.RSA.Create(4096);
        var request = new CertificateRequest(
            $"CN={subjectName}",
            rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Authentication
                critical: false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(subjectName);
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(2));

        return new X509Certificate2(
            certificate.Export(X509ContentType.Pfx),
            (string?)null,
            X509KeyStorageFlags.Exportable);
    }
}
