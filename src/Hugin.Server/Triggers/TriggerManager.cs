// Licensed to the Hugin IRC Server under one or more agreements.
// The Hugin IRC Server licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hugin.Core.Triggers;
using Microsoft.Extensions.Logging;

namespace Hugin.Server.Triggers;

/// <summary>
/// Implementation of the trigger manager with JSON-based trigger definitions.
/// </summary>
public sealed class TriggerManager : ITriggerManager
{
    private readonly ILogger<TriggerManager> _logger;
    private readonly ITriggerActionExecutor _actionExecutor;
    private readonly ConcurrentDictionary<string, TriggerDefinition> _triggers = new();
    private readonly ConcurrentDictionary<string, TriggerStatistics> _statistics = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cooldowns = new();
    private readonly ConcurrentDictionary<string, Regex> _compiledRegexes = new();
    private readonly List<string> _loadedFiles = [];
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="TriggerManager"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="actionExecutor">The action executor.</param>
    public TriggerManager(ILogger<TriggerManager> logger, ITriggerActionExecutor actionExecutor)
    {
        _logger = logger;
        _actionExecutor = actionExecutor;
    }

    /// <inheritdoc />
    public IReadOnlyList<TriggerDefinition> Triggers =>
        _triggers.Values.OrderBy(t => t.Priority).ToList();

    /// <inheritdoc />
    public async Task<int> LoadTriggersAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Trigger file not found: {FilePath}", filePath);
            return 0;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var triggerFile = JsonSerializer.Deserialize<TriggerFile>(json, JsonOptions);

            if (triggerFile?.Triggers == null || triggerFile.Triggers.Count == 0)
            {
                _logger.LogWarning("No triggers found in file: {FilePath}", filePath);
                return 0;
            }

            var loadedCount = 0;
            foreach (var trigger in triggerFile.Triggers)
            {
                if (string.IsNullOrEmpty(trigger.Id))
                {
                    _logger.LogWarning("Trigger without ID in file {FilePath}, skipping", filePath);
                    continue;
                }

                if (AddTrigger(trigger))
                {
                    loadedCount++;
                }
            }

            lock (_lock)
            {
                if (!_loadedFiles.Contains(filePath))
                {
                    _loadedFiles.Add(filePath);
                }
            }

