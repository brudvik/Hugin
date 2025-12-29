using Hugin.Core.Entities;

namespace Hugin.Core.Interfaces;

/// <summary>
/// Manager for registering and executing hooks.
/// </summary>
public interface IHookManager
{
    /// <summary>
    /// Registers a pre-command hook.
    /// </summary>
    /// <param name="hook">The hook to register.</param>
    void RegisterPreCommandHook(IPreCommandHook hook);

    /// <summary>
    /// Registers a post-command hook.
    /// </summary>
    /// <param name="hook">The hook to register.</param>
    void RegisterPostCommandHook(IPostCommandHook hook);

    /// <summary>
    /// Registers an event hook.
    /// </summary>
    /// <param name="hook">The hook to register.</param>
    void RegisterEventHook(IEventHook hook);

    /// <summary>
    /// Registers a message hook.
    /// </summary>
    /// <param name="hook">The hook to register.</param>
    void RegisterMessageHook(IMessageHook hook);

    /// <summary>
    /// Unregisters a pre-command hook.
    /// </summary>
    /// <param name="hook">The hook to unregister.</param>
    /// <returns>True if the hook was found and removed.</returns>
    bool UnregisterPreCommandHook(IPreCommandHook hook);

    /// <summary>
    /// Unregisters a post-command hook.
    /// </summary>
    /// <param name="hook">The hook to unregister.</param>
    /// <returns>True if the hook was found and removed.</returns>
    bool UnregisterPostCommandHook(IPostCommandHook hook);

    /// <summary>
    /// Unregisters an event hook.
    /// </summary>
    /// <param name="hook">The hook to unregister.</param>
    /// <returns>True if the hook was found and removed.</returns>
    bool UnregisterEventHook(IEventHook hook);

    /// <summary>
    /// Unregisters a message hook.
    /// </summary>
    /// <param name="hook">The hook to unregister.</param>
    /// <returns>True if the hook was found and removed.</returns>
    bool UnregisterMessageHook(IMessageHook hook);

    /// <summary>
    /// Executes all pre-command hooks for a command.
    /// </summary>
    /// <param name="command">The command name.</param>
    /// <param name="parameters">The command parameters.</param>
    /// <param name="user">The user executing the command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hook context with results.</returns>
    ValueTask<PreCommandContext> ExecutePreCommandHooksAsync(
        string command,
        IReadOnlyList<string> parameters,
        User user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes all post-command hooks for a command.
    /// </summary>
    /// <param name="command">The command name.</param>
    /// <param name="parameters">The command parameters.</param>
    /// <param name="user">The user who executed the command.</param>
    /// <param name="success">Whether the command succeeded.</param>
    /// <param name="data">Data from pre-hooks and handler.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask ExecutePostCommandHooksAsync(
        string command,
        IReadOnlyList<string> parameters,
        User user,
        bool success,
        IReadOnlyDictionary<string, object> data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes all event hooks for an event.
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <param name="user">The user involved (if any).</param>
    /// <param name="channel">The channel involved (if any).</param>
    /// <param name="data">Additional event data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The event context with results.</returns>
    ValueTask<EventContext> ExecuteEventHooksAsync(
        ServerEvent eventType,
        User? user = null,
        Channel? channel = null,
        IReadOnlyDictionary<string, object>? data = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes all message hooks for an outgoing message.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="targetConnectionId">The target connection ID.</param>
    /// <param name="targetUser">The target user (if known).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The potentially modified message, or null to suppress.</returns>
    ValueTask<string?> ExecuteMessageHooksAsync(
        string message,
        Guid targetConnectionId,
        User? targetUser = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of registered hooks by type.
    /// </summary>
    HookStatistics GetStatistics();
}

/// <summary>
/// Statistics about registered hooks.
/// </summary>
public sealed class HookStatistics
{
    /// <summary>
    /// Gets the number of registered pre-command hooks.
    /// </summary>
    public int PreCommandHookCount { get; init; }

    /// <summary>
    /// Gets the number of registered post-command hooks.
    /// </summary>
    public int PostCommandHookCount { get; init; }

    /// <summary>
    /// Gets the number of registered event hooks.
    /// </summary>
    public int EventHookCount { get; init; }

    /// <summary>
    /// Gets the number of registered message hooks.
    /// </summary>
    public int MessageHookCount { get; init; }

    /// <summary>
    /// Gets the total number of registered hooks.
    /// </summary>
    public int TotalHookCount =>
        PreCommandHookCount + PostCommandHookCount + EventHookCount + MessageHookCount;
}
