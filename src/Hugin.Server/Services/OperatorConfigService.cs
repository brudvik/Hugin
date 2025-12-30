// Hugin IRC Server - Operator Configuration Service
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using System.Collections.Concurrent;
using System.Text.Json;
using Hugin.Core.Interfaces;
using Hugin.Security;
using Microsoft.Extensions.Logging;

namespace Hugin.Server.Services;

/// <summary>
/// Cached JSON serializer options for operator configuration.
/// </summary>
internal static class OperatorJsonOptions
{
    /// <summary>
    /// Options for writing operator config (indented).
    /// </summary>
    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };
}

/// <summary>
/// In-memory operator configuration service with file persistence.
/// </summary>
public sealed class OperatorConfigService : IOperatorConfigService
{
    private readonly ConcurrentDictionary<string, OperatorConfig> _operators = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<OperatorConfigService> _logger;
    private readonly string _configPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="OperatorConfigService"/> class.
    /// </summary>
    public OperatorConfigService(
        ILogger<OperatorConfigService> logger)
    {
        _logger = logger;
        _configPath = Path.Combine(AppContext.BaseDirectory, "operators.json");

        LoadFromFile();
        EnsureDefaultOperators();
    }

    /// <inheritdoc />
    public IReadOnlyList<OperatorConfig> GetAllOperators()
    {
        return _operators.Values.ToList();
    }

    /// <inheritdoc />
    public OperatorConfig? GetOperator(string name)
    {
        _operators.TryGetValue(name, out var config);
        return config;
    }

    /// <inheritdoc />
    public bool ValidateCredentials(string name, string password)
    {
        if (!_operators.TryGetValue(name, out var config))
        {
            return false;
        }

        return PasswordHasher.VerifyPassword(password, config.PasswordHash);
    }

    /// <inheritdoc />
    public void AddOrUpdateOperator(OperatorConfig config)
    {
        _operators[config.Name] = config;
        SaveToFile();
        _logger.LogInformation("Operator {Name} added/updated", config.Name);
    }

    /// <inheritdoc />
    public bool RemoveOperator(string name)
    {
        var removed = _operators.TryRemove(name, out _);
        if (removed)
        {
            SaveToFile();
            _logger.LogInformation("Operator {Name} removed", name);
        }
        return removed;
    }

    /// <inheritdoc />
    public void SetOnline(string name)
    {
        if (_operators.TryGetValue(name, out var config))
        {
            config.IsOnline = true;
            _logger.LogDebug("Operator {Name} is now online", name);
        }
    }

    /// <inheritdoc />
    public void SetOffline(string name)
    {
        if (_operators.TryGetValue(name, out var config))
        {
            config.IsOnline = false;
            config.LastSeen = DateTimeOffset.UtcNow;
            _logger.LogDebug("Operator {Name} is now offline", name);
        }
    }

    /// <summary>
    /// Loads operators from file.
    /// </summary>
    private void LoadFromFile()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var operators = JsonSerializer.Deserialize<OperatorConfig[]>(json);
                if (operators != null)
                {
                    foreach (var op in operators)
                    {
                        _operators[op.Name] = op;
                    }
                    _logger.LogInformation("Loaded {Count} operators from {Path}", operators.Length, _configPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load operators from {Path}", _configPath);
        }
    }

    /// <summary>
    /// Saves operators to file.
    /// </summary>
    private void SaveToFile()
    {
        try
        {
            var json = JsonSerializer.Serialize(_operators.Values.ToArray(), OperatorJsonOptions.WriteOptions);
            File.WriteAllText(_configPath, json);
            _logger.LogDebug("Saved {Count} operators to {Path}", _operators.Count, _configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save operators to {Path}", _configPath);
        }
    }

    /// <summary>
    /// Ensures default operators exist for initial setup.
    /// </summary>
    private void EnsureDefaultOperators()
    {
        if (_operators.IsEmpty)
        {
            // Create default admin operator
            var adminHash = PasswordHasher.HashPassword("admin123");
            _operators["admin"] = new OperatorConfig
            {
                Name = "admin",
                PasswordHash = adminHash,
                OperClass = "admin",
                Hostmasks = ["*@*"],
                Permissions = ["kill", "kline", "gline", "rehash", "restart", "die"]
            };

            // Create default oper
            var operHash = PasswordHasher.HashPassword("oper123");
            _operators["oper"] = new OperatorConfig
            {
                Name = "oper",
                PasswordHash = operHash,
                OperClass = "local",
                Hostmasks = ["*@*"],
                Permissions = ["kill", "kline"]
            };

            SaveToFile();
            _logger.LogInformation("Created default operators (admin, oper). Please change default passwords!");
        }
    }
}