            _logger.LogInformation("Loaded {Count} triggers from {FilePath}", loadedCount, filePath);
            return loadedCount;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse trigger file: {FilePath}", filePath);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load triggers from: {FilePath}", filePath);
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task<int> LoadTriggersFromDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
        {
            _logger.LogWarning("Triggers directory not found: {DirectoryPath}", directoryPath);
            return 0;
        }

        var jsonFiles = Directory.GetFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly);
        var totalLoaded = 0;

        foreach (var file in jsonFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalLoaded += await LoadTriggersAsync(file, cancellationToken);
        }

        _logger.LogInformation("Loaded {Count} total triggers from {DirectoryPath}", totalLoaded, directoryPath);
        return totalLoaded;
    }

    /// <inheritdoc />
    public async Task<int> ReloadTriggersAsync(CancellationToken cancellationToken = default)
    {
        List<string> filesToReload;
        lock (_lock)
        {
            filesToReload = [.. _loadedFiles];
        }

        // Clear existing triggers
        _triggers.Clear();
        _compiledRegexes.Clear();

        var totalLoaded = 0;
        foreach (var file in filesToReload)
        {
            totalLoaded += await LoadTriggersAsync(file, cancellationToken);
        }

        _logger.LogInformation("Reloaded {Count} triggers from {FileCount} files", totalLoaded, filesToReload.Count);
        return totalLoaded;
    }

    /// <inheritdoc />
    public bool AddTrigger(TriggerDefinition trigger)
    {
        if (string.IsNullOrEmpty(trigger.Id))
        {
            return false;
        }

        // Pre-compile regex patterns
        foreach (var condition in trigger.Conditions.Where(c => c.Type == TriggerConditionType.Regex))
        {
            if (!string.IsNullOrEmpty(condition.Pattern))
            {
                var key = $"{trigger.Id}:{condition.GetHashCode()}";
                try
                {
                    var options = condition.CaseSensitive
                        ? RegexOptions.Compiled
                        : RegexOptions.Compiled | RegexOptions.IgnoreCase;

                    _compiledRegexes[key] = new Regex(
                        condition.Pattern,
                        options,
                        TimeSpan.FromMilliseconds(100));
                }
                catch (RegexParseException ex)
                {
                    _logger.LogWarning("Invalid regex in trigger {TriggerId}: {Error}", trigger.Id, ex.Message);
                    return false;
                }
            }
        }

        _triggers[trigger.Id] = trigger;
        _statistics[trigger.Id] = new TriggerStatistics { TriggerId = trigger.Id };

        _logger.LogDebug("Added trigger: {TriggerId} ({Name})", trigger.Id, trigger.Name ?? trigger.Id);
        return true;
    }

    /// <inheritdoc />
    public bool RemoveTrigger(string triggerId)
    {
        if (!_triggers.TryRemove(triggerId, out _))
        {
            return false;
        }

        _statistics.TryRemove(triggerId, out _);

        // Remove compiled regexes
        var keysToRemove = _compiledRegexes.Keys.Where(k => k.StartsWith(triggerId + ":", StringComparison.Ordinal)).ToList();
        foreach (var key in keysToRemove)
        {
            _compiledRegexes.TryRemove(key, out _);
        }

        _logger.LogInformation("Removed trigger: {TriggerId}", triggerId);
        return true;
    }

    /// <inheritdoc />
    public bool SetTriggerEnabled(string triggerId, bool enabled)
    {
        if (!_triggers.TryGetValue(triggerId, out var trigger))
        {
            return false;
        }

        trigger.Enabled = enabled;
        _logger.LogInformation("Trigger {TriggerId} {State}", triggerId, enabled ? "enabled" : "disabled");
        return true;
    }

    /// <inheritdoc />
    public async Task<TriggerContext> ProcessEventAsync(TriggerContext context, CancellationToken cancellationToken = default)
    {
        var orderedTriggers = _triggers.Values
            .Where(t => t.Enabled && t.Events.Contains(context.Event))
            .OrderBy(t => t.Priority)
            .ToList();

        foreach (var trigger in orderedTriggers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var matchResult = MatchTrigger(trigger, context);

            if (!matchResult.Matched)
            {
                continue;
            }

            if (matchResult.OnCooldown)
            {
                if (_statistics.TryGetValue(trigger.Id, out var stats))
                {
                    stats.CooldownBlockCount++;
                }
                continue;
            }

            // Set match groups in context
            context.MatchGroups = matchResult.MatchGroups;

            // Execute actions
            foreach (var action in trigger.Actions)
            {
                try
                {
                    await _actionExecutor.ExecuteAsync(action, context, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing action for trigger {TriggerId}", trigger.Id);
                }
            }

            // Update statistics
            if (_statistics.TryGetValue(trigger.Id, out var triggerStats))
            {
                triggerStats.FireCount++;
                triggerStats.LastFired = DateTimeOffset.UtcNow;
            }

            // Update cooldown
            if (trigger.Cooldown > 0)
            {
                var cooldownKey = GetCooldownKey(trigger, context);
                _cooldowns[cooldownKey] = DateTimeOffset.UtcNow.AddSeconds(trigger.Cooldown);
            }

            // Check if we should stop processing
            if (trigger.StopOnMatch || context.StopProcessing)
            {
                break;
            }
        }

        return context;
    }

    /// <inheritdoc />
    public TriggerDefinition? GetTrigger(string triggerId)
    {
        return _triggers.TryGetValue(triggerId, out var trigger) ? trigger : null;
    }

    /// <inheritdoc />
    public TriggerStatistics? GetStatistics(string triggerId)
    {
        return _statistics.TryGetValue(triggerId, out var stats) ? stats : null;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, TriggerStatistics> GetAllStatistics()
    {
        return new Dictionary<string, TriggerStatistics>(_statistics);
    }

    /// <inheritdoc />
    public async Task SaveTriggersAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var triggerFile = new TriggerFile
        {
            Triggers = [.. _triggers.Values.OrderBy(t => t.Priority)]
        };

        var json = JsonSerializer.Serialize(triggerFile, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        _logger.LogInformation("Saved {Count} triggers to {FilePath}", triggerFile.Triggers.Count, filePath);
    }

    #region Private Methods

    private TriggerMatchResult MatchTrigger(TriggerDefinition trigger, TriggerContext context)
    {
        // Check cooldown first
        if (trigger.Cooldown > 0)
        {
            var cooldownKey = GetCooldownKey(trigger, context);
            if (_cooldowns.TryGetValue(cooldownKey, out var expiry) && DateTimeOffset.UtcNow < expiry)
            {
                return new TriggerMatchResult { OnCooldown = true };
            }
        }

        List<string>? matchGroups = null;

        // Check all conditions
        foreach (var condition in trigger.Conditions)
        {
            var conditionMatched = MatchCondition(trigger, condition, context, out var groups);

            if (conditionMatched && groups != null)
            {
                matchGroups = groups;
            }

            // If MatchAll is true and this condition failed, the trigger doesn't match
            if (!conditionMatched && condition.MatchAll)
            {
                return new TriggerMatchResult { Matched = false };
            }

            // If MatchAll is false and this condition matched, the trigger matches
            if (conditionMatched && !condition.MatchAll)
            {
                return new TriggerMatchResult
                {
                    Matched = true,
                    Trigger = trigger,
                    MatchGroups = matchGroups
                };
            }
        }

        // All conditions checked - if we got here with MatchAll, we matched
        return new TriggerMatchResult
        {
            Matched = trigger.Conditions.Count == 0 || trigger.Conditions.All(c => c.MatchAll),
            Trigger = trigger,
            MatchGroups = matchGroups
        };
    }

    private bool MatchCondition(TriggerDefinition trigger, TriggerCondition condition, TriggerContext context, out List<string>? matchGroups)
    {
        matchGroups = null;

        // Check channel filter
        if (!string.IsNullOrEmpty(condition.Channel) && !string.IsNullOrEmpty(context.Channel))
        {
            if (!WildcardMatch(context.Channel, condition.Channel, !condition.CaseSensitive))
            {
                return false;
            }
        }

        // Check nick filter
        if (!string.IsNullOrEmpty(condition.Nick) && !string.IsNullOrEmpty(context.Nick))
        {
            if (!WildcardMatch(context.Nick, condition.Nick, !condition.CaseSensitive))
            {
                return false;
            }
        }

        // Check hostmask filter
        if (!string.IsNullOrEmpty(condition.Hostmask) && !string.IsNullOrEmpty(context.Hostmask))
        {
            if (!WildcardMatch(context.Hostmask, condition.Hostmask, !condition.CaseSensitive))
            {
                return false;
            }
        }

        // Check registered requirement
        if (condition.Registered.HasValue && context.IsRegistered != condition.Registered.Value)
        {
            return false;
        }

        // Check operator requirement
        if (condition.IsOperator.HasValue && context.IsOperator != condition.IsOperator.Value)
        {
            return false;
        }

        // Check time range
        if (!string.IsNullOrEmpty(condition.TimeRange))
        {
            if (!IsInTimeRange(condition.TimeRange))
            {
                return false;
            }
        }

        // Check day of week
        if (condition.Days != null && condition.Days.Count > 0)
        {
            var today = DateTime.UtcNow.DayOfWeek.ToString();
            if (!condition.Days.Contains(today, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Now check the pattern-based condition type
        var message = context.Message ?? string.Empty;
        var comparison = condition.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        return condition.Type switch
        {
            TriggerConditionType.Always => true,

            TriggerConditionType.Regex => MatchRegex(trigger.Id, condition, message, out matchGroups),

            TriggerConditionType.Wildcard =>
                WildcardMatch(message, condition.Pattern ?? "*", !condition.CaseSensitive),

            TriggerConditionType.Contains =>
                !string.IsNullOrEmpty(condition.Pattern) && message.Contains(condition.Pattern, comparison),

            TriggerConditionType.Equals =>
                message.Equals(condition.Pattern, comparison),

            TriggerConditionType.StartsWith =>
                !string.IsNullOrEmpty(condition.Pattern) && message.StartsWith(condition.Pattern, comparison),

            TriggerConditionType.EndsWith =>
                !string.IsNullOrEmpty(condition.Pattern) && message.EndsWith(condition.Pattern, comparison),

            TriggerConditionType.Command =>
                MatchCommand(message, condition.Pattern ?? string.Empty, comparison),

            _ => false
        };
    }

    private bool MatchRegex(string triggerId, TriggerCondition condition, string message, out List<string>? groups)
    {
        groups = null;
        var key = $"{triggerId}:{condition.GetHashCode()}";

        if (!_compiledRegexes.TryGetValue(key, out var regex))
        {
            return false;
        }

        try
        {
            var match = regex.Match(message);
            if (match.Success)
            {
                groups = match.Groups.Cast<Group>().Select(g => g.Value).ToList();
                return true;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            _logger.LogWarning("Regex timeout in trigger condition");
        }

        return false;
    }

    private static bool MatchCommand(string message, string command, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(command))
        {
            return false;
        }

        // Command format: !command or prefix+command
        var cmdPattern = command.StartsWith('!') ? command : "!" + command;

        return message.Equals(cmdPattern, comparison) ||
               message.StartsWith(cmdPattern + " ", comparison);
    }

    private static bool WildcardMatch(string input, string pattern, bool ignoreCase)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        var options = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;

        try
        {
            return Regex.IsMatch(input, regexPattern, options, TimeSpan.FromMilliseconds(100));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsInTimeRange(string timeRange)
    {
        var parts = timeRange.Split('-');
        if (parts.Length != 2)
        {
            return true;
        }

        if (!TimeSpan.TryParse(parts[0], CultureInfo.InvariantCulture, out var start) ||
            !TimeSpan.TryParse(parts[1], CultureInfo.InvariantCulture, out var end))
        {
            return true;
        }

        var now = DateTime.UtcNow.TimeOfDay;

        // Handle ranges that cross midnight
        if (start <= end)
        {
            return now >= start && now <= end;
        }
        else
        {
            return now >= start || now <= end;
        }
    }

    private static string GetCooldownKey(TriggerDefinition trigger, TriggerContext context)
    {
        return trigger.CooldownScope switch
        {
            TriggerCooldownScope.Global => trigger.Id,
            TriggerCooldownScope.Channel => $"{trigger.Id}:{context.Channel}",
            TriggerCooldownScope.User => $"{trigger.Id}:{context.Nick}",
            TriggerCooldownScope.UserChannel => $"{trigger.Id}:{context.Nick}:{context.Channel}",
            _ => trigger.Id
        };
    }

    #endregion

    /// <summary>
    /// Container for trigger file JSON structure.
    /// </summary>
    private sealed class TriggerFile
    {
        public List<TriggerDefinition> Triggers { get; set; } = [];
    }
}

/// <summary>
/// Interface for executing trigger actions.
/// </summary>
public interface ITriggerActionExecutor
{
    /// <summary>
    /// Executes a trigger action.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="context">The trigger context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExecuteAsync(TriggerAction action, TriggerContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of the trigger action executor.
/// </summary>
public sealed class TriggerActionExecutor : ITriggerActionExecutor
{
    private readonly ILogger<TriggerActionExecutor> _logger;
    private readonly Func<string, string, Task>? _sendMessage;
    private readonly Func<string, string, Task>? _sendNotice;
    private readonly Func<string, string, string, Task>? _kickUser;
    private readonly Func<string, string, TimeSpan?, Task>? _setBan;
    private readonly Func<string, string, Task>? _setMode;
    private readonly Func<string, string, Task>? _killUser;

    /// <summary>
    /// Initializes a new instance of the <see cref="TriggerActionExecutor"/> class.
    /// </summary>
    public TriggerActionExecutor(
        ILogger<TriggerActionExecutor> logger,
        Func<string, string, Task>? sendMessage = null,
        Func<string, string, Task>? sendNotice = null,
        Func<string, string, string, Task>? kickUser = null,
        Func<string, string, TimeSpan?, Task>? setBan = null,
        Func<string, string, Task>? setMode = null,
        Func<string, string, Task>? killUser = null)
    {
        _logger = logger;
        _sendMessage = sendMessage;
        _sendNotice = sendNotice;
        _kickUser = kickUser;
        _setBan = setBan;
        _setMode = setMode;
        _killUser = killUser;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(TriggerAction action, TriggerContext context, CancellationToken cancellationToken = default)
    {
        // Apply delay if specified
        if (action.Delay.HasValue && action.Delay.Value > 0)
        {
            await Task.Delay(action.Delay.Value, cancellationToken);
        }

        var target = ExpandPlaceholders(action.Target ?? context.Channel ?? context.Nick ?? "", context);
        var message = ExpandPlaceholders(action.Message ?? "", context);
        var reason = ExpandPlaceholders(action.Reason ?? "Triggered by automated rule", context);

        switch (action.Type)
        {
            case TriggerActionType.Reply:
                if (_sendMessage != null && !string.IsNullOrEmpty(target))
                {
                    await _sendMessage(target, message);
                }
                break;

            case TriggerActionType.Notice:
                if (_sendNotice != null && !string.IsNullOrEmpty(target))
                {
                    await _sendNotice(target, message);
                }
                break;

            case TriggerActionType.Kick:
                if (_kickUser != null && !string.IsNullOrEmpty(context.Channel) && !string.IsNullOrEmpty(context.Nick))
                {
                    await _kickUser(context.Channel, context.Nick, reason);
                }
                break;

            case TriggerActionType.Ban:
                if (_setBan != null && !string.IsNullOrEmpty(context.Channel))
                {
                    var mask = !string.IsNullOrEmpty(action.Target)
                        ? ExpandPlaceholders(action.Target, context)
                        : $"*!*@{context.Hostname}";
                    var duration = action.Duration.HasValue
                        ? TimeSpan.FromSeconds(action.Duration.Value)
                        : (TimeSpan?)null;
                    await _setBan(context.Channel, mask, duration);
                }
                break;

            case TriggerActionType.KickBan:
                if (_setBan != null && !string.IsNullOrEmpty(context.Channel))
                {
                    var mask = $"*!*@{context.Hostname}";
                    var duration = action.Duration.HasValue
                        ? TimeSpan.FromSeconds(action.Duration.Value)
                        : (TimeSpan?)null;
                    await _setBan(context.Channel, mask, duration);
                }
                if (_kickUser != null && !string.IsNullOrEmpty(context.Channel) && !string.IsNullOrEmpty(context.Nick))
                {
                    await _kickUser(context.Channel, context.Nick, reason);
                }
                break;

            case TriggerActionType.Mode:
                if (_setMode != null && !string.IsNullOrEmpty(action.Mode))
                {
                    var modeTarget = !string.IsNullOrEmpty(target) ? target : context.Channel ?? "";
                    await _setMode(modeTarget, action.Mode);
                }
                break;

            case TriggerActionType.Kill:
                if (_killUser != null && !string.IsNullOrEmpty(context.Nick))
                {
                    await _killUser(context.Nick, reason);
                }
                break;

            case TriggerActionType.Block:
                context.Block = true;
                break;

            case TriggerActionType.Log:
                _logger.LogInformation("[Trigger] {Message}", message);
                break;

            case TriggerActionType.Chain:
                // Chain action would need the trigger manager - not implemented here
                _logger.LogDebug("Chain action to {Target} (not implemented in executor)", target);
                break;

            case TriggerActionType.Raw:
                _logger.LogDebug("Raw action: {Message} (not implemented in executor)", message);
                break;
        }
    }

    /// <summary>
    /// Expands placeholders in a string.
    /// </summary>
    private static string ExpandPlaceholders(string template, TriggerContext context)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        var result = template
            .Replace("{nick}", context.Nick ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{channel}", context.Channel ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{hostmask}", context.Hostmask ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{hostname}", context.Hostname ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{username}", context.Username ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{account}", context.Account ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{message}", context.Message ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{target}", context.Target ?? "", StringComparison.OrdinalIgnoreCase);

        // Replace numbered match groups
        if (context.MatchGroups != null)
        {
            result = result.Replace("{match}", context.MatchGroups.Count > 0 ? context.MatchGroups[0] : "", StringComparison.OrdinalIgnoreCase);

            for (int i = 0; i < context.MatchGroups.Count; i++)
            {
                result = result.Replace($"{{{i}}}", context.MatchGroups[i], StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }
}
