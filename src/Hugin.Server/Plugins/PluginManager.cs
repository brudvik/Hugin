// Licensed to the Hugin IRC Server under one or more agreements.
// The Hugin IRC Server licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hugin.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hugin.Server.Plugins;

/// <summary>
/// Manages plugin discovery, loading, and lifecycle.
/// </summary>
public sealed class PluginManager : IPluginManager, IDisposable
{
    private readonly ILogger<PluginManager> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _pluginsDirectory;
    private readonly string _serverVersion;
    private readonly string _serverName;

    private readonly ConcurrentDictionary<string, LoadedPlugin> _plugins = new();
    private readonly ConcurrentDictionary<string, PluginInfo> _discoveredPlugins = new();
    private readonly ConcurrentDictionary<string, List<PluginEventCallback>> _eventHandlers = new();
    private readonly ConcurrentDictionary<string, PluginCommandHandler> _commandHandlers = new();

    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginManager"/> class.
    /// </summary>
    public PluginManager(
        ILogger<PluginManager> logger,
        IServiceProvider serviceProvider,
        string pluginsDirectory,
        string serverName = "irc.hugin.local",
        string serverVersion = "1.0.0")
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _pluginsDirectory = pluginsDirectory;
        _serverName = serverName;
        _serverVersion = serverVersion;

        // Ensure plugins directory exists
        if (!Directory.Exists(_pluginsDirectory))
        {
            Directory.CreateDirectory(_pluginsDirectory);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<PluginInfo> Plugins =>
        _discoveredPlugins.Values.ToList();

    /// <inheritdoc />
    public IReadOnlyList<PluginInfo> EnabledPlugins =>
        _plugins.Values.Where(p => p.Info.IsEnabled).Select(p => p.Info).ToList();

    /// <inheritdoc />
    public async Task<int> DiscoverPluginsAsync(CancellationToken cancellationToken = default)
    {
        var pluginDirs = Directory.GetDirectories(_pluginsDirectory);
        var discovered = 0;

        foreach (var dir in pluginDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var manifestPath = Path.Combine(dir, "plugin.json");
            if (!File.Exists(manifestPath))
            {
                _logger.LogDebug("No plugin.json found in {Directory}", dir);
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                var manifest = JsonSerializer.Deserialize<PluginManifest>(json, JsonOptions);

                if (manifest == null)
                {
                    _logger.LogWarning("Failed to parse plugin manifest: {Path}", manifestPath);
                    continue;
                }

                var info = new PluginInfo
                {
                    Manifest = manifest,
                    PluginDirectory = dir,
                    IsEnabled = manifest.EnabledByDefault
                };

                _discoveredPlugins[manifest.Id] = info;
                discovered++;

                _logger.LogInformation(
                    "Discovered plugin: {Id} ({Name} v{Version})",
                    manifest.Id, manifest.Name, manifest.Version);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse plugin manifest: {Path}", manifestPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error discovering plugin in {Directory}", dir);
            }
        }

        _logger.LogInformation("Discovered {Count} plugins", discovered);
        return discovered;
    }

    /// <inheritdoc />
    public async Task<bool> LoadPluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        if (_plugins.ContainsKey(pluginId))
        {
            _logger.LogWarning("Plugin {PluginId} is already loaded", pluginId);
            return false;
        }

        if (!_discoveredPlugins.TryGetValue(pluginId, out var info))
        {
            _logger.LogWarning("Plugin {PluginId} not found", pluginId);
            return false;
        }

        // Check dependencies
        if (info.Manifest.Dependencies != null)
        {
            foreach (var depId in info.Manifest.Dependencies)
            {
                if (!_plugins.ContainsKey(depId))
                {
                    _logger.LogWarning(
                        "Plugin {PluginId} requires {DependencyId} which is not loaded",
                        pluginId, depId);

                    // Try to load dependency
                    if (!await LoadPluginAsync(depId, cancellationToken))
                    {
                        info.LoadError = $"Failed to load dependency: {depId}";
                        return false;
                    }
                }
            }
        }

