using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Hugin.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hugin.Server.Services;

/// <summary>
/// Implementation of the module manager for loading and unloading dynamic modules.
/// </summary>
public sealed class ModuleManager : IModuleManager, IDisposable
{
    private readonly ILogger<ModuleManager> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHookManager _hookManager;
    private readonly ConcurrentDictionary<string, LoadedModule> _loadedModules = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _modulesPath;
    private bool _disposed;

    /// <inheritdoc />
    public IReadOnlyCollection<ModuleInfo> LoadedModules =>
        _loadedModules.Values.Select(m => m.Info).ToList().AsReadOnly();

    /// <inheritdoc />
    public event EventHandler<ModuleEventArgs>? ModuleLoaded;

    /// <inheritdoc />
    public event EventHandler<ModuleEventArgs>? ModuleUnloaded;

    /// <inheritdoc />
    public event EventHandler<ModuleEventArgs>? ModuleError;

    /// <summary>
    /// Creates a new ModuleManager instance.
    /// </summary>
    /// <param name="logger">Logger for module operations.</param>
    /// <param name="serviceProvider">Service provider for dependency injection.</param>
    /// <param name="hookManager">Hook manager for module hooks.</param>
    /// <param name="modulesPath">Path to the modules directory.</param>
    public ModuleManager(
        ILogger<ModuleManager> logger,
        IServiceProvider serviceProvider,
        IHookManager hookManager,
        string? modulesPath = null)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _hookManager = hookManager;
        _modulesPath = modulesPath ?? Path.Combine(AppContext.BaseDirectory, "modules");

