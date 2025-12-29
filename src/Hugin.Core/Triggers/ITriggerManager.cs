// Licensed to the Hugin IRC Server under one or more agreements.
// The Hugin IRC Server licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Hugin.Core.Triggers;

/// <summary>
/// Represents a trigger condition that must be matched.
/// </summary>
public sealed class TriggerCondition
{
    /// <summary>
    /// Gets or sets the type of condition to match.
    /// </summary>
    [JsonPropertyName("type")]
    public TriggerConditionType Type { get; set; }

    /// <summary>
    /// Gets or sets the pattern to match against.
    /// For regex conditions, this is a regex pattern.
    /// For wildcard conditions, this supports * and ? wildcards.
    /// </summary>
    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    /// <summary>
    /// Gets or sets the channel(s) this condition applies to.
    /// Supports wildcards. Null means all channels.
    /// </summary>
    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    /// <summary>
    /// Gets or sets the nickname(s) this condition applies to.
    /// Supports wildcards. Null means all users.
    /// </summary>
    [JsonPropertyName("nick")]
    public string? Nick { get; set; }

    /// <summary>
    /// Gets or sets the hostmask pattern this condition applies to.
    /// Supports wildcards. Null means all hostmasks.
    /// </summary>
    [JsonPropertyName("hostmask")]
    public string? Hostmask { get; set; }

    /// <summary>
    /// Gets or sets whether the user must be registered/identified.
    /// Null means any user.
    /// </summary>
    [JsonPropertyName("registered")]
    public bool? Registered { get; set; }

    /// <summary>
    /// Gets or sets whether the user must be an operator.
    /// Null means any user.
    /// </summary>
    [JsonPropertyName("operator")]
    public bool? IsOperator { get; set; }

    /// <summary>
    /// Gets or sets time-of-day restrictions (HH:MM-HH:MM format).
    /// Example: "09:00-17:00" for business hours.
    /// </summary>
    [JsonPropertyName("time")]
    public string? TimeRange { get; set; }

    /// <summary>
    /// Gets or sets day-of-week restrictions.
    /// Example: ["Monday", "Tuesday", "Wednesday"]
    /// </summary>
    [JsonPropertyName("days")]
    public List<string>? Days { get; set; }

    /// <summary>
    /// Gets or sets whether the pattern match is case-sensitive.
    /// Default is false (case-insensitive).
    /// </summary>
    [JsonPropertyName("caseSensitive")]
    public bool CaseSensitive { get; set; } = false;

    /// <summary>
    /// Gets or sets whether all sub-conditions must match (AND) or any (OR).
    /// Default is true (AND logic).
    /// </summary>
    [JsonPropertyName("matchAll")]
    public bool MatchAll { get; set; } = true;
}

/// <summary>
/// Types of conditions that can trigger an action.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TriggerConditionType
{
    /// <summary>Message matches a regex pattern.</summary>
    Regex,

    /// <summary>Message matches a wildcard pattern.</summary>
    Wildcard,

    /// <summary>Message contains the specified text.</summary>
    Contains,

    /// <summary>Message equals the specified text exactly.</summary>
    Equals,

    /// <summary>Message starts with the specified text.</summary>
    StartsWith,

    /// <summary>Message ends with the specified text.</summary>
    EndsWith,

    /// <summary>Matches based on channel/nick/hostmask only (no message pattern).</summary>
    Always,

    /// <summary>Command trigger (e.g., !command).</summary>
    Command
}

/// <summary>
/// Represents an action to take when a trigger matches.
/// </summary>
public sealed class TriggerAction
{
    /// <summary>
    /// Gets or sets the type of action to perform.
    /// </summary>
    [JsonPropertyName("type")]
    public TriggerActionType Type { get; set; }

    /// <summary>
    /// Gets or sets the target of the action.
    /// Can use placeholders: {nick}, {channel}, {account}, {match}, {1}, {2}, etc.
    /// </summary>
    [JsonPropertyName("target")]
    public string? Target { get; set; }

    /// <summary>
    /// Gets or sets the message or value for the action.
    /// Can use placeholders.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets the mode string for mode actions.
    /// Example: "+b" or "-o"
    /// </summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    /// <summary>
    /// Gets or sets the duration for timed actions (in seconds).
    /// Used for temporary bans, mutes, etc.
    /// </summary>
    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    /// <summary>
    /// Gets or sets the reason for kick/ban actions.
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>
    /// Gets or sets the delay before executing the action (in milliseconds).
    /// </summary>
    [JsonPropertyName("delay")]
    public int? Delay { get; set; }
}

