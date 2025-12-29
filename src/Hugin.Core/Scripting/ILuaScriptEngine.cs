// Licensed to the Hugin IRC Server under one or more agreements.
// The Hugin IRC Server licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hugin.Core.Scripting;

/// <summary>
/// Represents a Lua script context with metadata and state.
/// </summary>
public sealed class LuaScript
{
    /// <summary>
    /// Gets the unique identifier for this script.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the file path of the script.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Gets the display name of the script.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the script version if defined.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Gets the script author if defined.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Gets the script description if defined.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets whether the script is currently enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets the time the script was loaded.
    /// </summary>
    public DateTimeOffset LoadedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the last time the script was modified.
    /// </summary>
    public DateTimeOffset? LastModified { get; set; }

    /// <summary>
    /// Gets the registered event handlers for this script.
    /// </summary>
    public IReadOnlyList<string> RegisteredHandlers { get; init; } = [];
}

/// <summary>
/// Represents the result of executing a Lua script or function.
/// </summary>
public sealed class LuaExecutionResult
{
    /// <summary>
    /// Gets whether the execution was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the error message if execution failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets the return values from the script.
    /// </summary>
    public IReadOnlyList<object?> ReturnValues { get; init; } = [];

    /// <summary>
    /// Gets the execution time in milliseconds.
    /// </summary>
    public double ExecutionTimeMs { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static LuaExecutionResult Ok(params object?[] values) => new()
    {
        Success = true,
        ReturnValues = values
    };

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    public static LuaExecutionResult Fail(string error) => new()
    {
        Success = false,
        Error = error
    };
}

/// <summary>
/// IRC event types that scripts can hook into.
/// </summary>
public enum ScriptEvent
{
    /// <summary>User connected to the server.</summary>
    UserConnect,

    /// <summary>User disconnected from the server.</summary>
    UserDisconnect,

    /// <summary>User changed nickname.</summary>
    NickChange,

    /// <summary>User joined a channel.</summary>
    ChannelJoin,

    /// <summary>User left a channel.</summary>
    ChannelPart,

    /// <summary>User was kicked from a channel.</summary>
    ChannelKick,

    /// <summary>Channel message received.</summary>
    ChannelMessage,

    /// <summary>Private message received.</summary>
    PrivateMessage,

    /// <summary>Notice received.</summary>
    Notice,

    /// <summary>Channel topic changed.</summary>
    TopicChange,

    /// <summary>Channel mode changed.</summary>
    ChannelModeChange,

    /// <summary>User mode changed.</summary>
    UserModeChange,

    /// <summary>User set away.</summary>
    Away,

    /// <summary>User returned from away.</summary>
    Back,

    /// <summary>User invited to channel.</summary>
    Invite,

    /// <summary>Server received a command.</summary>
    Command,

    /// <summary>Timer event fired.</summary>
    Timer,

    /// <summary>Server starting.</summary>
    ServerStart,

    /// <summary>Server stopping.</summary>
    ServerStop,

    /// <summary>User logged into account.</summary>
    AccountLogin,

    /// <summary>User logged out of account.</summary>
    AccountLogout
}

/// <summary>
/// Context passed to script event handlers.
/// </summary>
public sealed class ScriptEventContext
{
    /// <summary>
    /// Gets the event type.
    /// </summary>
    public required ScriptEvent Event { get; init; }

    /// <summary>
    /// Gets the source user nickname (if applicable).
    /// </summary>
    public string? Nick { get; init; }

    /// <summary>
    /// Gets the source user hostmask (if applicable).
    /// </summary>
    public string? Hostmask { get; init; }

    /// <summary>
    /// Gets the target channel (if applicable).
    /// </summary>
    public string? Channel { get; init; }

    /// <summary>
    /// Gets the target user (if applicable).
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the message text (if applicable).
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets the command name (for Command events).
    /// </summary>
    public string? Command { get; init; }

    /// <summary>
    /// Gets the command parameters (for Command events).
    /// </summary>
    public IReadOnlyList<string>? Parameters { get; init; }

    /// <summary>
    /// Gets the old value (for change events like NickChange).
    /// </summary>
    public string? OldValue { get; init; }

    /// <summary>
    /// Gets the new value (for change events like NickChange).
    /// </summary>
    public string? NewValue { get; init; }

