// Licensed to the Hugin IRC Server under one or more agreements.
// The Hugin IRC Server licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hugin.Core.Plugins;

/// <summary>
/// Represents plugin metadata from plugin.json manifest.
/// </summary>
public sealed class PluginManifest
{
    /// <summary>
    /// Gets the unique identifier for the plugin.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the display name of the plugin.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the plugin version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Gets the plugin author.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Gets the plugin description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the plugin website or repository URL.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// Gets the main assembly DLL name (without path).
    /// </summary>
    public required string Assembly { get; init; }

    /// <summary>
    /// Gets the fully qualified name of the plugin entry point class.
    /// Must implement IPlugin.
    /// </summary>
    public required string EntryPoint { get; init; }

    /// <summary>
    /// Gets the minimum Hugin server version required.
    /// </summary>
    public string? MinServerVersion { get; init; }

    /// <summary>
    /// Gets the IDs of plugins this plugin depends on.
    /// </summary>
    public IReadOnlyList<string>? Dependencies { get; init; }

    /// <summary>
    /// Gets the permissions required by this plugin.
    /// </summary>
    public IReadOnlyList<string>? Permissions { get; init; }

    /// <summary>
    /// Gets whether this plugin is enabled by default.
    /// </summary>
    public bool EnabledByDefault { get; init; } = true;

    /// <summary>
    /// Gets the plugin license.
    /// </summary>
    public string? License { get; init; }
}

/// <summary>
/// Represents the runtime state of a loaded plugin.
/// </summary>
public sealed class PluginInfo
{
    /// <summary>
    /// Gets the plugin manifest.
    /// </summary>
    public required PluginManifest Manifest { get; init; }

    /// <summary>
    /// Gets the directory where the plugin is installed.
    /// </summary>
    public required string PluginDirectory { get; init; }

    /// <summary>
    /// Gets whether the plugin is currently enabled.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Gets whether the plugin is currently loaded.
    /// </summary>
    public bool IsLoaded { get; set; }

    /// <summary>
    /// Gets the time the plugin was loaded.
    /// </summary>
    public DateTimeOffset? LoadedAt { get; set; }

    /// <summary>
    /// Gets any error message if the plugin failed to load.
    /// </summary>
    public string? LoadError { get; set; }

    /// <summary>
    /// Gets the plugin state (for plugin-specific data).
    /// </summary>
    public PluginState State { get; } = new();
}

/// <summary>
/// Represents persistent state for a plugin.
/// </summary>
public sealed class PluginState
{
    private readonly Dictionary<string, object?> _data = new();

    /// <summary>
    /// Gets or sets a value in the plugin state.
    /// </summary>
    public object? this[string key]
    {
        get => _data.TryGetValue(key, out var value) ? value : null;
        set => _data[key] = value;
    }

    /// <summary>
    /// Gets a typed value from the plugin state.
    /// </summary>
    public T? Get<T>(string key)
    {
        return _data.TryGetValue(key, out var value) && value is T typed ? typed : default;
    }

    /// <summary>
    /// Sets a value in the plugin state.
    /// </summary>
    public void Set<T>(string key, T value)
    {
        _data[key] = value;
    }

    /// <summary>
    /// Removes a value from the plugin state.
    /// </summary>
    public bool Remove(string key)
    {
        return _data.Remove(key);
    }

    /// <summary>
    /// Clears all plugin state.
    /// </summary>
    public void Clear()
    {
        _data.Clear();
    }
}

/// <summary>
/// Interface that all plugins must implement.
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// Called when the plugin is loaded.
    /// Use this to initialize resources, register commands, etc.
    /// </summary>
    /// <param name="context">The plugin context providing access to server APIs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnLoadAsync(IPluginContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when the plugin is being unloaded.
    /// Use this to cleanup resources, save state, etc.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnUnloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when the plugin is enabled (after being disabled).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnEnableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when the plugin is disabled (but not unloaded).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnDisableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when the server configuration is reloaded.
    /// Use this to re-read any configuration settings.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnConfigReloadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Base class for plugins that provides default implementations.
/// </summary>
public abstract class PluginBase : IPlugin
{
    /// <summary>
    /// Gets the plugin context.
    /// </summary>
    protected IPluginContext? Context { get; private set; }

    /// <inheritdoc />
    public virtual Task OnLoadAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        Context = context;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task OnUnloadAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task OnEnableAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task OnDisableAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task OnConfigReloadAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Context provided to plugins for accessing server APIs.
/// </summary>
public interface IPluginContext
{
    /// <summary>
    /// Gets the plugin info.
    /// </summary>
    PluginInfo PluginInfo { get; }

    /// <summary>
    /// Gets the server name.
    /// </summary>
    string ServerName { get; }