/// <summary>
/// Types of actions that can be performed by a trigger.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TriggerActionType
{
    /// <summary>Send a PRIVMSG to a target.</summary>
    Reply,

    /// <summary>Send a NOTICE to a target.</summary>
    Notice,

    /// <summary>Kick the user from the channel.</summary>
    Kick,

    /// <summary>Ban the user from the channel.</summary>
    Ban,

    /// <summary>Kick and ban the user.</summary>
    KickBan,

    /// <summary>Set a mode on the channel or user.</summary>
    Mode,

    /// <summary>Set the channel topic.</summary>
    Topic,

    /// <summary>Kill the user (disconnect them).</summary>
    Kill,

    /// <summary>Set a K-line (server ban).</summary>
    Kline,

    /// <summary>Block the message from being sent.</summary>
    Block,

    /// <summary>Log the event to the server log.</summary>
    Log,

    /// <summary>Execute another trigger by name.</summary>
    Chain,

    /// <summary>Send a raw IRC command.</summary>
    Raw
}

/// <summary>
/// Represents a complete trigger definition.
/// </summary>
public sealed class TriggerDefinition
{
    /// <summary>
    /// Gets or sets the unique identifier for this trigger.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the display name of the trigger.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets whether the trigger is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the trigger priority (lower = higher priority).
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Gets or sets the event type(s) this trigger responds to.
    /// </summary>
    [JsonPropertyName("events")]
    public List<TriggerEventType> Events { get; set; } = [TriggerEventType.Message];

    /// <summary>
    /// Gets or sets the conditions that must be met for the trigger to fire.
    /// </summary>
    [JsonPropertyName("conditions")]
    public List<TriggerCondition> Conditions { get; set; } = [];

    /// <summary>
    /// Gets or sets the actions to perform when the trigger fires.
    /// </summary>
    [JsonPropertyName("actions")]
    public List<TriggerAction> Actions { get; set; } = [];

    /// <summary>
    /// Gets or sets the cooldown period in seconds between trigger fires.
    /// </summary>
    [JsonPropertyName("cooldown")]
    public int Cooldown { get; set; } = 0;

    /// <summary>
    /// Gets or sets the cooldown scope (per-user, per-channel, or global).
    /// </summary>
    [JsonPropertyName("cooldownScope")]
    public TriggerCooldownScope CooldownScope { get; set; } = TriggerCooldownScope.Global;

    /// <summary>
    /// Gets or sets whether to stop processing other triggers after this one fires.
    /// </summary>
    [JsonPropertyName("stopOnMatch")]
    public bool StopOnMatch { get; set; } = false;

    /// <summary>
    /// Gets or sets a description of what this trigger does.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Event types that triggers can respond to.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TriggerEventType
{
    /// <summary>Channel message.</summary>
    Message,

    /// <summary>Private message.</summary>
    PrivateMessage,

    /// <summary>Notice.</summary>
    Notice,

    /// <summary>User joined a channel.</summary>
    Join,

    /// <summary>User left a channel.</summary>
    Part,

    /// <summary>User quit the server.</summary>
    Quit,

    /// <summary>User changed nickname.</summary>
    Nick,

    /// <summary>User was kicked.</summary>
    Kick,

    /// <summary>Topic changed.</summary>
    Topic,

    /// <summary>Mode changed.</summary>
    Mode,

    /// <summary>User connected.</summary>
    Connect,

    /// <summary>User identified to account.</summary>
    Login
}

/// <summary>
/// Scope for trigger cooldowns.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TriggerCooldownScope
{
    /// <summary>Global cooldown for all users.</summary>
    Global,

    /// <summary>Per-channel cooldown.</summary>
    Channel,

    /// <summary>Per-user cooldown.</summary>
    User,

    /// <summary>Per-user-per-channel cooldown.</summary>
    UserChannel
}

/// <summary>
/// Context for trigger matching and action execution.
/// </summary>
public sealed class TriggerContext
{
    /// <summary>
    /// Gets or sets the event type.
    /// </summary>
    public required TriggerEventType Event { get; init; }

    /// <summary>
    /// Gets or sets the source nickname.
    /// </summary>
    public string? Nick { get; init; }

    /// <summary>
    /// Gets or sets the source username.
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// Gets or sets the source hostname.
    /// </summary>
    public string? Hostname { get; init; }

    /// <summary>
    /// Gets or sets the full hostmask (nick!user@host).
    /// </summary>
    public string? Hostmask { get; init; }

    /// <summary>
    /// Gets or sets the channel (if applicable).
    /// </summary>
    public string? Channel { get; init; }

    /// <summary>
    /// Gets or sets the message or text content.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets or sets the target (for KICK, MODE, etc.).
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets or sets the account name (if logged in).
    /// </summary>
    public string? Account { get; init; }