    /// <summary>
    /// Gets additional event-specific data.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Extra { get; init; }

    /// <summary>
    /// Gets or sets whether the event should be cancelled.
    /// </summary>
    public bool Cancel { get; set; }

    /// <summary>
    /// Gets or sets a replacement message (if applicable).
    /// </summary>
    public string? ReplacementMessage { get; set; }
}

/// <summary>
/// Statistics for a loaded script.
/// </summary>
public sealed class ScriptStatistics
{
    /// <summary>
    /// Gets the script ID.
    /// </summary>
    public required string ScriptId { get; init; }

    /// <summary>
    /// Gets the total number of times event handlers were called.
    /// </summary>
    public long TotalCalls { get; set; }

    /// <summary>
    /// Gets the total execution time in milliseconds.
    /// </summary>
    public double TotalExecutionTimeMs { get; set; }

    /// <summary>
    /// Gets the number of errors encountered.
    /// </summary>
    public long ErrorCount { get; set; }

    /// <summary>
    /// Gets the last error message.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Gets the time of the last error.
    /// </summary>
    public DateTimeOffset? LastErrorTime { get; set; }
}

/// <summary>
/// Interface for the Lua scripting engine.
/// </summary>
public interface ILuaScriptEngine
{
    /// <summary>
    /// Gets all currently loaded scripts.
    /// </summary>
    IReadOnlyList<LuaScript> LoadedScripts { get; }

    /// <summary>
    /// Loads a Lua script from a file.
    /// </summary>
    /// <param name="filePath">Path to the Lua script file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded script or null if loading failed.</returns>
    Task<LuaScript?> LoadScriptAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unloads a script by ID.
    /// </summary>
    /// <param name="scriptId">The script ID to unload.</param>
    /// <returns>True if the script was unloaded.</returns>
    bool UnloadScript(string scriptId);

    /// <summary>
    /// Reloads a script by ID.
    /// </summary>
    /// <param name="scriptId">The script ID to reload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The reloaded script or null if reloading failed.</returns>
    Task<LuaScript?> ReloadScriptAsync(string scriptId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads all scripts from a directory.
    /// </summary>
    /// <param name="directoryPath">Path to the scripts directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of scripts loaded.</returns>
    Task<int> LoadScriptsFromDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fires an event to all scripts that have registered handlers.
    /// </summary>
    /// <param name="context">The event context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The event context (possibly modified by handlers).</returns>
    Task<ScriptEventContext> FireEventAsync(ScriptEventContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a Lua code string directly.
    /// </summary>
    /// <param name="code">The Lua code to execute.</param>
    /// <param name="scriptId">Optional script ID context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The execution result.</returns>
    Task<LuaExecutionResult> ExecuteAsync(string code, string? scriptId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics for a specific script.
    /// </summary>
    /// <param name="scriptId">The script ID.</param>
    /// <returns>Script statistics or null if not found.</returns>
    ScriptStatistics? GetStatistics(string scriptId);

    /// <summary>
    /// Gets statistics for all scripts.
    /// </summary>
    /// <returns>Dictionary of script ID to statistics.</returns>
    IReadOnlyDictionary<string, ScriptStatistics> GetAllStatistics();

    /// <summary>
    /// Enables or disables a script.
    /// </summary>
    /// <param name="scriptId">The script ID.</param>
    /// <param name="enabled">Whether to enable or disable.</param>
    /// <returns>True if the script state was changed.</returns>
    bool SetScriptEnabled(string scriptId, bool enabled);

    /// <summary>
    /// Registers a timer that will call back to a script function.
    /// </summary>
    /// <param name="scriptId">The script that owns the timer.</param>
    /// <param name="timerName">Name of the timer.</param>
    /// <param name="intervalMs">Interval in milliseconds.</param>
    /// <param name="repeat">Whether to repeat or fire once.</param>
    /// <returns>True if the timer was registered.</returns>
    bool RegisterTimer(string scriptId, string timerName, int intervalMs, bool repeat);

    /// <summary>
    /// Unregisters a timer.
    /// </summary>
    /// <param name="scriptId">The script that owns the timer.</param>
    /// <param name="timerName">Name of the timer.</param>
    /// <returns>True if the timer was unregistered.</returns>
    bool UnregisterTimer(string scriptId, string timerName);
}
