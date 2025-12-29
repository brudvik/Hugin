namespace Hugin.Core.Interfaces;

/// <summary>
/// Interface for components that need to respond to configuration reloads.
/// </summary>
public interface IReloadable
{
    /// <summary>
    /// Called when configuration should be reloaded.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if reload was successful; false otherwise.</returns>
    ValueTask<bool> ReloadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for managing configuration reloads across the server.
/// </summary>
public interface IConfigurationReloader
{
    /// <summary>
    /// Registers a component that can be reloaded.
    /// </summary>
    /// <param name="name">A descriptive name for the component.</param>
    /// <param name="reloadable">The reloadable component.</param>
    void Register(string name, IReloadable reloadable);

    /// <summary>
    /// Unregisters a component.
    /// </summary>
    /// <param name="name">The component name.</param>
    void Unregister(string name);

    /// <summary>
    /// Reloads all registered components.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A report of which components succeeded and which failed.</returns>
    ValueTask<ReloadReport> ReloadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads a specific component.
    /// </summary>
    /// <param name="name">The component name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful; false otherwise.</returns>
    ValueTask<bool> ReloadComponentAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the names of all registered components.
    /// </summary>
    IEnumerable<string> RegisteredComponents { get; }
}

/// <summary>
/// Report of a configuration reload operation.
/// </summary>
public sealed class ReloadReport
{
    /// <summary>
    /// Components that reloaded successfully.
    /// </summary>
    public IReadOnlyList<string> Succeeded { get; }

    /// <summary>
    /// Components that failed to reload.
    /// </summary>
    public IReadOnlyList<string> Failed { get; }

    /// <summary>
    /// Overall success status.
    /// </summary>
    public bool AllSucceeded => Failed.Count == 0;

    /// <summary>
    /// Creates a new reload report.
    /// </summary>
    public ReloadReport(IReadOnlyList<string> succeeded, IReadOnlyList<string> failed)
    {
        Succeeded = succeeded;
        Failed = failed;
    }
}
