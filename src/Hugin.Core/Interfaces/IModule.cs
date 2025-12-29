namespace Hugin.Core.Interfaces;

/// <summary>
/// Interface for dynamically loadable modules.
/// Modules can add functionality to the IRC server at runtime.
/// </summary>
public interface IModule
{
    /// <summary>
    /// Gets the unique module identifier.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the human-readable module name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the module version.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets the module author.
    /// </summary>
    string? Author { get; }

    /// <summary>
    /// Gets a description of what the module does.
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// Gets the modules this module depends on.
    /// </summary>
    IReadOnlyList<string>? Dependencies { get; }

    /// <summary>
    /// Called when the module is loaded.
    /// </summary>
    /// <param name="context">The module context providing access to server services.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the module loaded successfully; otherwise false.</returns>
    ValueTask<bool> LoadAsync(IModuleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when the module is unloaded.
    /// </summary>
    /// <param name="context">The module context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask UnloadAsync(IModuleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when the module should reload its configuration.
    /// </summary>
    /// <param name="context">The module context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if reload was successful; otherwise false.</returns>
    ValueTask<bool> ReloadAsync(IModuleContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Context provided to modules for accessing server services.
/// </summary>
public interface IModuleContext
{
    /// <summary>
    /// Gets a service from the dependency injection container.
    /// </summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <returns>The service instance, or null if not found.</returns>
    T? GetService<T>() where T : class;

    /// <summary>
    /// Registers a command handler from the module.
    /// </summary>
    /// <param name="command">The command name to handle.</param>
    /// <param name="handler">The command handler object.</param>
    void RegisterCommandHandler(string command, object handler);

    /// <summary>
    /// Registers a pre-command hook.
    /// </summary>
    /// <param name="hook">The hook to register.</param>
    void RegisterHook(IPreCommandHook hook);

    /// <summary>
    /// Registers a post-command hook.
    /// </summary>
    /// <param name="hook">The hook to register.</param>
    void RegisterHook(IPostCommandHook hook);

    /// <summary>
    /// Registers an event hook.
    /// </summary>
    /// <param name="hook">The hook to register.</param>
    void RegisterHook(IEventHook hook);

    /// <summary>
    /// Registers a message hook.
    /// </summary>
    /// <param name="hook">The hook to register.</param>
    void RegisterHook(IMessageHook hook);

    /// <summary>
    /// Logs a debug message from the module.
    /// </summary>
    /// <param name="message">The message template.</param>
    /// <param name="args">Message arguments.</param>
    void LogDebug(string message, params object[] args);

    /// <summary>
    /// Logs an informational message from the module.
    /// </summary>
    /// <param name="message">The message template.</param>
    /// <param name="args">Message arguments.</param>
    void LogInformation(string message, params object[] args);

    /// <summary>
    /// Logs a warning message from the module.
    /// </summary>
    /// <param name="message">The message template.</param>
    /// <param name="args">Message arguments.</param>
    void LogWarning(string message, params object[] args);

    /// <summary>
    /// Logs an error message from the module.
    /// </summary>
    /// <param name="exception">Optional exception.</param>
    /// <param name="message">The message template.</param>
    /// <param name="args">Message arguments.</param>
    void LogError(Exception? exception, string message, params object[] args);
}

/// <summary>
/// Manages loading and unloading of modules.
/// </summary>
public interface IModuleManager
{
    /// <summary>
    /// Gets all currently loaded modules.
    /// </summary>
    IReadOnlyCollection<ModuleInfo> LoadedModules { get; }

    /// <summary>
    /// Loads a module from an assembly or by ID.
    /// </summary>
    /// <param name="moduleIdOrPath">Module ID or path to the module assembly.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the module was loaded successfully.</returns>
    Task<bool> LoadModuleAsync(string moduleIdOrPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unloads a module by ID.
    /// </summary>
    /// <param name="moduleId">The module ID to unload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the module was unloaded; otherwise false.</returns>
    Task<bool> UnloadModuleAsync(string moduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads a module by ID.
    /// </summary>
    /// <param name="moduleId">The module ID to reload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the module was reloaded; otherwise false.</returns>
    Task<bool> ReloadModuleAsync(string moduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a module is loaded.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <returns>True if the module is loaded.</returns>
    bool IsModuleLoaded(string moduleId);

    /// <summary>
    /// Gets information about a loaded module.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <returns>Module info, or null if not found.</returns>
    ModuleInfo? GetModuleInfo(string moduleId);

    /// <summary>
    /// Discovers available modules in the modules directory.
    /// </summary>
    /// <returns>Paths to discovered module assemblies.</returns>
    IEnumerable<string> DiscoverModules();

    /// <summary>
    /// Event raised when a module is loaded.
    /// </summary>
    event EventHandler<ModuleEventArgs>? ModuleLoaded;

    /// <summary>
    /// Event raised when a module is unloaded.
    /// </summary>
    event EventHandler<ModuleEventArgs>? ModuleUnloaded;

    /// <summary>
    /// Event raised when a module error occurs.
    /// </summary>
    event EventHandler<ModuleEventArgs>? ModuleError;
}

/// <summary>
/// Information about a module.
/// </summary>
public sealed record ModuleInfo
{
    /// <summary>
    /// Gets the module ID.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the module name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the module version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Gets the module author.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Gets the module description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets whether the module is currently loaded.
    /// </summary>
    public bool IsLoaded { get; init; }

    /// <summary>
    /// Gets when the module was loaded.
    /// </summary>
    public DateTimeOffset? LoadedAt { get; init; }

    /// <summary>
    /// Gets the path to the module assembly.
    /// </summary>
    public string? AssemblyPath { get; init; }
}

/// <summary>
/// Event args for module events.
/// </summary>
public sealed class ModuleEventArgs : EventArgs
{
    /// <summary>
    /// Gets the module info.
    /// </summary>
    public ModuleInfo Module { get; }

    /// <summary>
    /// Gets whether the operation was successful.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets an error message if the operation failed.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Creates new module event args.
    /// </summary>
    /// <param name="module">The module info.</param>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="error">Optional error message.</param>
    public ModuleEventArgs(ModuleInfo module, bool success, string? error = null)
    {
        Module = module;
        Success = success;
        Error = error;
    }
}