    /// <summary>
    /// Gets the server version.
    /// </summary>
    string ServerVersion { get; }

    /// <summary>
    /// Gets the plugin state for persistent storage.
    /// </summary>
    PluginState State { get; }

    /// <summary>
    /// Gets a service from the DI container.
    /// </summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <returns>The service instance or null.</returns>
    T? GetService<T>() where T : class;

    /// <summary>
    /// Gets a required service from the DI container.
    /// </summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <returns>The service instance.</returns>
    /// <exception cref="InvalidOperationException">If the service is not registered.</exception>
    T GetRequiredService<T>() where T : class;

    /// <summary>
    /// Registers a command handler.
    /// </summary>
    /// <param name="command">The command name.</param>
    /// <param name="handler">The command handler delegate.</param>
    void RegisterCommand(string command, PluginCommandHandler handler);

    /// <summary>
    /// Unregisters a command handler.
    /// </summary>
    /// <param name="command">The command name.</param>
    void UnregisterCommand(string command);

    /// <summary>
    /// Registers an event handler.
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <param name="handler">The event handler delegate.</param>
    void RegisterEventHandler(PluginEventType eventType, PluginEventCallback handler);

    /// <summary>
    /// Unregisters an event handler.
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <param name="handler">The event handler delegate.</param>
    void UnregisterEventHandler(PluginEventType eventType, PluginEventCallback handler);

    /// <summary>
    /// Logs a debug message.
    /// </summary>
    void LogDebug(string message, params object[] args);

    /// <summary>
    /// Logs an information message.
    /// </summary>
    void LogInfo(string message, params object[] args);

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    void LogWarning(string message, params object[] args);

    /// <summary>
    /// Logs an error message.
    /// </summary>
    void LogError(string message, params object[] args);

    /// <summary>
    /// Logs an error message with an exception.
    /// </summary>
    void LogError(Exception exception, string message, params object[] args);

