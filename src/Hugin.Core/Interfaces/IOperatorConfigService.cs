// Hugin IRC Server - Operator Configuration Interface
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

namespace Hugin.Core.Interfaces;

/// <summary>
/// Service for managing IRC operator configurations.
/// </summary>
public interface IOperatorConfigService
{
    /// <summary>
    /// Gets all configured operators.
    /// </summary>
    /// <returns>All operator configurations.</returns>
    IReadOnlyList<OperatorConfig> GetAllOperators();

    /// <summary>
    /// Gets an operator by name.
    /// </summary>
    /// <param name="name">The operator name.</param>
    /// <returns>The operator configuration if found; otherwise null.</returns>
    OperatorConfig? GetOperator(string name);

    /// <summary>
    /// Validates operator credentials.
    /// </summary>
    /// <param name="name">The operator name.</param>
    /// <param name="password">The password to validate.</param>
    /// <returns>True if credentials are valid; otherwise false.</returns>
    bool ValidateCredentials(string name, string password);

    /// <summary>
    /// Adds or updates an operator configuration.
    /// </summary>
    /// <param name="config">The operator configuration.</param>
    void AddOrUpdateOperator(OperatorConfig config);

    /// <summary>
    /// Removes an operator by name.
    /// </summary>
    /// <param name="name">The operator name to remove.</param>
    /// <returns>True if removed; false if not found.</returns>
    bool RemoveOperator(string name);

    /// <summary>
    /// Marks an operator as online.
    /// </summary>
    /// <param name="name">The operator name.</param>
    void SetOnline(string name);

    /// <summary>
    /// Marks an operator as offline.
    /// </summary>
    /// <param name="name">The operator name.</param>
    void SetOffline(string name);
}

/// <summary>
/// Configuration for an IRC operator.
/// </summary>
public sealed class OperatorConfig
{
    /// <summary>
    /// Gets or sets the operator name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the password hash (Argon2id).
    /// </summary>
    public required string PasswordHash { get; set; }

    /// <summary>
    /// Gets or sets the operator class/level.
    /// </summary>
    public required string OperClass { get; set; }

    /// <summary>
    /// Gets or sets allowed hostmasks.
    /// </summary>
    public string[] Hostmasks { get; set; } = [];

    /// <summary>
    /// Gets or sets permissions/flags.
    /// </summary>
    public string[] Permissions { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the operator is currently online.
    /// </summary>
    public bool IsOnline { get; set; }

    /// <summary>
    /// Gets or sets the last seen time.
    /// </summary>
    public DateTimeOffset? LastSeen { get; set; }
}