    /// <summary>
    /// Gets or sets whether the user is registered/identified.
    /// </summary>
    public bool IsRegistered { get; init; }

    /// <summary>
    /// Gets or sets whether the user is an operator.
    /// </summary>
    public bool IsOperator { get; init; }

    /// <summary>
    /// Gets or sets regex match groups (populated after matching).
    /// </summary>
    public IReadOnlyList<string>? MatchGroups { get; set; }

    /// <summary>
    /// Gets or sets whether the event should be blocked.
    /// </summary>
    public bool Block { get; set; }

    /// <summary>
    /// Gets or sets whether to stop processing further triggers.
    /// </summary>
    public bool StopProcessing { get; set; }
}

/// <summary>
/// Result of trigger matching.
/// </summary>
public sealed class TriggerMatchResult
{
    /// <summary>
    /// Gets whether the trigger matched.
    /// </summary>
    public bool Matched { get; init; }

    /// <summary>
    /// Gets the trigger that matched.
    /// </summary>
    public TriggerDefinition? Trigger { get; init; }

    /// <summary>
    /// Gets the regex match groups if applicable.
    /// </summary>
    public IReadOnlyList<string>? MatchGroups { get; init; }

    /// <summary>
    /// Gets whether the trigger was on cooldown.
    /// </summary>
    public bool OnCooldown { get; init; }
}

/// <summary>
/// Statistics for a trigger.
/// </summary>
public sealed class TriggerStatistics
{
    /// <summary>
    /// Gets the trigger ID.
    /// </summary>
    public required string TriggerId { get; init; }

    /// <summary>
    /// Gets the number of times the trigger has fired.
    /// </summary>
    public long FireCount { get; set; }

    /// <summary>
    /// Gets the last time the trigger fired.
    /// </summary>
    public DateTimeOffset? LastFired { get; set; }

    /// <summary>
    /// Gets the number of times the trigger was blocked by cooldown.
    /// </summary>
    public long CooldownBlockCount { get; set; }
}

/// <summary>
/// Interface for the trigger manager.
/// </summary>
public interface ITriggerManager
{
    /// <summary>
    /// Gets all loaded triggers.
    /// </summary>
    IReadOnlyList<TriggerDefinition> Triggers { get; }

    /// <summary>
    /// Loads triggers from a JSON file.
    /// </summary>
    /// <param name="filePath">Path to the JSON file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of triggers loaded.</returns>
    Task<int> LoadTriggersAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads all triggers from a directory.
    /// </summary>
    /// <param name="directoryPath">Path to the triggers directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of triggers loaded.</returns>
    Task<int> LoadTriggersFromDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads all triggers from their source files.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of triggers loaded.</returns>
    Task<int> ReloadTriggersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a trigger programmatically.
    /// </summary>
    /// <param name="trigger">The trigger to add.</param>
    /// <returns>True if added successfully.</returns>
    bool AddTrigger(TriggerDefinition trigger);

    /// <summary>
    /// Removes a trigger by ID.
    /// </summary>
    /// <param name="triggerId">The trigger ID to remove.</param>
    /// <returns>True if removed.</returns>
    bool RemoveTrigger(string triggerId);

    /// <summary>
    /// Enables or disables a trigger.
    /// </summary>
    /// <param name="triggerId">The trigger ID.</param>
    /// <param name="enabled">Whether to enable or disable.</param>
    /// <returns>True if the trigger was found and updated.</returns>
    bool SetTriggerEnabled(string triggerId, bool enabled);

    /// <summary>
    /// Processes an event and fires matching triggers.
    /// </summary>
    /// <param name="context">The trigger context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The modified context.</returns>
    Task<TriggerContext> ProcessEventAsync(TriggerContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a trigger by ID.
    /// </summary>
    /// <param name="triggerId">The trigger ID.</param>
    /// <returns>The trigger or null if not found.</returns>
    TriggerDefinition? GetTrigger(string triggerId);

    /// <summary>
    /// Gets statistics for a trigger.
    /// </summary>
    /// <param name="triggerId">The trigger ID.</param>
    /// <returns>Statistics or null if not found.</returns>
    TriggerStatistics? GetStatistics(string triggerId);

    /// <summary>
    /// Gets statistics for all triggers.
    /// </summary>
    /// <returns>Dictionary of trigger ID to statistics.</returns>
    IReadOnlyDictionary<string, TriggerStatistics> GetAllStatistics();

    /// <summary>
    /// Saves triggers to a JSON file.
    /// </summary>
    /// <param name="filePath">Path to the JSON file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveTriggersAsync(string filePath, CancellationToken cancellationToken = default);
}
