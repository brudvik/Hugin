namespace Hugin.Protocol.Commands;

/// <summary>
/// Base interface for all IRC command handlers.
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// Gets the command name (e.g., "PRIVMSG", "JOIN").
    /// </summary>
    string Command { get; }

    /// <summary>
    /// Gets the minimum number of parameters required.
    /// </summary>
    int MinimumParameters { get; }

    /// <summary>
    /// Gets whether this command requires registration.
    /// </summary>
    bool RequiresRegistration { get; }

    /// <summary>
    /// Gets whether this command requires operator privileges.
    /// </summary>
    bool RequiresOperator { get; }

    /// <summary>
    /// Handles the command.
    /// </summary>
    ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default);
}