        try
        {
            var assemblyPath = Path.Combine(info.PluginDirectory, info.Manifest.Assembly);
            if (!File.Exists(assemblyPath))
            {
                info.LoadError = $"Assembly not found: {info.Manifest.Assembly}";
                _logger.LogError("Plugin assembly not found: {Path}", assemblyPath);
                return false;
            }

            // Create isolated load context
            var loadContext = new PluginLoadContext(assemblyPath, info.PluginDirectory);
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

            // Find entry point
            var entryPointType = assembly.GetType(info.Manifest.EntryPoint);
            if (entryPointType == null)
            {
                info.LoadError = $"Entry point not found: {info.Manifest.EntryPoint}";
                _logger.LogError("Plugin entry point not found: {EntryPoint}", info.Manifest.EntryPoint);
                loadContext.Unload();
                return false;
            }

            if (!typeof(IPlugin).IsAssignableFrom(entryPointType))
            {
                info.LoadError = $"Entry point does not implement IPlugin: {info.Manifest.EntryPoint}";
                _logger.LogError("Plugin entry point does not implement IPlugin");
                loadContext.Unload();
                return false;
            }

            // Create plugin instance
            var plugin = (IPlugin?)Activator.CreateInstance(entryPointType);
            if (plugin == null)
            {
                info.LoadError = "Failed to create plugin instance";
                _logger.LogError("Failed to create plugin instance for {PluginId}", pluginId);
                loadContext.Unload();
                return false;
            }

            // Create plugin context
            var context = new PluginContext(
                info,
                _serverName,
                _serverVersion,
                _serviceProvider,
                _logger,
                RegisterCommand,
                UnregisterCommand,
                RegisterEventHandler,
                UnregisterEventHandler);

            // Initialize plugin
            await plugin.OnLoadAsync(context, cancellationToken);

            var loadedPlugin = new LoadedPlugin
            {
                Info = info,
                Plugin = plugin,
                Context = context,
                LoadContext = loadContext
            };

            _plugins[pluginId] = loadedPlugin;
            info.IsLoaded = true;
            info.IsEnabled = true;
            info.LoadedAt = DateTimeOffset.UtcNow;
            info.LoadError = null;

            _logger.LogInformation(
                "Loaded plugin: {PluginId} ({Name} v{Version})",
                pluginId, info.Manifest.Name, info.Manifest.Version);

            return true;
        }
        catch (Exception ex)
        {
            info.LoadError = ex.Message;
            _logger.LogError(ex, "Failed to load plugin {PluginId}", pluginId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UnloadPluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        if (!_plugins.TryRemove(pluginId, out var loadedPlugin))
        {
            _logger.LogWarning("Plugin {PluginId} is not loaded", pluginId);
            return false;
        }

        try
        {
            // Call unload hook
            await loadedPlugin.Plugin.OnUnloadAsync(cancellationToken);

            // Unregister all commands from this plugin
            var commandsToRemove = _commandHandlers.Keys
                .Where(k => k.StartsWith(pluginId + ":", StringComparison.Ordinal))
                .ToList();
            foreach (var cmd in commandsToRemove)
            {
                _commandHandlers.TryRemove(cmd, out _);
            }

            // Unload assembly context
            loadedPlugin.LoadContext.Unload();

            loadedPlugin.Info.IsLoaded = false;
            loadedPlugin.Info.IsEnabled = false;
            loadedPlugin.Info.LoadedAt = null;

            _logger.LogInformation("Unloaded plugin: {PluginId}", pluginId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unloading plugin {PluginId}", pluginId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ReloadPluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        await UnloadPluginAsync(pluginId, cancellationToken);

        // Force GC to release the assembly
        GC.Collect();
        GC.WaitForPendingFinalizers();

        return await LoadPluginAsync(pluginId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> EnablePluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        if (!_plugins.TryGetValue(pluginId, out var loadedPlugin))
        {
            // Plugin not loaded, try to load it
            return await LoadPluginAsync(pluginId, cancellationToken);
        }

        if (loadedPlugin.Info.IsEnabled)
        {
            return true;
        }

        try
        {
            await loadedPlugin.Plugin.OnEnableAsync(cancellationToken);
            loadedPlugin.Info.IsEnabled = true;
            _logger.LogInformation("Enabled plugin: {PluginId}", pluginId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable plugin {PluginId}", pluginId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DisablePluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        if (!_plugins.TryGetValue(pluginId, out var loadedPlugin))
        {
            return false;
        }

        if (!loadedPlugin.Info.IsEnabled)
        {
            return true;
        }

        try
        {
            await loadedPlugin.Plugin.OnDisableAsync(cancellationToken);
            loadedPlugin.Info.IsEnabled = false;
            _logger.LogInformation("Disabled plugin: {PluginId}", pluginId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable plugin {PluginId}", pluginId);
            return false;
        }
    }

    /// <inheritdoc />
    public PluginInfo? GetPlugin(string pluginId)
    {
        return _discoveredPlugins.TryGetValue(pluginId, out var info) ? info : null;
    }

    /// <inheritdoc />
    public async Task<int> LoadAllEnabledPluginsAsync(CancellationToken cancellationToken = default)
    {
        await DiscoverPluginsAsync(cancellationToken);

        var loadedCount = 0;
        foreach (var info in _discoveredPlugins.Values.Where(p => p.IsEnabled))
        {
            if (await LoadPluginAsync(info.Manifest.Id, cancellationToken))
            {
                loadedCount++;
            }
        }

        _logger.LogInformation("Loaded {Count} plugins", loadedCount);
        return loadedCount;
    }

    /// <inheritdoc />
    public async Task UnloadAllPluginsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var pluginId in _plugins.Keys.ToList())
        {
            await UnloadPluginAsync(pluginId, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task NotifyConfigReloadAsync(CancellationToken cancellationToken = default)
    {
        foreach (var plugin in _plugins.Values.Where(p => p.Info.IsEnabled))
        {
            try
            {
                await plugin.Plugin.OnConfigReloadAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying plugin {PluginId} of config reload", plugin.Info.Manifest.Id);
            }
        }
    }

    /// <inheritdoc />
    public async Task<PluginEventContext> FireEventAsync(PluginEventContext context, CancellationToken cancellationToken = default)
    {
        var eventKey = context.EventType.ToString();

        if (!_eventHandlers.TryGetValue(eventKey, out var handlers) || handlers.Count == 0)
        {
            return context;
        }

        foreach (var handler in handlers.ToList())
        {
            try
            {
                var shouldContinue = await handler(context, cancellationToken);
                if (!shouldContinue || context.Cancel)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in plugin event handler for {EventType}", context.EventType);
            }
        }

        return context;
    }

    /// <summary>
    /// Disposes the plugin manager and unloads all plugins.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Unload all plugins synchronously
        foreach (var plugin in _plugins.Values)
        {
            try
            {
                plugin.Plugin.OnUnloadAsync(CancellationToken.None).GetAwaiter().GetResult();
                plugin.LoadContext.Unload();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing plugin {PluginId}", plugin.Info.Manifest.Id);
            }
        }

        _plugins.Clear();
        _logger.LogInformation("Plugin manager disposed");
    }

    #region Private Methods

    private void RegisterCommand(string pluginId, string command, PluginCommandHandler handler)
    {
        var key = $"{pluginId}:{command.ToUpperInvariant()}";
        _commandHandlers[key] = handler;
        _logger.LogDebug("Registered command {Command} for plugin {PluginId}", command, pluginId);
    }

    private void UnregisterCommand(string pluginId, string command)
    {
        var key = $"{pluginId}:{command.ToUpperInvariant()}";
        _commandHandlers.TryRemove(key, out _);
        _logger.LogDebug("Unregistered command {Command} for plugin {PluginId}", command, pluginId);
    }

    private void RegisterEventHandler(string pluginId, PluginEventType eventType, PluginEventCallback handler)
    {
        var key = eventType.ToString();
        _eventHandlers.AddOrUpdate(
            key,
            _ => [handler],
            (_, list) =>
            {
                lock (list)
                {
                    list.Add(handler);
                }
                return list;
            });
        _logger.LogDebug("Registered event handler for {EventType} from plugin {PluginId}", eventType, pluginId);
    }

    private void UnregisterEventHandler(string pluginId, PluginEventType eventType, PluginEventCallback handler)
    {
        var key = eventType.ToString();
        if (_eventHandlers.TryGetValue(key, out var handlers))
        {
            lock (handlers)
            {
                handlers.Remove(handler);
            }
        }
    }

    #endregion

    /// <summary>
    /// Represents a loaded plugin with its runtime state.
    /// </summary>
    private sealed class LoadedPlugin
    {
        public required PluginInfo Info { get; init; }
        public required IPlugin Plugin { get; init; }
        public required PluginContext Context { get; init; }
        public required PluginLoadContext LoadContext { get; init; }
    }
}

/// <summary>
/// AssemblyLoadContext for plugin isolation.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath, string pluginDirectory) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }
}

/// <summary>
/// Implementation of IPluginContext.
/// </summary>
internal sealed class PluginContext : IPluginContext
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly Action<string, string, PluginCommandHandler> _registerCommand;
    private readonly Action<string, string> _unregisterCommand;
    private readonly Action<string, PluginEventType, PluginEventCallback> _registerEventHandler;
    private readonly Action<string, PluginEventType, PluginEventCallback> _unregisterEventHandler;
    private readonly List<CancellationTokenSource> _scheduledTasks = [];

    public PluginContext(
        PluginInfo pluginInfo,
        string serverName,
        string serverVersion,
        IServiceProvider serviceProvider,
        ILogger logger,
        Action<string, string, PluginCommandHandler> registerCommand,
        Action<string, string> unregisterCommand,
        Action<string, PluginEventType, PluginEventCallback> registerEventHandler,
        Action<string, PluginEventType, PluginEventCallback> unregisterEventHandler)
    {
        PluginInfo = pluginInfo;
        ServerName = serverName;
        ServerVersion = serverVersion;
        State = pluginInfo.State;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _registerCommand = registerCommand;
        _unregisterCommand = unregisterCommand;
        _registerEventHandler = registerEventHandler;
        _unregisterEventHandler = unregisterEventHandler;
    }

    public PluginInfo PluginInfo { get; }
    public string ServerName { get; }
    public string ServerVersion { get; }
    public PluginState State { get; }

    public T? GetService<T>() where T : class
    {
        return _serviceProvider.GetService<T>();
    }

    public T GetRequiredService<T>() where T : class
    {
        return _serviceProvider.GetRequiredService<T>();
    }

    public void RegisterCommand(string command, PluginCommandHandler handler)
    {
        _registerCommand(PluginInfo.Manifest.Id, command, handler);
    }

    public void UnregisterCommand(string command)
    {
        _unregisterCommand(PluginInfo.Manifest.Id, command);
    }

    public void RegisterEventHandler(PluginEventType eventType, PluginEventCallback handler)
    {
        _registerEventHandler(PluginInfo.Manifest.Id, eventType, handler);
    }

    public void UnregisterEventHandler(PluginEventType eventType, PluginEventCallback handler)
    {
        _unregisterEventHandler(PluginInfo.Manifest.Id, eventType, handler);
    }

#pragma warning disable CA2254 // Template should be static - plugin provides dynamic templates
    public void LogDebug(string message, params object[] args)
    {
        _logger.LogDebug($"[{PluginInfo.Manifest.Id}] {message}", args);
    }

    public void LogInfo(string message, params object[] args)
    {
        _logger.LogInformation($"[{PluginInfo.Manifest.Id}] {message}", args);
    }

    public void LogWarning(string message, params object[] args)
    {
        _logger.LogWarning($"[{PluginInfo.Manifest.Id}] {message}", args);
    }

    public void LogError(string message, params object[] args)
    {
        _logger.LogError($"[{PluginInfo.Manifest.Id}] {message}", args);
    }

    public void LogError(Exception exception, string message, params object[] args)
    {
        _logger.LogError(exception, $"[{PluginInfo.Manifest.Id}] {message}", args);
    }
#pragma warning restore CA2254

    public Task SendMessageAsync(string target, string message, CancellationToken cancellationToken = default)
    {
        // This would be implemented to use the actual IRC message sending
        LogDebug("PRIVMSG {Target} :{Message}", target, message);
        return Task.CompletedTask;
    }

    public Task SendNoticeAsync(string target, string message, CancellationToken cancellationToken = default)
    {
        LogDebug("NOTICE {Target} :{Message}", target, message);
        return Task.CompletedTask;
    }

    public Task SendRawAsync(string rawMessage, CancellationToken cancellationToken = default)
    {
        LogDebug("RAW: {Message}", rawMessage);
        return Task.CompletedTask;
    }

    public T GetConfig<T>(string key, T defaultValue)
    {
        // This would be implemented to read from plugin configuration
        return defaultValue;
    }

    public CancellationTokenSource Schedule(TimeSpan delay, Func<CancellationToken, Task> action)
    {
        var cts = new CancellationTokenSource();
        _scheduledTasks.Add(cts);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token);
                if (!cts.IsCancellationRequested)
                {
                    await action(cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (Exception ex)
            {
                LogError(ex, "Error in scheduled task");
            }
            finally
            {
                _scheduledTasks.Remove(cts);
            }
        }, cts.Token);

        return cts;
    }

    public CancellationTokenSource ScheduleRecurring(TimeSpan interval, Func<CancellationToken, Task> action)
    {
        var cts = new CancellationTokenSource();
        _scheduledTasks.Add(cts);

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(interval, cts.Token);
                    if (!cts.IsCancellationRequested)
                    {
                        await action(cts.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (Exception ex)
            {
                LogError(ex, "Error in recurring task");
            }
            finally
            {
                _scheduledTasks.Remove(cts);
            }
        }, cts.Token);

        return cts;
    }
}
