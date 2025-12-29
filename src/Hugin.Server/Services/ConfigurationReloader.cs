using System.Collections.Concurrent;
using Hugin.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hugin.Server.Services;

/// <summary>
/// Implementation of configuration reloader that coordinates REHASH across components.
/// </summary>
public sealed class ConfigurationReloader : IConfigurationReloader
{
    private readonly ConcurrentDictionary<string, IReloadable> _components = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ConfigurationReloader> _logger;

    /// <summary>
    /// Creates a new configuration reloader.
    /// </summary>
    public ConfigurationReloader(ILogger<ConfigurationReloader> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IEnumerable<string> RegisteredComponents => _components.Keys;

    /// <inheritdoc />
    public void Register(string name, IReloadable reloadable)
    {
        if (_components.TryAdd(name, reloadable))
        {
            _logger.LogDebug("Registered reloadable component: {ComponentName}", name);
        }
        else
        {
            _logger.LogWarning("Component {ComponentName} is already registered", name);
        }
    }

    /// <inheritdoc />
    public void Unregister(string name)
    {
        if (_components.TryRemove(name, out _))
        {
            _logger.LogDebug("Unregistered reloadable component: {ComponentName}", name);
        }
    }

    /// <inheritdoc />
    public async ValueTask<ReloadReport> ReloadAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting configuration reload for all components");

        var succeeded = new List<string>();
        var failed = new List<string>();

        foreach (var (name, component) in _components)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug("Reloading component: {ComponentName}", name);
                var success = await component.ReloadAsync(cancellationToken);

                if (success)
                {
                    succeeded.Add(name);
                    _logger.LogDebug("Successfully reloaded: {ComponentName}", name);
                }
                else
                {
                    failed.Add(name);
                    _logger.LogWarning("Failed to reload: {ComponentName}", name);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed.Add(name);
                _logger.LogError(ex, "Error reloading component {ComponentName}", name);
            }
        }

        var report = new ReloadReport(succeeded, failed);

        if (report.AllSucceeded)
        {
            _logger.LogInformation("Configuration reload complete. All {Count} components reloaded successfully",
                succeeded.Count);
        }
        else
        {
            _logger.LogWarning("Configuration reload complete. {Succeeded} succeeded, {Failed} failed",
                succeeded.Count, failed.Count);
        }

        return report;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ReloadComponentAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_components.TryGetValue(name, out var component))
        {
            _logger.LogWarning("Component {ComponentName} not found for reload", name);
            return false;
        }

        try
        {
            _logger.LogDebug("Reloading component: {ComponentName}", name);
            var success = await component.ReloadAsync(cancellationToken);

            if (success)
            {
                _logger.LogInformation("Successfully reloaded: {ComponentName}", name);
            }
            else
            {
                _logger.LogWarning("Failed to reload: {ComponentName}", name);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reloading component {ComponentName}", name);
            return false;
        }
    }
}