    /// <summary>
    /// Sends a message to a target (channel or user).
    /// </summary>
    Task SendMessageAsync(string target, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a notice to a target (channel or user).
    /// </summary>
    Task SendNoticeAsync(string target, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a raw IRC message.
    /// </summary>
    Task SendRawAsync(string rawMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets plugin configuration value.
    /// </summary>
    /// <typeparam name="T">The configuration value type.</typeparam>
    /// <param name="key">The configuration key.</param>
    /// <param name="defaultValue">Default value if not found.</param>
    /// <returns>The configuration value.</returns>
    T GetConfig<T>(string key, T defaultValue);

    /// <summary>
    /// Schedules a task to run after a delay.
    /// </summary>
    /// <param name="delay">The delay before executing.</param>
    /// <param name="action">The action to execute.</param>
    /// <returns>A cancellation token to cancel the scheduled task.</returns>
    CancellationTokenSource Schedule(TimeSpan delay, Func<CancellationToken, Task> action);

    /// <summary>
    /// Schedules a recurring task.
    /// </summary>
    /// <param name="interval">The interval between executions.</param>
    /// <param name="action">The action to execute.</param>
    /// <returns>A cancellation token to cancel the scheduled task.</returns>
    CancellationTokenSource ScheduleRecurring(TimeSpan interval, Func<CancellationToken, Task> action);
}

/// <summary>
/// Delegate for plugin command handlers.
/// </summary>
/// <param name="context">The command context.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>True if the command was handled.</returns>
public delegate Task<bool> PluginCommandHandler(PluginCommandContext context, CancellationToken cancellationToken);

/// <summary>
/// Delegate for plugin event callbacks.
/// </summary>
/// <param name="context">The event context.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>True to allow the event to continue, false to cancel.</returns>
public delegate Task<bool> PluginEventCallback(PluginEventContext context, CancellationToken cancellationToken);

/// <summary>
/// Context for plugin commands.
/// </summary>
public sealed class PluginCommandContext
{
    /// <summary>
    /// Gets the command name.
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// Gets the command parameters.
    /// </summary>
    public required IReadOnlyList<string> Parameters { get; init; }

    /// <summary>
    /// Gets the raw message line.
    /// </summary>
    public required string RawMessage { get; init; }

    /// <summary>
    /// Gets the source user nickname.
    /// </summary>
    public required string Nick { get; init; }

    /// <summary>
    /// Gets the source user hostmask.
    /// </summary>
    public string? Hostmask { get; init; }

    /// <summary>
    /// Gets the source user account name (if logged in).
    /// </summary>
    public string? Account { get; init; }

    /// <summary>
    /// Gets whether the source user is an operator.
    /// </summary>
    public bool IsOperator { get; init; }

    /// <summary>
    /// Gets the target (channel or nick for responses).
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Sends a reply to the appropriate target.
    /// </summary>
    public Func<string, CancellationToken, Task>? Reply { get; init; }
}

/// <summary>
/// Context for plugin events.
/// </summary>
public sealed class PluginEventContext
{
    /// <summary>
    /// Gets the event type.
    /// </summary>
    public required PluginEventType EventType { get; init; }

    /// <summary>
    /// Gets the source user nickname.
    /// </summary>
    public string? Nick { get; init; }

    /// <summary>
    /// Gets the source user hostmask.
    /// </summary>
    public string? Hostmask { get; init; }

    /// <summary>
    /// Gets the channel (if applicable).
    /// </summary>
    public string? Channel { get; init; }

    /// <summary>
    /// Gets the target (if applicable).
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the message (if applicable).
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets the old value (for change events).
    /// </summary>
    public string? OldValue { get; init; }

    /// <summary>
    /// Gets the new value (for change events).
    /// </summary>
    public string? NewValue { get; init; }

    /// <summary>
    /// Gets additional event data.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Data { get; init; }

    /// <summary>
    /// Gets or sets whether this event should be cancelled.
    /// </summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// Plugin event types.
/// </summary>
public enum PluginEventType
{
    /// <summary>User connected.</summary>
    UserConnect,

    /// <summary>User disconnected.</summary>
    UserDisconnect,

    /// <summary>User registered (completed connection).</summary>
    UserRegister,

    /// <summary>User joined channel.</summary>
    ChannelJoin,

    /// <summary>User left channel.</summary>
    ChannelPart,

    /// <summary>User kicked from channel.</summary>
    ChannelKick,

    /// <summary>Channel message received.</summary>
    ChannelMessage,

    /// <summary>Private message received.</summary>
    PrivateMessage,

    /// <summary>Notice received.</summary>
    Notice,

    /// <summary>Nickname changed.</summary>
    NickChange,

    /// <summary>User quit.</summary>
    Quit,

    /// <summary>Channel topic changed.</summary>
    TopicChange,

    /// <summary>Channel mode changed.</summary>
    ChannelModeChange,

    /// <summary>User mode changed.</summary>
    UserModeChange,

    /// <summary>User logged into account.</summary>
    AccountLogin,

    /// <summary>User logged out of account.</summary>
    AccountLogout,

    /// <summary>Channel created.</summary>
    ChannelCreate,

    /// <summary>Channel destroyed (empty).</summary>
    ChannelDestroy,

    /// <summary>Server linking.</summary>
    ServerLink,

    /// <summary>Server split.</summary>
    ServerSplit,

    /// <summary>Configuration reloaded.</summary>
    ConfigReload
}

/// <summary>
/// Interface for the plugin manager.
/// </summary>
public interface IPluginManager
{
    /// <summary>
    /// Gets all discovered plugins.
    /// </summary>
    IReadOnlyList<PluginInfo> Plugins { get; }

    /// <summary>
    /// Gets all loaded and enabled plugins.
    /// </summary>
    IReadOnlyList<PluginInfo> EnabledPlugins { get; }

    /// <summary>
    /// Discovers plugins from the plugins directory.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of plugins discovered.</returns>
    Task<int> DiscoverPluginsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a plugin by ID.
    /// </summary>
    /// <param name="pluginId">The plugin ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if loaded successfully.</returns>
    Task<bool> LoadPluginAsync(string pluginId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unloads a plugin by ID.
    /// </summary>
    /// <param name="pluginId">The plugin ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if unloaded successfully.</returns>
    Task<bool> UnloadPluginAsync(string pluginId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads a plugin by ID.
    /// </summary>
    /// <param name="pluginId">The plugin ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if reloaded successfully.</returns>
    Task<bool> ReloadPluginAsync(string pluginId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables a plugin by ID.
    /// </summary>
    /// <param name="pluginId">The plugin ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if enabled successfully.</returns>
    Task<bool> EnablePluginAsync(string pluginId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables a plugin by ID.
    /// </summary>
    /// <param name="pluginId">The plugin ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if disabled successfully.</returns>
    Task<bool> DisablePluginAsync(string pluginId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a plugin by ID.
    /// </summary>
    /// <param name="pluginId">The plugin ID.</param>
    /// <returns>The plugin info or null.</returns>
    PluginInfo? GetPlugin(string pluginId);

    /// <summary>
    /// Loads all enabled plugins.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of plugins loaded.</returns>
    Task<int> LoadAllEnabledPluginsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Unloads all plugins.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UnloadAllPluginsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies all plugins of a configuration reload.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task NotifyConfigReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fires an event to all enabled plugins.
    /// </summary>
    /// <param name="context">The event context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The modified context.</returns>
    Task<PluginEventContext> FireEventAsync(PluginEventContext context, CancellationToken cancellationToken = default);
}