        // Ensure modules directory exists
        if (!Directory.Exists(_modulesPath))
        {
            Directory.CreateDirectory(_modulesPath);
            _logger.LogInformation("Created modules directory: {Path}", _modulesPath);
        }
    }

    /// <inheritdoc />
    public async Task<bool> LoadModuleAsync(string moduleIdOrPath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string modulePath;

        // Determine if it's a path or module ID
        if (Path.IsPathRooted(moduleIdOrPath) || moduleIdOrPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            modulePath = moduleIdOrPath;
        }
        else
        {
            // Look for module in modules directory
            modulePath = Path.Combine(_modulesPath, moduleIdOrPath, $"{moduleIdOrPath}.dll");
            if (!File.Exists(modulePath))
            {
                modulePath = Path.Combine(_modulesPath, $"{moduleIdOrPath}.dll");
            }
        }

        if (!File.Exists(modulePath))
        {
            _logger.LogError("Module not found: {Path}", modulePath);
            RaiseModuleError(moduleIdOrPath, $"Module file not found: {modulePath}");
            return false;
        }

        try
        {
            _logger.LogInformation("Loading module from: {Path}", modulePath);

            // Create isolated load context for the module
            var loadContext = new ModuleLoadContext(modulePath);
            var assembly = loadContext.LoadFromAssemblyPath(modulePath);

            // Find module types
            var moduleTypes = assembly.GetTypes()
                .Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                .ToList();

            if (moduleTypes.Count == 0)
            {
                _logger.LogError("No IModule implementations found in {Path}", modulePath);
                loadContext.Unload();
                RaiseModuleError(moduleIdOrPath, "No IModule implementations found");
                return false;
            }

            // Create module context
            var moduleContext = new ModuleContext(_serviceProvider, _hookManager, _logger);

            foreach (var moduleType in moduleTypes)
            {
                var module = (IModule?)Activator.CreateInstance(moduleType);
                if (module is null)
                {
                    _logger.LogWarning("Failed to create instance of {Type}", moduleType.FullName);
                    continue;
                }

                // Check if already loaded
                if (_loadedModules.ContainsKey(module.Id))
                {
                    _logger.LogWarning("Module {Id} is already loaded", module.Id);
                    continue;
                }

                // Check dependencies
                if (!await CheckDependenciesAsync(module, cancellationToken).ConfigureAwait(false))
                {
                    _logger.LogError("Module {Id} has unmet dependencies", module.Id);
                    RaiseModuleError(module.Id, "Unmet dependencies");
                    continue;
                }

                // Load the module
                await module.LoadAsync(moduleContext, cancellationToken).ConfigureAwait(false);

                var info = new ModuleInfo
                {
                    Id = module.Id,
                    Name = module.Name,
                    Version = module.Version,
                    Author = module.Author,
                    Description = module.Description,
                    IsLoaded = true,
                    LoadedAt = DateTimeOffset.UtcNow
                };

                var loadedModule = new LoadedModule(module, loadContext, info, modulePath);
                _loadedModules[module.Id] = loadedModule;

                _logger.LogInformation("Loaded module: {Name} v{Version} by {Author}",
                    module.Name, module.Version, module.Author);

                RaiseModuleLoaded(info);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load module from {Path}", modulePath);
            RaiseModuleError(moduleIdOrPath, ex.Message);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UnloadModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_loadedModules.TryRemove(moduleId, out var loadedModule))
        {
            _logger.LogWarning("Module {Id} is not loaded", moduleId);
            return false;
        }

        try
        {
            _logger.LogInformation("Unloading module: {Name}", loadedModule.Module.Name);

            // Create context for unload
            var moduleContext = new ModuleContext(_serviceProvider, _hookManager, _logger);

            await loadedModule.Module.UnloadAsync(moduleContext, cancellationToken).ConfigureAwait(false);

            // Unload the assembly context
            loadedModule.LoadContext.Unload();

            var info = loadedModule.Info with { IsLoaded = false };
            RaiseModuleUnloaded(info);

            _logger.LogInformation("Unloaded module: {Name}", loadedModule.Module.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unloading module {Id}", moduleId);
            RaiseModuleError(moduleId, ex.Message);

            // Put it back if unload failed
            _loadedModules[moduleId] = loadedModule;
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ReloadModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_loadedModules.TryGetValue(moduleId, out var loadedModule))
        {
            _logger.LogWarning("Module {Id} is not loaded, cannot reload", moduleId);
            return false;
        }

        var modulePath = loadedModule.Path;

        // Unload first
        if (!await UnloadModuleAsync(moduleId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        // Give the GC a chance to collect the old assembly
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Reload from the same path
        return await LoadModuleAsync(modulePath, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool IsModuleLoaded(string moduleId)
    {
        return _loadedModules.ContainsKey(moduleId);
    }

    /// <inheritdoc />
    public ModuleInfo? GetModuleInfo(string moduleId)
    {
        return _loadedModules.TryGetValue(moduleId, out var loaded) ? loaded.Info : null;
    }

    /// <inheritdoc />
    public IEnumerable<string> DiscoverModules()
    {
        if (!Directory.Exists(_modulesPath))
        {
            yield break;
        }

        // Find all DLLs in the modules directory
        foreach (var dll in Directory.EnumerateFiles(_modulesPath, "*.dll", SearchOption.AllDirectories))
        {
            yield return dll;
        }
    }

    /// <summary>
    /// Checks if all dependencies for a module are satisfied.
    /// </summary>
    private async Task<bool> CheckDependenciesAsync(IModule module, CancellationToken cancellationToken)
    {
        if (module.Dependencies is null || module.Dependencies.Count == 0)
        {
            return true;
        }

        foreach (var dependency in module.Dependencies)
        {
            if (!_loadedModules.ContainsKey(dependency))
            {
                _logger.LogWarning("Module {Id} requires {Dependency} which is not loaded",
                    module.Id, dependency);

                // Try to load the dependency
                if (!await LoadModuleAsync(dependency, cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void RaiseModuleLoaded(ModuleInfo info)
    {
        try
        {
            ModuleLoaded?.Invoke(this, new ModuleEventArgs(info, true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ModuleLoaded event handler");
        }
    }

    private void RaiseModuleUnloaded(ModuleInfo info)
    {
        try
        {
            ModuleUnloaded?.Invoke(this, new ModuleEventArgs(info, true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ModuleUnloaded event handler");
        }
    }

    private void RaiseModuleError(string moduleId, string error)
    {
        try
        {
            var info = new ModuleInfo
            {
                Id = moduleId,
                Name = moduleId,
                Version = "0.0.0",
                IsLoaded = false
            };
            ModuleError?.Invoke(this, new ModuleEventArgs(info, false, error));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ModuleError event handler");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Unload all modules synchronously on dispose
        foreach (var module in _loadedModules.Values)
        {
            try
            {
                var context = new ModuleContext(_serviceProvider, _hookManager, _logger);
                module.Module.UnloadAsync(context, CancellationToken.None).AsTask().Wait();
                module.LoadContext.Unload();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unloading module {Id} during dispose", module.Module.Id);
            }
        }

        _loadedModules.Clear();
    }

    /// <summary>
    /// Represents a loaded module with its context.
    /// </summary>
    private sealed record LoadedModule(
        IModule Module,
        ModuleLoadContext LoadContext,
        ModuleInfo Info,
        string Path);
}

/// <summary>
/// Assembly load context for module isolation.
/// </summary>
internal sealed class ModuleLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    /// <summary>
    /// Creates a new module load context.
    /// </summary>
    /// <param name="pluginPath">Path to the module assembly.</param>
    public ModuleLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // First try to resolve from module's dependencies
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath is not null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        // Fall back to default context for shared assemblies
        return null;
    }

    /// <inheritdoc />
    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath is not null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return nint.Zero;
    }
}

/// <summary>
/// Module context implementation providing services to modules.
/// </summary>
internal sealed class ModuleContext : IModuleContext
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHookManager _hookManager;
    private readonly ILogger _logger;
    private readonly List<IPreCommandHook> _registeredPreHooks = new();
    private readonly List<IPostCommandHook> _registeredPostHooks = new();
    private readonly List<IEventHook> _registeredEventHooks = new();
    private readonly List<IMessageHook> _registeredMessageHooks = new();
    private readonly List<(string Command, object Handler)> _registeredCommands = new();

    /// <summary>
    /// Creates a new module context.
    /// </summary>
    public ModuleContext(
        IServiceProvider serviceProvider,
        IHookManager hookManager,
        ILogger logger)
    {
        _serviceProvider = serviceProvider;
        _hookManager = hookManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public T? GetService<T>() where T : class
    {
        return _serviceProvider.GetService(typeof(T)) as T;
    }

    /// <inheritdoc />
    public void RegisterCommandHandler(string command, object handler)
    {
        _registeredCommands.Add((command, handler));
        _logger.LogDebug("Module registered command handler for {Command}", command);
        // Note: Actual command registration would integrate with the command dispatcher
    }

    /// <inheritdoc />
    public void RegisterHook(IPreCommandHook hook)
    {
        _hookManager.RegisterPreCommandHook(hook);
        _registeredPreHooks.Add(hook);
    }

    /// <inheritdoc />
    public void RegisterHook(IPostCommandHook hook)
    {
        _hookManager.RegisterPostCommandHook(hook);
        _registeredPostHooks.Add(hook);
    }

    /// <inheritdoc />
    public void RegisterHook(IEventHook hook)
    {
        _hookManager.RegisterEventHook(hook);
        _registeredEventHooks.Add(hook);
    }

    /// <inheritdoc />
    public void RegisterHook(IMessageHook hook)
    {
        _hookManager.RegisterMessageHook(hook);
        _registeredMessageHooks.Add(hook);
    }

    /// <inheritdoc />
#pragma warning disable CA2254 // Template should be static - module provides dynamic templates
    public void LogDebug(string message, params object[] args)
    {
        _logger.LogDebug(message, args);
    }

    /// <inheritdoc />
    public void LogInformation(string message, params object[] args)
    {
        _logger.LogInformation(message, args);
    }

    /// <inheritdoc />
    public void LogWarning(string message, params object[] args)
    {
        _logger.LogWarning(message, args);
    }

    /// <inheritdoc />
    public void LogError(Exception? exception, string message, params object[] args)
    {
        _logger.LogError(exception, message, args);
    }
#pragma warning restore CA2254

    /// <summary>
    /// Unregisters all hooks registered by this context.
    /// </summary>
    public void UnregisterAll()
    {
        foreach (var hook in _registeredPreHooks)
        {
            _hookManager.UnregisterPreCommandHook(hook);
        }
        _registeredPreHooks.Clear();

        foreach (var hook in _registeredPostHooks)
        {
            _hookManager.UnregisterPostCommandHook(hook);
        }
        _registeredPostHooks.Clear();

        foreach (var hook in _registeredEventHooks)
        {
            _hookManager.UnregisterEventHook(hook);
        }
        _registeredEventHooks.Clear();

        foreach (var hook in _registeredMessageHooks)
        {
            _hookManager.UnregisterMessageHook(hook);
        }
        _registeredMessageHooks.Clear();

        _registeredCommands.Clear();
    }
}
