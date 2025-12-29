using Hugin.Core.Entities;

namespace Hugin.Core.Interfaces;

/// <summary>
/// Base interface for hooks that can intercept commands and events.
/// </summary>
public interface IHook
{
    /// <summary>
    /// Gets the hook priority (lower = earlier).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Gets whether this hook is currently enabled.
    /// </summary>
    bool IsEnabled { get; }
}

/// <summary>
/// Hook for intercepting commands before they are processed.
/// </summary>
public interface IPreCommandHook : IHook
{
    /// <summary>
    /// Gets the command name this hook applies to, or "*" for all commands.
    /// </summary>
    string Command { get; }

    /// <summary>
    /// Called before a command is processed.
    /// </summary>
    /// <param name="context">The pre-command context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hook result indicating whether to continue processing.</returns>
    ValueTask<HookResult> BeforeCommandAsync(PreCommandContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Hook for intercepting commands after they are processed.
/// </summary>
public interface IPostCommandHook : IHook
{
    /// <summary>
    /// Gets the command name this hook applies to, or "*" for all commands.
    /// </summary>
    string Command { get; }

    /// <summary>
    /// Called after a command is processed.
    /// </summary>
    /// <param name="context">The post-command context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask AfterCommandAsync(PostCommandContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Hook for intercepting server events.
/// </summary>
public interface IEventHook : IHook
{
    /// <summary>
    /// Gets the event type this hook handles.
    /// </summary>
    ServerEvent EventType { get; }

    /// <summary>
    /// Called when the event occurs.
    /// </summary>
    /// <param name="context">The event context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hook result indicating whether to continue processing.</returns>
    ValueTask<HookResult> OnEventAsync(EventContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Hook for intercepting outgoing messages.
/// </summary>
public interface IMessageHook : IHook
{
    /// <summary>
    /// Called before a message is sent to a client.
    /// </summary>
    /// <param name="context">The message context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The potentially modified message, or null to suppress.</returns>
    ValueTask<string?> BeforeSendAsync(MessageContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a hook execution.
/// </summary>
public enum HookResult
{
    /// <summary>
    /// Continue processing normally.
    /// </summary>
    Continue,

    /// <summary>
    /// Stop processing this command/event (handled by hook).
    /// </summary>
    Handled,

    /// <summary>
    /// Deny the command/event (send error to user).
    /// </summary>
    Deny
}

/// <summary>
/// Types of server events that can be hooked.
/// </summary>
public enum ServerEvent
{
    /// <summary>
    /// User connected.
    /// </summary>
    UserConnect,

    /// <summary>
    /// User registered (completed connection).
    /// </summary>
    UserRegister,

    /// <summary>
    /// User disconnected.
    /// </summary>
    UserDisconnect,

    /// <summary>
    /// User changed nickname.
    /// </summary>
    NickChange,

    /// <summary>
    /// User joined a channel.
    /// </summary>
    ChannelJoin,

    /// <summary>
    /// User left a channel.
    /// </summary>
    ChannelPart,

    /// <summary>
    /// User was kicked from a channel.
    /// </summary>
    ChannelKick,

    /// <summary>
    /// Channel topic changed.
    /// </summary>
    TopicChange,

    /// <summary>
    /// Channel mode changed.
    /// </summary>
    ChannelModeChange,

    /// <summary>
    /// User mode changed.
    /// </summary>
    UserModeChange,

    /// <summary>
    /// Message sent to channel.
    /// </summary>
    ChannelMessage,

    /// <summary>
    /// Private message sent.
    /// </summary>
    PrivateMessage,

    /// <summary>
    /// Server is starting.
    /// </summary>
    ServerStart,

    /// <summary>
    /// Server is stopping.
    /// </summary>
    ServerStop,

    /// <summary>
    /// Configuration was reloaded.
    /// </summary>
    ConfigReload
}

/// <summary>
/// Context for pre-command hooks.
/// </summary>
public sealed class PreCommandContext
{
    /// <summary>
    /// Gets the command name.
    /// </summary>
    public string Command { get; }

    /// <summary>
    /// Gets the command parameters.
    /// </summary>
    public IReadOnlyList<string> Parameters { get; }

    /// <summary>
    /// Gets the user executing the command.
    /// </summary>
    public User User { get; }

    /// <summary>
    /// Gets or sets custom data to pass to the handler.
    /// </summary>
    public Dictionary<string, object> Data { get; } = new();

    /// <summary>
    /// Gets or sets whether to skip the default handler.
    /// </summary>
    public bool SkipHandler { get; set; }

    /// <summary>
    /// Gets or sets a custom response to send instead.
    /// </summary>
    public string? CustomResponse { get; set; }

    /// <summary>
    /// Creates a new pre-command context.
    /// </summary>
    public PreCommandContext(string command, IReadOnlyList<string> parameters, User user)
    {
        Command = command;
        Parameters = parameters;
        User = user;
    }
}

/// <summary>
/// Context for post-command hooks.
/// </summary>
public sealed class PostCommandContext
{
    /// <summary>
    /// Gets the command name.
    /// </summary>
    public string Command { get; }

    /// <summary>
    /// Gets the command parameters.
    /// </summary>
    public IReadOnlyList<string> Parameters { get; }

    /// <summary>
    /// Gets the user who executed the command.
    /// </summary>
    public User User { get; }

    /// <summary>
    /// Gets whether the command executed successfully.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets data passed from pre-hooks or the handler.
    /// </summary>
    public IReadOnlyDictionary<string, object> Data { get; }

    /// <summary>
    /// Creates a new post-command context.
    /// </summary>
    public PostCommandContext(
        string command,
        IReadOnlyList<string> parameters,
        User user,
        bool success,
        IReadOnlyDictionary<string, object> data)
    {
        Command = command;
        Parameters = parameters;
        User = user;
        Success = success;
        Data = data;
    }
}

/// <summary>
/// Context for event hooks.
/// </summary>
public sealed class EventContext
{
    /// <summary>
    /// Gets the event type.
    /// </summary>
    public ServerEvent EventType { get; }

    /// <summary>
    /// Gets the user involved in the event (if applicable).
    /// </summary>
    public User? User { get; }

    /// <summary>
    /// Gets the channel involved in the event (if applicable).
    /// </summary>
    public Channel? Channel { get; }

    /// <summary>
    /// Gets additional event data.
    /// </summary>
    public IReadOnlyDictionary<string, object> Data { get; }

    /// <summary>
    /// Gets or sets whether to suppress the event.
    /// </summary>
    public bool Suppress { get; set; }

    /// <summary>
    /// Creates a new event context.
    /// </summary>
    public EventContext(
        ServerEvent eventType,
        User? user = null,
        Channel? channel = null,
        IReadOnlyDictionary<string, object>? data = null)
    {
        EventType = eventType;
        User = user;
        Channel = channel;
        Data = data ?? new Dictionary<string, object>();
    }
}

/// <summary>
/// Context for message hooks.
/// </summary>
public sealed class MessageContext
{
    /// <summary>
    /// Gets the message being sent.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the target connection ID.
    /// </summary>
    public Guid TargetConnectionId { get; }

    /// <summary>
    /// Gets the target user (if known).
    /// </summary>
    public User? TargetUser { get; }

    /// <summary>
    /// Creates a new message context.
    /// </summary>
    public MessageContext(string message, Guid targetConnectionId, User? targetUser = null)
    {
        Message = message;
        TargetConnectionId = targetConnectionId;
        TargetUser = targetUser;
    }
}
