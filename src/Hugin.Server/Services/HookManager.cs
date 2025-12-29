using System.Collections.Concurrent;
using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hugin.Server.Services;

/// <summary>
/// Implementation of the hook manager for intercepting commands and events.
/// </summary>
public sealed class HookManager : IHookManager
{
    private readonly ILogger<HookManager> _logger;

    // Use concurrent collections for thread safety
    private readonly ConcurrentDictionary<string, List<IPreCommandHook>> _preCommandHooks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<IPostCommandHook>> _postCommandHooks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<ServerEvent, List<IEventHook>> _eventHooks = new();
    private readonly List<IMessageHook> _messageHooks = new();

    private readonly object _preCommandLock = new();
    private readonly object _postCommandLock = new();
    private readonly object _eventLock = new();
    private readonly object _messageLock = new();

    /// <summary>
    /// Creates a new HookManager instance.
    /// </summary>
    /// <param name="logger">Logger for hook operations.</param>
    public HookManager(ILogger<HookManager> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void RegisterPreCommandHook(IPreCommandHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);

        lock (_preCommandLock)
        {
            var list = _preCommandHooks.GetOrAdd(hook.Command, _ => new List<IPreCommandHook>());
            list.Add(hook);
            // Sort by priority (lower = earlier)
            list.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        _logger.LogDebug("Registered pre-command hook for {Command} with priority {Priority}",
            hook.Command, hook.Priority);
    }

    /// <inheritdoc />
    public void RegisterPostCommandHook(IPostCommandHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);

        lock (_postCommandLock)
        {
            var list = _postCommandHooks.GetOrAdd(hook.Command, _ => new List<IPostCommandHook>());
            list.Add(hook);
            list.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        _logger.LogDebug("Registered post-command hook for {Command} with priority {Priority}",
            hook.Command, hook.Priority);
    }

    /// <inheritdoc />
    public void RegisterEventHook(IEventHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);

        lock (_eventLock)
        {
            var list = _eventHooks.GetOrAdd(hook.EventType, _ => new List<IEventHook>());
            list.Add(hook);
            list.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        _logger.LogDebug("Registered event hook for {EventType} with priority {Priority}",
            hook.EventType, hook.Priority);
    }

    /// <inheritdoc />
    public void RegisterMessageHook(IMessageHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);

        lock (_messageLock)
        {
            _messageHooks.Add(hook);
            _messageHooks.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        _logger.LogDebug("Registered message hook with priority {Priority}", hook.Priority);
    }

    /// <inheritdoc />
    public bool UnregisterPreCommandHook(IPreCommandHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);

        lock (_preCommandLock)
        {
            if (_preCommandHooks.TryGetValue(hook.Command, out var list))
            {
                var removed = list.Remove(hook);
                if (removed)
                {
                    _logger.LogDebug("Unregistered pre-command hook for {Command}", hook.Command);
                }
                return removed;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool UnregisterPostCommandHook(IPostCommandHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);

        lock (_postCommandLock)
        {
            if (_postCommandHooks.TryGetValue(hook.Command, out var list))
            {
                var removed = list.Remove(hook);
                if (removed)
                {
                    _logger.LogDebug("Unregistered post-command hook for {Command}", hook.Command);
                }
                return removed;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool UnregisterEventHook(IEventHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);

        lock (_eventLock)
        {
            if (_eventHooks.TryGetValue(hook.EventType, out var list))
            {
                var removed = list.Remove(hook);
                if (removed)
                {
                    _logger.LogDebug("Unregistered event hook for {EventType}", hook.EventType);
                }
                return removed;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool UnregisterMessageHook(IMessageHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);

        lock (_messageLock)
        {
            var removed = _messageHooks.Remove(hook);
            if (removed)
            {
                _logger.LogDebug("Unregistered message hook");
            }
            return removed;
        }
    }

    /// <inheritdoc />
    public async ValueTask<PreCommandContext> ExecutePreCommandHooksAsync(
        string command,
        IReadOnlyList<string> parameters,
        User user,
        CancellationToken cancellationToken = default)
    {
        var context = new PreCommandContext(command, parameters, user);

        // Get hooks for this specific command and wildcard hooks
        var hooks = GetPreCommandHooksForCommand(command);

        foreach (var hook in hooks)
        {
            if (!hook.IsEnabled)
            {
                continue;
            }

            try
            {
                var result = await hook.BeforeCommandAsync(context, cancellationToken).ConfigureAwait(false);

                switch (result)
                {
                    case HookResult.Handled:
                        _logger.LogDebug("Pre-command hook handled {Command}, skipping further processing", command);
                        context.SkipHandler = true;
                        return context;

                    case HookResult.Deny:
                        _logger.LogDebug("Pre-command hook denied {Command}", command);
                        context.SkipHandler = true;
                        return context;

                    case HookResult.Continue:
                    default:
                        // Continue to next hook
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing pre-command hook for {Command}", command);
                // Continue with other hooks
            }
        }

        return context;
    }

    /// <inheritdoc />
    public async ValueTask ExecutePostCommandHooksAsync(
        string command,
        IReadOnlyList<string> parameters,
        User user,
        bool success,
        IReadOnlyDictionary<string, object> data,
        CancellationToken cancellationToken = default)
    {
        var context = new PostCommandContext(command, parameters, user, success, data);

        // Get hooks for this specific command and wildcard hooks
        var hooks = GetPostCommandHooksForCommand(command);

        foreach (var hook in hooks)
        {
            if (!hook.IsEnabled)
            {
                continue;
            }

            try
            {
                await hook.AfterCommandAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing post-command hook for {Command}", command);
                // Continue with other hooks
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<EventContext> ExecuteEventHooksAsync(
        ServerEvent eventType,
        User? user = null,
        Channel? channel = null,
        IReadOnlyDictionary<string, object>? data = null,
        CancellationToken cancellationToken = default)
    {
        var context = new EventContext(eventType, user, channel, data);

        List<IEventHook>? hooks;
        lock (_eventLock)
        {
            if (!_eventHooks.TryGetValue(eventType, out var hookList))
            {
                return context;
            }
            // Create a copy for iteration
            hooks = hookList.ToList();
        }

        foreach (var hook in hooks)
        {
            if (!hook.IsEnabled)
            {
                continue;
            }

            try
            {
                var result = await hook.OnEventAsync(context, cancellationToken).ConfigureAwait(false);

                switch (result)
                {
                    case HookResult.Handled:
                        _logger.LogDebug("Event hook handled {EventType}, skipping further processing", eventType);
                        return context;

                    case HookResult.Deny:
                        _logger.LogDebug("Event hook denied {EventType}", eventType);
                        context.Suppress = true;
                        return context;

                    case HookResult.Continue:
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing event hook for {EventType}", eventType);
            }
        }

        return context;
    }

    /// <inheritdoc />
    public async ValueTask<string?> ExecuteMessageHooksAsync(
        string message,
        Guid targetConnectionId,
        User? targetUser = null,
        CancellationToken cancellationToken = default)
    {
        var context = new MessageContext(message, targetConnectionId, targetUser);
        var currentMessage = message;

        List<IMessageHook> hooks;
        lock (_messageLock)
        {
            hooks = _messageHooks.ToList();
        }

        foreach (var hook in hooks)
        {
            if (!hook.IsEnabled)
            {
                continue;
            }

            try
            {
                var result = await hook.BeforeSendAsync(
                    new MessageContext(currentMessage, targetConnectionId, targetUser),
                    cancellationToken).ConfigureAwait(false);

                if (result is null)
                {
                    _logger.LogDebug("Message hook suppressed message to {ConnectionId}", targetConnectionId);
                    return null;
                }

                currentMessage = result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing message hook");
            }
        }

        return currentMessage;
    }

    /// <inheritdoc />
    public HookStatistics GetStatistics()
    {
        int preCount = 0;
        int postCount = 0;
        int eventCount = 0;
        int messageCount;

        lock (_preCommandLock)
        {
            foreach (var list in _preCommandHooks.Values)
            {
                preCount += list.Count;
            }
        }

        lock (_postCommandLock)
        {
            foreach (var list in _postCommandHooks.Values)
            {
                postCount += list.Count;
            }
        }

        lock (_eventLock)
        {
            foreach (var list in _eventHooks.Values)
            {
                eventCount += list.Count;
            }
        }

        lock (_messageLock)
        {
            messageCount = _messageHooks.Count;
        }

        return new HookStatistics
        {
            PreCommandHookCount = preCount,
            PostCommandHookCount = postCount,
            EventHookCount = eventCount,
            MessageHookCount = messageCount
        };
    }

    /// <summary>
    /// Gets all pre-command hooks for a command, including wildcard hooks.
    /// </summary>
    private List<IPreCommandHook> GetPreCommandHooksForCommand(string command)
    {
        var result = new List<IPreCommandHook>();

        lock (_preCommandLock)
        {
            // Get specific hooks
            if (_preCommandHooks.TryGetValue(command, out var specific))
            {
                result.AddRange(specific);
            }

            // Get wildcard hooks
            if (_preCommandHooks.TryGetValue("*", out var wildcard))
            {
                result.AddRange(wildcard);
            }
        }

        // Sort combined list by priority
        result.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        return result;
    }

    /// <summary>
    /// Gets all post-command hooks for a command, including wildcard hooks.
    /// </summary>
    private List<IPostCommandHook> GetPostCommandHooksForCommand(string command)
    {
        var result = new List<IPostCommandHook>();

        lock (_postCommandLock)
        {
            // Get specific hooks
            if (_postCommandHooks.TryGetValue(command, out var specific))
            {
                result.AddRange(specific);
            }

            // Get wildcard hooks
            if (_postCommandHooks.TryGetValue("*", out var wildcard))
            {
                result.AddRange(wildcard);
            }
        }

        // Sort combined list by priority
        result.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        return result;
    }
}
