using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the ACCEPT command for caller-ID management.
/// Allows users with +g mode to manage their accept list.
/// Syntax: ACCEPT [nick], [nick], [-nick], [*]
///   - nick: Add nick to accept list
///   - -nick: Remove nick from accept list
///   - *: List current accept list
/// </summary>
public sealed class AcceptHandler : CommandHandlerBase
{
    private readonly IAcceptListManager? _acceptManager;

    /// <summary>
    /// Creates a new ACCEPT handler.
    /// </summary>
    /// <param name="acceptManager">Optional accept list manager.</param>
    public AcceptHandler(IAcceptListManager? acceptManager = null)
    {
        _acceptManager = acceptManager;
    }

    /// <inheritdoc />
    public override string Command => "ACCEPT";

    /// <inheritdoc />
    public override int MinimumParameters => 0;

    /// <inheritdoc />
    public override bool RequiresRegistration => true;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User?.Nickname?.Value ?? "*";

        if (context.User is null)
        {
            return;
        }

        // No parameters - same as listing
        if (context.Message.Parameters.Count == 0)
        {
            await ListAcceptListAsync(context, nick, cancellationToken);
            return;
        }

        var parameter = context.Message.Parameters[0];

        // Handle comma-separated list
        var entries = parameter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry))
            {
                continue;
            }

            if (entry == "*")
            {
                // List current accept list
                await ListAcceptListAsync(context, nick, cancellationToken);
            }
            else if (entry.StartsWith('-'))
            {
                // Remove from accept list
                var toRemove = entry[1..];
                if (!Nickname.TryCreate(toRemove, out var nickname, out _))
                {
                    continue;
                }

                if (_acceptManager is not null)
                {
                    var removed = _acceptManager.RemoveFromAcceptList(context.User.ConnectionId, nickname.Value);
                    if (removed)
                    {
                        // RPL_ACCEPTREMOVED (as used by some IRCds)
                        await context.ReplyAsync(
                            IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                                $"{nickname.Value} has been removed from your accept list"),
                            cancellationToken);
                    }
                }
            }
            else
            {
                // Add to accept list
                if (!Nickname.TryCreate(entry, out var nickname, out _))
                {
                    continue;
                }

                // Check if user exists (optional - some implementations don't require this)
                var targetUser = context.Users.GetByNickname(nickname);
                if (targetUser is null)
                {
                    await context.ReplyAsync(
                        IrcNumerics.NoSuchNick(context.ServerName, nick, nickname.Value),
                        cancellationToken);
                    continue;
                }

                if (_acceptManager is not null)
                {
                    var added = _acceptManager.AddToAcceptList(context.User.ConnectionId, nickname.Value);
                    if (added)
                    {
                        // RPL_ACCEPTADD (as used by some IRCds)
                        await context.ReplyAsync(
                            IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                                $"{nickname.Value} has been added to your accept list"),
                            cancellationToken);
                    }
                    else
                    {
                        // Already on list
                        await context.ReplyAsync(
                            IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                                $"{nickname.Value} is already on your accept list"),
                            cancellationToken);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Lists the user's accept list.
    /// </summary>
    private async ValueTask ListAcceptListAsync(CommandContext context, string nick, CancellationToken cancellationToken)
    {
        if (_acceptManager is null)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "NOTICE", nick,
                    "Accept list feature is not available"),
                cancellationToken);
            return;
        }

        var acceptList = _acceptManager.GetAcceptList(context.User!.ConnectionId);

        // RPL_ACCEPTLIST (281) - list entries
        foreach (var accepted in acceptList)
        {
            await context.ReplyAsync(
                IrcMessage.CreateWithSource(context.ServerName, "281", nick, accepted),
                cancellationToken);
        }

        // RPL_ENDOFACCEPT (282) - end of list
        await context.ReplyAsync(
            IrcMessage.CreateWithSource(context.ServerName, "282", nick, "End of /ACCEPT list"),
            cancellationToken);
    }
}

/// <summary>
/// Interface for managing accept lists.
/// </summary>
public interface IAcceptListManager
{
    /// <summary>
    /// Adds a nickname to a user's accept list.
    /// </summary>
    /// <param name="userId">The user's connection ID.</param>
    /// <param name="nickname">The nickname to accept.</param>
    /// <returns>True if added, false if already on list.</returns>
    bool AddToAcceptList(Guid userId, string nickname);

    /// <summary>
    /// Removes a nickname from a user's accept list.
    /// </summary>
    /// <param name="userId">The user's connection ID.</param>
    /// <param name="nickname">The nickname to remove.</param>
    /// <returns>True if removed, false if not on list.</returns>
    bool RemoveFromAcceptList(Guid userId, string nickname);

    /// <summary>
    /// Gets the accept list for a user.
    /// </summary>
    /// <param name="userId">The user's connection ID.</param>
    /// <returns>List of accepted nicknames.</returns>
    IReadOnlyList<string> GetAcceptList(Guid userId);

    /// <summary>
    /// Checks if a sender should be allowed to message a target.
    /// </summary>
    /// <param name="targetUserId">The target user's ID.</param>
    /// <param name="targetHasCallerIdMode">Whether target has +g mode.</param>
    /// <param name="senderNickname">The sender's nickname.</param>
    /// <param name="senderIsOper">Whether sender is an operator.</param>
    /// <returns>True if allowed.</returns>
    bool IsMessageAllowed(Guid targetUserId, bool targetHasCallerIdMode, string senderNickname, bool senderIsOper);
}
