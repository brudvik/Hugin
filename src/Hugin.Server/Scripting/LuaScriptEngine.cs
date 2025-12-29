// Licensed to the Hugin IRC Server under one or more agreements.
// The Hugin IRC Server licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hugin.Core.Scripting;
using Microsoft.Extensions.Logging;
using NLua;

namespace Hugin.Server.Scripting;

/// <summary>
/// Lua scripting engine implementation with sandbox and IRC API bindings.
/// </summary>
public sealed class LuaScriptEngine : ILuaScriptEngine, IDisposable
{
    private readonly ILogger<LuaScriptEngine> _logger;
    private readonly ConcurrentDictionary<string, LoadedScript> _scripts = new();
    private readonly ConcurrentDictionary<string, ScriptStatistics> _statistics = new();
    private readonly ConcurrentDictionary<string, Timer> _timers = new();
    private readonly IrcApi _ircApi;
    private readonly object _luaLock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LuaScriptEngine"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="ircApi">The IRC API for scripts to use.</param>
    public LuaScriptEngine(ILogger<LuaScriptEngine> logger, IrcApi ircApi)
    {
        _logger = logger;
        _ircApi = ircApi;
    }

    /// <inheritdoc />
    public IReadOnlyList<LuaScript> LoadedScripts =>
        _scripts.Values.Select(s => s.Metadata).ToList();

