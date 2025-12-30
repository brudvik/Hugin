// Hugin IRC Server - Configuration Controller
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using System.Text.Json;
using Hugin.Server.Api.Auth;
using Hugin.Server.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hugin.Server.Api.Controllers;

/// <summary>
/// Cached JSON serializer options for configuration serialization.
/// </summary>
internal static class ConfigJsonOptions
{
    /// <summary>
    /// Options for reading configuration (case-insensitive).
    /// </summary>
    public static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Options for writing configuration (indented).
    /// </summary>
    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };
}

/// <summary>
/// Server configuration management endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = AdminRoles.Admin)]
public sealed class ConfigController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigController"/> class.
    /// </summary>
    public ConfigController(
        IConfiguration configuration,
        ILogger<ConfigController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current server configuration.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ServerConfigDto>), StatusCodes.Status200OK)]
    public IActionResult GetConfiguration()
    {
        var config = new ServerConfigDto
        {
            ServerName = _configuration["Hugin:Server:Name"] ?? "irc.example.com",
            NetworkName = _configuration["Hugin:Server:NetworkName"] ?? "HuginNet",
            Description = _configuration["Hugin:Server:Description"],
            AdminEmail = _configuration["Hugin:Server:AdminEmail"],
            MaxUsers = _configuration.GetValue("Hugin:Limits:MaxUsers", 10000),
            MaxChannelsPerUser = _configuration.GetValue("Hugin:Limits:MaxChannelsPerUser", 50),
            MaxNickLength = _configuration.GetValue("Hugin:Limits:MaxNickLength", 30),
            MaxChannelLength = _configuration.GetValue("Hugin:Limits:MaxChannelLength", 50),
            MaxTopicLength = _configuration.GetValue("Hugin:Limits:MaxTopicLength", 390),
            Ports = new PortConfigDto
            {
                TlsPort = _configuration.GetValue("Hugin:Ports:Tls", 6697),
                WebSocketPort = _configuration.GetValue("Hugin:Ports:WebSocket", 8443),
                AdminPort = _configuration.GetValue("Hugin:Ports:Admin", 9443),
                PlaintextPort = _configuration.GetValue("Hugin:Ports:Plaintext", 0)
            },
            Tls = new TlsConfigDto
            {
                CertificatePath = _configuration["Hugin:Security:CertificatePath"],
                HasValidCertificate = !string.IsNullOrEmpty(_configuration["Hugin:Security:CertificatePath"]),
                CertificateSubject = null, // Would need to load cert to get this
                CertificateExpiry = null,
                UseLetsEncrypt = _configuration.GetValue("Hugin:Security:UseLetsEncrypt", false),
                LetsEncryptEmail = _configuration["Hugin:Security:LetsEncryptEmail"]
            }
        };

        return Ok(ApiResponse<ServerConfigDto>.Ok(config));
    }

    /// <summary>
    /// Updates server configuration.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateConfiguration([FromBody] ServerConfigDto config, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse.Fail("Invalid configuration"));
        }

        try
        {
            // Read current appsettings.json
            var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!System.IO.File.Exists(appSettingsPath))
            {
                return BadRequest(ApiResponse.Fail("Configuration file not found"));
            }

            var json = await System.IO.File.ReadAllTextAsync(appSettingsPath, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            
            // Create a dictionary to modify
            var root = JsonSerializer.Deserialize<Dictionary<string, object>>(json, ConfigJsonOptions.ReadOptions) ?? new();

            // Update Hugin section
            var huginSection = new Dictionary<string, object>
            {
                ["Server"] = new Dictionary<string, object>
                {
                    ["Name"] = config.ServerName,
                    ["NetworkName"] = config.NetworkName,
                    ["Description"] = config.Description ?? "",
                    ["AdminEmail"] = config.AdminEmail ?? ""
                },
                ["Limits"] = new Dictionary<string, object>
                {
                    ["MaxUsers"] = config.MaxUsers,
                    ["MaxChannelsPerUser"] = config.MaxChannelsPerUser,
                    ["MaxNickLength"] = config.MaxNickLength,
                    ["MaxChannelLength"] = config.MaxChannelLength,
                    ["MaxTopicLength"] = config.MaxTopicLength
                },
                ["Ports"] = new Dictionary<string, object>
                {
                    ["Tls"] = config.Ports?.TlsPort ?? 6697,
                    ["WebSocket"] = config.Ports?.WebSocketPort ?? 8443,
                    ["Admin"] = config.Ports?.AdminPort ?? 9443,
                    ["Plaintext"] = config.Ports?.PlaintextPort ?? 0
                }
            };

            root["Hugin"] = huginSection;

            // Write back
            var updatedJson = JsonSerializer.Serialize(root, ConfigJsonOptions.WriteOptions);
            await System.IO.File.WriteAllTextAsync(appSettingsPath, updatedJson, cancellationToken);

            _logger.LogInformation("Configuration updated by {User}", User.Identity?.Name);

            return Ok(ApiResponse.Ok("Configuration updated. Some changes may require a server restart."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update configuration");
            return BadRequest(ApiResponse.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Gets rate limiting configuration.
    /// </summary>
    [HttpGet("ratelimits")]
    [ProducesResponseType(typeof(ApiResponse<RateLimitConfigDto>), StatusCodes.Status200OK)]
    public IActionResult GetRateLimits()
    {
        var config = new RateLimitConfigDto
        {
            ConnectionsPerMinute = _configuration.GetValue("Hugin:RateLimits:ConnectionsPerMinute", 10),
            MessagesPerSecond = _configuration.GetValue("Hugin:RateLimits:MessagesPerSecond", 5),
            JoinsPerMinute = _configuration.GetValue("Hugin:RateLimits:JoinsPerMinute", 10),
            NickChangesPerMinute = _configuration.GetValue("Hugin:RateLimits:NickChangesPerMinute", 3),
            PrivmsgsPerSecond = _configuration.GetValue("Hugin:RateLimits:PrivmsgsPerSecond", 3)
        };

        return Ok(ApiResponse<RateLimitConfigDto>.Ok(config));
    }

    /// <summary>
    /// Updates rate limiting configuration.
    /// </summary>
    [HttpPut("ratelimits")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateRateLimits([FromBody] RateLimitConfigDto config, CancellationToken cancellationToken)
    {
        try
        {
            // Read current appsettings.json
            var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!System.IO.File.Exists(appSettingsPath))
            {
                return BadRequest(ApiResponse.Fail("Configuration file not found"));
            }

            var json = await System.IO.File.ReadAllTextAsync(appSettingsPath, cancellationToken);
            var root = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json) ?? new();

            // Update rate limits section
            var rateLimits = new Dictionary<string, int>
            {
                ["ConnectionsPerMinute"] = config.ConnectionsPerMinute,
                ["MessagesPerSecond"] = config.MessagesPerSecond,
                ["JoinsPerMinute"] = config.JoinsPerMinute,
                ["NickChangesPerMinute"] = config.NickChangesPerMinute,
                ["PrivmsgsPerSecond"] = config.PrivmsgsPerSecond
            };

            // Parse existing Hugin section and update
            if (root.TryGetValue("Hugin", out var huginElement))
            {
                var hugin = JsonSerializer.Deserialize<Dictionary<string, object>>(huginElement.GetRawText()) ?? new();
                hugin["RateLimits"] = rateLimits;
                
                var newRoot = new Dictionary<string, object>(root.Count);
                foreach (var kvp in root)
                {
                    if (kvp.Key == "Hugin")
                        newRoot[kvp.Key] = hugin;
                    else
                        newRoot[kvp.Key] = kvp.Value;
                }

                var updatedJson = JsonSerializer.Serialize(newRoot, ConfigJsonOptions.WriteOptions);
                await System.IO.File.WriteAllTextAsync(appSettingsPath, updatedJson, cancellationToken);
            }

            _logger.LogInformation("Rate limits updated by {User}", User.Identity?.Name);
            return Ok(ApiResponse.Ok("Rate limits updated"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update rate limits");
            return BadRequest(ApiResponse.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Gets MOTD (Message of the Day).
    /// </summary>
    [HttpGet("motd")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<MotdDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMotd(CancellationToken cancellationToken)
    {
        var motdPath = Path.Combine(AppContext.BaseDirectory, "motd.txt");
        var motd = System.IO.File.Exists(motdPath) 
            ? await System.IO.File.ReadAllTextAsync(motdPath, cancellationToken)
            : "Welcome to Hugin IRC Server!";

        return Ok(ApiResponse<MotdDto>.Ok(new MotdDto
        {
            Content = motd,
            LastModified = System.IO.File.Exists(motdPath) ? System.IO.File.GetLastWriteTimeUtc(motdPath) : null
        }));
    }

    /// <summary>
    /// Updates MOTD.
    /// </summary>
    [HttpPut("motd")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMotd([FromBody] MotdDto motd, CancellationToken cancellationToken)
    {
        var motdPath = Path.Combine(AppContext.BaseDirectory, "motd.txt");
        await System.IO.File.WriteAllTextAsync(motdPath, motd.Content, cancellationToken);

        _logger.LogInformation("MOTD updated by {User}", User.Identity?.Name);
        return Ok(ApiResponse.Ok("MOTD updated"));
    }
}

/// <summary>
/// Rate limit configuration DTO.
/// </summary>
public sealed class RateLimitConfigDto
{
    /// <summary>
    /// Maximum connections per minute per IP.
    /// </summary>
    public int ConnectionsPerMinute { get; init; } = 10;

    /// <summary>
    /// Maximum messages per second per user.
    /// </summary>
    public int MessagesPerSecond { get; init; } = 5;

    /// <summary>
    /// Maximum channel joins per minute.
    /// </summary>
    public int JoinsPerMinute { get; init; } = 10;

    /// <summary>
    /// Maximum nick changes per minute.
    /// </summary>
    public int NickChangesPerMinute { get; init; } = 3;

    /// <summary>
    /// Maximum PRIVMSGs per second.
    /// </summary>
    public int PrivmsgsPerSecond { get; init; } = 3;
}

/// <summary>
/// MOTD DTO.
/// </summary>
public sealed class MotdDto
{
    /// <summary>
    /// MOTD content.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Last modified timestamp.
    /// </summary>
    public DateTime? LastModified { get; init; }
}