    /// <inheritdoc />
    public async Task<LuaScript?> LoadScriptAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.LogError("Script file path is null or empty");
            return null;
        }

        if (!File.Exists(filePath))
        {
            _logger.LogError("Script file not found: {FilePath}", filePath);
            return null;
        }

        try
        {
            var code = await File.ReadAllTextAsync(filePath, cancellationToken);
            var scriptId = Path.GetFileNameWithoutExtension(filePath);

            // Unload existing script with same ID
            if (_scripts.ContainsKey(scriptId))
            {
                UnloadScript(scriptId);
            }

            var lua = CreateSandboxedLua();
            var registeredHandlers = new List<string>();

            // Execute the script to register handlers
            lock (_luaLock)
            {
                try
                {
                    lua.DoString(code, scriptId);

                    // Check for registered event handlers
                    foreach (var eventName in GetEventFunctionNames())
                    {
                        var func = lua[eventName];
                        if (func is LuaFunction)
                        {
                            registeredHandlers.Add(eventName);
                        }
                    }
                }
                catch (NLua.Exceptions.LuaException ex)
                {
                    _logger.LogError(ex, "Error loading script {ScriptId}: {Error}", scriptId, ex.Message);
                    lua.Dispose();
                    return null;
                }
            }

            // Extract metadata from script
            var metadata = new LuaScript
            {
                Id = scriptId,
                FilePath = filePath,
                Name = GetScriptString(lua, "script_name") ?? scriptId,
                Version = GetScriptString(lua, "script_version"),
                Author = GetScriptString(lua, "script_author"),
                Description = GetScriptString(lua, "script_description"),
                LoadedAt = DateTimeOffset.UtcNow,
                LastModified = File.GetLastWriteTimeUtc(filePath),
                RegisteredHandlers = registeredHandlers
            };

            var loadedScript = new LoadedScript
            {
                Metadata = metadata,
                Lua = lua
            };

            _scripts[scriptId] = loadedScript;
            _statistics[scriptId] = new ScriptStatistics { ScriptId = scriptId };

            _logger.LogInformation(
                "Loaded script {ScriptId} ({Name}) with {HandlerCount} event handlers",
                scriptId, metadata.Name, registeredHandlers.Count);

            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load script from {FilePath}", filePath);
            return null;
        }
    }

    /// <inheritdoc />
    public bool UnloadScript(string scriptId)
    {
        if (!_scripts.TryRemove(scriptId, out var script))
        {
            return false;
        }

        // Remove timers for this script
        var timerKeys = _timers.Keys.Where(k => k.StartsWith(scriptId + ":", StringComparison.Ordinal)).ToList();
        foreach (var key in timerKeys)
        {
            if (_timers.TryRemove(key, out var timer))
            {
                timer.Dispose();
            }
        }

        script.Lua.Dispose();
        _statistics.TryRemove(scriptId, out _);

        _logger.LogInformation("Unloaded script {ScriptId}", scriptId);
        return true;
    }

    /// <inheritdoc />
    public async Task<LuaScript?> ReloadScriptAsync(string scriptId, CancellationToken cancellationToken = default)
    {
        if (!_scripts.TryGetValue(scriptId, out var script))
        {
            _logger.LogWarning("Cannot reload unknown script: {ScriptId}", scriptId);
            return null;
        }

        var filePath = script.Metadata.FilePath;
        return await LoadScriptAsync(filePath, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> LoadScriptsFromDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
        {
            _logger.LogWarning("Scripts directory not found: {DirectoryPath}", directoryPath);
            return 0;
        }

        var luaFiles = Directory.GetFiles(directoryPath, "*.lua", SearchOption.TopDirectoryOnly);
        var loadedCount = 0;

        foreach (var file in luaFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var script = await LoadScriptAsync(file, cancellationToken);
            if (script != null)
            {
                loadedCount++;
            }
        }

        _logger.LogInformation("Loaded {Count} scripts from {DirectoryPath}", loadedCount, directoryPath);
        return loadedCount;
    }

    /// <inheritdoc />
    public async Task<ScriptEventContext> FireEventAsync(ScriptEventContext context, CancellationToken cancellationToken = default)
    {
        var eventFunctionName = GetEventFunctionName(context.Event);
        var stopwatch = Stopwatch.StartNew();

        foreach (var (scriptId, script) in _scripts)
        {
            if (!script.Metadata.IsEnabled)
            {
                continue;
            }

            if (!script.Metadata.RegisteredHandlers.Contains(eventFunctionName))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await ExecuteEventHandlerAsync(script, eventFunctionName, context, cancellationToken);

                if (_statistics.TryGetValue(scriptId, out var stats))
                {
                    stats.TotalCalls++;
                    stats.TotalExecutionTimeMs += result.ExecutionTimeMs;

                    if (!result.Success)
                    {
                        stats.ErrorCount++;
                        stats.LastError = result.Error;
                        stats.LastErrorTime = DateTimeOffset.UtcNow;
                    }
                }

                // Check if handler wants to cancel the event
                if (context.Cancel)
                {
                    _logger.LogDebug("Event {Event} cancelled by script {ScriptId}", context.Event, scriptId);
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing {Event} handler in script {ScriptId}", context.Event, scriptId);

                if (_statistics.TryGetValue(scriptId, out var stats))
                {
                    stats.ErrorCount++;
                    stats.LastError = ex.Message;
                    stats.LastErrorTime = DateTimeOffset.UtcNow;
                }
            }
        }

        return context;
    }

    /// <inheritdoc />
    public Task<LuaExecutionResult> ExecuteAsync(string code, string? scriptId = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Lua lua;

            if (scriptId != null && _scripts.TryGetValue(scriptId, out var script))
            {
                lua = script.Lua;
            }
            else
            {
                // Create temporary sandbox for direct execution
                lua = CreateSandboxedLua();
            }

            object[]? results;
            lock (_luaLock)
            {
                results = lua.DoString(code, scriptId ?? "direct");
            }

            stopwatch.Stop();

            // Dispose temporary lua if not from a script
            if (scriptId == null || !_scripts.ContainsKey(scriptId))
            {
                lua.Dispose();
            }

            return Task.FromResult(new LuaExecutionResult
            {
                Success = true,
                ReturnValues = results?.ToList() ?? [],
                ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
            });
        }
        catch (NLua.Exceptions.LuaException ex)
        {
            stopwatch.Stop();
            return Task.FromResult(new LuaExecutionResult
            {
                Success = false,
                Error = ex.Message,
                ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return Task.FromResult(new LuaExecutionResult
            {
                Success = false,
                Error = ex.Message,
                ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
            });
        }
    }

    /// <inheritdoc />
    public ScriptStatistics? GetStatistics(string scriptId)
    {
        return _statistics.TryGetValue(scriptId, out var stats) ? stats : null;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ScriptStatistics> GetAllStatistics()
    {
        return new Dictionary<string, ScriptStatistics>(_statistics);
    }

    /// <inheritdoc />
    public bool SetScriptEnabled(string scriptId, bool enabled)
    {
        if (!_scripts.TryGetValue(scriptId, out var script))
        {
            return false;
        }

        script.Metadata.IsEnabled = enabled;
        _logger.LogInformation("Script {ScriptId} {State}", scriptId, enabled ? "enabled" : "disabled");
        return true;
    }

    /// <inheritdoc />
    public bool RegisterTimer(string scriptId, string timerName, int intervalMs, bool repeat)
    {
        if (!_scripts.TryGetValue(scriptId, out var script))
        {
            return false;
        }

        var key = $"{scriptId}:{timerName}";

        // Remove existing timer
        if (_timers.TryRemove(key, out var existing))
        {
            existing.Dispose();
        }

        var timer = new Timer(
            _ => OnTimerElapsed(scriptId, timerName, repeat),
            null,
            intervalMs,
            repeat ? intervalMs : Timeout.Infinite);

        _timers[key] = timer;
        _logger.LogDebug("Registered timer {TimerName} for script {ScriptId} ({Interval}ms, repeat={Repeat})",
            timerName, scriptId, intervalMs, repeat);

        return true;
    }

    /// <inheritdoc />
    public bool UnregisterTimer(string scriptId, string timerName)
    {
        var key = $"{scriptId}:{timerName}";

        if (!_timers.TryRemove(key, out var timer))
        {
            return false;
        }

        timer.Dispose();
        _logger.LogDebug("Unregistered timer {TimerName} for script {ScriptId}", timerName, scriptId);
        return true;
    }

    /// <summary>
    /// Disposes the scripting engine and all loaded scripts.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Dispose all timers
        foreach (var timer in _timers.Values)
        {
            timer.Dispose();
        }
        _timers.Clear();

        // Dispose all Lua states
        foreach (var script in _scripts.Values)
        {
            script.Lua.Dispose();
        }
        _scripts.Clear();

        _logger.LogInformation("Lua scripting engine disposed");
    }

    #region Private Methods

    /// <summary>
    /// Creates a sandboxed Lua state with restricted functions.
    /// </summary>
    private Lua CreateSandboxedLua()
    {
        var lua = new Lua();
        lua.State.Encoding = System.Text.Encoding.UTF8;

        // Register IRC API
        lua["irc"] = _ircApi;

        // Remove dangerous functions
        lock (_luaLock)
        {
            // Sandbox: remove dangerous functions
            lua.DoString(@"
                -- Remove dangerous functions
                os.execute = nil
                os.exit = nil
                os.remove = nil
                os.rename = nil
                os.tmpname = nil
                os.setlocale = nil
                io = nil
                loadfile = nil
                dofile = nil
                debug = nil
                package.loadlib = nil
                package.searchpath = nil

                -- Safe subset of os
                local safe_os = {
                    time = os.time,
                    date = os.date,
                    difftime = os.difftime,
                    clock = os.clock
                }
                os = safe_os

                -- Utility functions
                function log(msg)
                    irc:Log(tostring(msg))
                end

                function sleep(ms)
                    -- Note: This is a placeholder, actual sleep would block
                    -- Scripts should use timers instead
                end
            ", "sandbox");
        }

        return lua;
    }

    /// <summary>
    /// Executes an event handler in a script.
    /// </summary>
    private Task<LuaExecutionResult> ExecuteEventHandlerAsync(
        LoadedScript script,
        string functionName,
        ScriptEventContext context,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            lock (_luaLock)
            {
                var func = script.Lua[functionName] as LuaFunction;
                if (func == null)
                {
                    return Task.FromResult(LuaExecutionResult.Fail($"Function {functionName} not found"));
                }

                // Convert context to Lua table
                var table = CreateEventTable(script.Lua, context);
                var results = func.Call(table);

                // Check if handler returned false to cancel
                if (results != null && results.Length > 0 && results[0] is bool cancel && !cancel)
                {
                    context.Cancel = true;
                }

                // Check if handler modified the message
                if (script.Lua["_replacement_message"] is string replacement)
                {
                    context.ReplacementMessage = replacement;
                    script.Lua["_replacement_message"] = null;
                }
            }

            stopwatch.Stop();
            return Task.FromResult(new LuaExecutionResult
            {
                Success = true,
                ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
            });
        }
        catch (NLua.Exceptions.LuaException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning("Lua error in {ScriptId}.{Function}: {Error}",
                script.Metadata.Id, functionName, ex.Message);

            return Task.FromResult(new LuaExecutionResult
            {
                Success = false,
                Error = ex.Message,
                ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
            });
        }
    }

    /// <summary>
    /// Creates a Lua table from an event context.
    /// </summary>
    private static LuaTable CreateEventTable(Lua lua, ScriptEventContext context)
    {
        var table = (LuaTable)lua.DoString("return {}")[0];

        table["event"] = context.Event.ToString();
        table["nick"] = context.Nick;
        table["hostmask"] = context.Hostmask;
        table["channel"] = context.Channel;
        table["target"] = context.Target;
        table["message"] = context.Message;
        table["command"] = context.Command;
        table["old_value"] = context.OldValue;
        table["new_value"] = context.NewValue;

        if (context.Parameters != null)
        {
            var paramsTable = (LuaTable)lua.DoString("return {}")[0];
            for (int i = 0; i < context.Parameters.Count; i++)
            {
                paramsTable[i + 1] = context.Parameters[i];
            }
            table["params"] = paramsTable;
        }

        return table;
    }

    /// <summary>
    /// Gets a string value from the Lua state.
    /// </summary>
    private static string? GetScriptString(Lua lua, string name)
    {
        return lua[name] as string;
    }

    /// <summary>
    /// Gets the function name for an event type.
    /// </summary>
    private static string GetEventFunctionName(ScriptEvent eventType)
    {
        return eventType switch
        {
            ScriptEvent.UserConnect => "on_connect",
            ScriptEvent.UserDisconnect => "on_disconnect",
            ScriptEvent.NickChange => "on_nick",
            ScriptEvent.ChannelJoin => "on_join",
            ScriptEvent.ChannelPart => "on_part",
            ScriptEvent.ChannelKick => "on_kick",
            ScriptEvent.ChannelMessage => "on_message",
            ScriptEvent.PrivateMessage => "on_privmsg",
            ScriptEvent.Notice => "on_notice",
            ScriptEvent.TopicChange => "on_topic",
            ScriptEvent.ChannelModeChange => "on_channel_mode",
            ScriptEvent.UserModeChange => "on_user_mode",
            ScriptEvent.Away => "on_away",
            ScriptEvent.Back => "on_back",
            ScriptEvent.Invite => "on_invite",
            ScriptEvent.Command => "on_command",
            ScriptEvent.Timer => "on_timer",
            ScriptEvent.ServerStart => "on_server_start",
            ScriptEvent.ServerStop => "on_server_stop",
            ScriptEvent.AccountLogin => "on_login",
            ScriptEvent.AccountLogout => "on_logout",
            _ => $"on_{eventType.ToString().ToLowerInvariant()}"
        };
    }

    /// <summary>
    /// Gets all possible event function names.
    /// </summary>
    private static IEnumerable<string> GetEventFunctionNames()
    {
        return Enum.GetValues<ScriptEvent>().Select(GetEventFunctionName);
    }

    /// <summary>
    /// Handles timer elapsed events.
    /// </summary>
    private void OnTimerElapsed(string scriptId, string timerName, bool repeat)
    {
        if (!_scripts.TryGetValue(scriptId, out var script) || !script.Metadata.IsEnabled)
        {
            return;
        }

        try
        {
            var context = new ScriptEventContext
            {
                Event = ScriptEvent.Timer,
                Extra = new Dictionary<string, object?> { ["timer_name"] = timerName }
            };

            // Fire timer event synchronously
            lock (_luaLock)
            {
                var func = script.Lua["on_timer"] as LuaFunction;
                func?.Call(timerName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in timer {TimerName} for script {ScriptId}", timerName, scriptId);
        }

        // If not repeating, remove the timer
        if (!repeat)
        {
            UnregisterTimer(scriptId, timerName);
        }
    }

    #endregion

    /// <summary>
    /// Internal class representing a loaded script with its Lua state.
    /// </summary>
    private sealed class LoadedScript
    {
        public required LuaScript Metadata { get; init; }
        public required Lua Lua { get; init; }
    }
}

/// <summary>
/// IRC API exposed to Lua scripts.
/// </summary>
public class IrcApi
{
    private readonly ILogger _logger;
    private readonly Func<string, string, Task>? _sendMessage;
    private readonly Func<string, string, Task>? _sendNotice;
    private readonly Func<string, string, string, Task>? _kickUser;
    private readonly Func<string, string, Task>? _setMode;
    private readonly Func<string, string?, TimeSpan?, Task>? _setBan;

    /// <summary>
    /// Initializes a new instance of the <see cref="IrcApi"/> class.
    /// </summary>
    public IrcApi(
        ILogger logger,
        Func<string, string, Task>? sendMessage = null,
        Func<string, string, Task>? sendNotice = null,
        Func<string, string, string, Task>? kickUser = null,
        Func<string, string, Task>? setMode = null,
        Func<string, string?, TimeSpan?, Task>? setBan = null)
    {
        _logger = logger;
        _sendMessage = sendMessage;
        _sendNotice = sendNotice;
        _kickUser = kickUser;
        _setMode = setMode;
        _setBan = setBan;
    }

    /// <summary>
    /// Logs a message from a script.
    /// </summary>
    public void Log(string message)
    {
        _logger.LogInformation("[Script] {Message}", message);
    }

    /// <summary>
    /// Sends a message to a channel or user.
    /// </summary>
    public void SendMessage(string target, string message)
    {
        _sendMessage?.Invoke(target, message);
    }

    /// <summary>
    /// Sends a notice to a channel or user.
    /// </summary>
    public void SendNotice(string target, string message)
    {
        _sendNotice?.Invoke(target, message);
    }

    /// <summary>
    /// Kicks a user from a channel.
    /// </summary>
    public void Kick(string channel, string nick, string reason)
    {
        _kickUser?.Invoke(channel, nick, reason);
    }

    /// <summary>
    /// Sets a mode on a channel or user.
    /// </summary>
    public void SetMode(string target, string mode)
    {
        _setMode?.Invoke(target, mode);
    }

    /// <summary>
    /// Sets a ban on a channel.
    /// </summary>
    public void Ban(string channel, string mask, int? durationSeconds = null)
    {
        var duration = durationSeconds.HasValue
            ? TimeSpan.FromSeconds(durationSeconds.Value)
            : (TimeSpan?)null;
        _setBan?.Invoke(channel, mask, duration);
    }

    /// <summary>
    /// Gets the current Unix timestamp.
    /// </summary>
#pragma warning disable CA1822 // Member can be static - exposed to Lua as instance method
    public long Time()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    /// <summary>
    /// Formats a timestamp as a string.
    /// </summary>
    public string FormatTime(long timestamp, string format = "yyyy-MM-dd HH:mm:ss")
    {
        return DateTimeOffset.FromUnixTimeSeconds(timestamp).ToString(format, CultureInfo.InvariantCulture);
    }
#pragma warning restore CA1822
}
