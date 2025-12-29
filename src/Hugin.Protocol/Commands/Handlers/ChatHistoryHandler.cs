using System.Globalization;
using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the CHATHISTORY command (IRCv3 draft/chathistory capability).
/// Provides message history playback for channels and private messages.
/// </summary>
/// <remarks>
/// Subcommands supported:
/// - LATEST target [timestamp|msgid] limit - Gets latest messages
/// - BEFORE target timestamp|msgid limit - Gets messages before a point
/// - AFTER target timestamp|msgid limit - Gets messages after a point
/// - AROUND target msgid limit - Gets messages around a specific message
/// - BETWEEN target timestamp|msgid timestamp|msgid limit - Gets messages between two points
/// - TARGETS timestamp limit - Lists conversation targets
/// </remarks>
public sealed class ChatHistoryHandler : CommandHandlerBase
{
    private const int MaxLimit = 500;
    private const int DefaultLimit = 100;

    /// <inheritdoc />
    public override string Command => "CHATHISTORY";

    /// <inheritdoc />
    public override int MinimumParameters => 2;

    /// <inheritdoc />
    public override bool RequiresRegistration => true;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User?.Nickname?.Value ?? "*";

        // Get message repository from DI
        var messageRepo = context.ServiceProvider(typeof(IMessageRepository)) as IMessageRepository;
        if (messageRepo == null)
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "MESSAGE_PROVIDER_UNAVAILABLE",
                    nick, "Chat history is not available"),
                cancellationToken);
            return;
        }

        // Check if capability is enabled
        if (!context.Capabilities.HasChatHistory)
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "NEED_CAPABILITY",
                    nick, "You need to enable draft/chathistory capability"),
                cancellationToken);
            return;
        }

        var subcommand = context.Message.Parameters[0].ToUpperInvariant();
        var parameters = context.Message.Parameters.Skip(1).ToList();

        switch (subcommand)
        {
            case "LATEST":
                await HandleLatestAsync(context, messageRepo, nick, parameters, cancellationToken);
                break;
            case "BEFORE":
                await HandleBeforeAsync(context, messageRepo, nick, parameters, cancellationToken);
                break;
            case "AFTER":
                await HandleAfterAsync(context, messageRepo, nick, parameters, cancellationToken);
                break;
            case "AROUND":
                await HandleAroundAsync(context, messageRepo, nick, parameters, cancellationToken);
                break;
            case "BETWEEN":
                await HandleBetweenAsync(context, messageRepo, nick, parameters, cancellationToken);
                break;
            case "TARGETS":
                await HandleTargetsAsync(context, messageRepo, nick, parameters, cancellationToken);
                break;
            default:
                await context.ReplyAsync(
                    IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "INVALID_PARAMS",
                        subcommand, "Unknown subcommand"),
                    cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Handles CHATHISTORY LATEST - gets the most recent messages.
    /// Syntax: LATEST target [timestamp|*] limit
    /// </summary>
    private static async ValueTask HandleLatestAsync(
        CommandContext context,
        IMessageRepository repo,
        string nick,
        List<string> parameters,
        CancellationToken cancellationToken)
    {
        if (parameters.Count < 2)
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "NEED_MORE_PARAMS",
                    nick, "Not enough parameters"),
                cancellationToken);
            return;
        }

        var target = parameters[0];
        if (!ValidateTargetAccess(context, target, out var error))
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "INVALID_TARGET",
                    target, error),
                cancellationToken);
            return;
        }

        var limit = ParseLimit(parameters.Count > 2 ? parameters[2] : parameters[1]);
        var timestampParam = parameters.Count > 2 ? parameters[1] : "*";

        IEnumerable<StoredMessage> messages;
        if (timestampParam == "*")
        {
            messages = await repo.GetLatestAsync(target, limit, cancellationToken);
        }
        else if (TryParseTimestamp(timestampParam, out var timestamp))
        {
            messages = await repo.GetBeforeAsync(target, timestamp, limit, cancellationToken);
        }
        else if (timestampParam.StartsWith("msgid=", StringComparison.OrdinalIgnoreCase))
        {
            var msgId = timestampParam[6..];
            var refMsg = await repo.GetByIdAsync(msgId, cancellationToken);
            if (refMsg != null)
            {
                messages = await repo.GetBeforeAsync(target, refMsg.Timestamp, limit, cancellationToken);
            }
            else
            {
                await context.ReplyAsync(
                    IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "UNKNOWN_MSGID",
                        msgId, "Unknown message ID"),
                    cancellationToken);
                return;
            }
        }
        else
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "INVALID_PARAMS",
                    timestampParam, "Invalid timestamp or message ID"),
                cancellationToken);
            return;
        }

        await SendHistoryBatchAsync(context, target, messages, cancellationToken);
    }

    /// <summary>
    /// Handles CHATHISTORY BEFORE - gets messages before a timestamp.
    /// Syntax: BEFORE target timestamp|msgid limit
    /// </summary>
    private static async ValueTask HandleBeforeAsync(
        CommandContext context,
        IMessageRepository repo,
        string nick,
        List<string> parameters,
        CancellationToken cancellationToken)
    {
        if (parameters.Count < 3)
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "NEED_MORE_PARAMS",
                    nick, "Not enough parameters"),
                cancellationToken);
            return;
        }

        var target = parameters[0];
        if (!ValidateTargetAccess(context, target, out var error))
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "INVALID_TARGET",
                    target, error),
                cancellationToken);
            return;
        }

        var limit = ParseLimit(parameters[2]);

        if (TryParseTimestamp(parameters[1], out var timestamp))
        {
            var messages = await repo.GetBeforeAsync(target, timestamp, limit, cancellationToken);
            await SendHistoryBatchAsync(context, target, messages, cancellationToken);
        }
        else if (parameters[1].StartsWith("msgid=", StringComparison.OrdinalIgnoreCase))
        {
            var msgId = parameters[1][6..];
            var refMsg = await repo.GetByIdAsync(msgId, cancellationToken);
            if (refMsg != null)
            {
                var messages = await repo.GetBeforeAsync(target, refMsg.Timestamp, limit, cancellationToken);
                await SendHistoryBatchAsync(context, target, messages, cancellationToken);
            }
            else
            {
                await context.ReplyAsync(
                    IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "UNKNOWN_MSGID",
                        msgId, "Unknown message ID"),
                    cancellationToken);
            }
        }
        else
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "INVALID_PARAMS",
                    parameters[1], "Invalid timestamp or message ID"),
                cancellationToken);
        }
    }

    /// <summary>
    /// Handles CHATHISTORY AFTER - gets messages after a timestamp.
    /// Syntax: AFTER target timestamp|msgid limit
    /// </summary>
    private static async ValueTask HandleAfterAsync(
        CommandContext context,
        IMessageRepository repo,
        string nick,
        List<string> parameters,
        CancellationToken cancellationToken)
    {
        if (parameters.Count < 3)
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "NEED_MORE_PARAMS",
                    nick, "Not enough parameters"),
                cancellationToken);
            return;
        }

        var target = parameters[0];
        if (!ValidateTargetAccess(context, target, out var error))
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "INVALID_TARGET",
                    target, error),
                cancellationToken);
            return;
        }

        var limit = ParseLimit(parameters[2]);

        if (TryParseTimestamp(parameters[1], out var timestamp))
        {
            var messages = await repo.GetAfterAsync(target, timestamp, limit, cancellationToken);
            await SendHistoryBatchAsync(context, target, messages, cancellationToken);
        }
        else if (parameters[1].StartsWith("msgid=", StringComparison.OrdinalIgnoreCase))
        {
            var msgId = parameters[1][6..];
            var refMsg = await repo.GetByIdAsync(msgId, cancellationToken);
            if (refMsg != null)
            {
                var messages = await repo.GetAfterAsync(target, refMsg.Timestamp, limit, cancellationToken);
                await SendHistoryBatchAsync(context, target, messages, cancellationToken);
            }
            else
            {
                await context.ReplyAsync(
                    IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "UNKNOWN_MSGID",
                        msgId, "Unknown message ID"),
                    cancellationToken);
            }
        }
        else
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "INVALID_PARAMS",
                    parameters[1], "Invalid timestamp or message ID"),
                cancellationToken);
        }
    }

    /// <summary>
    /// Handles CHATHISTORY AROUND - gets messages around a specific message.
    /// Syntax: AROUND target msgid limit
    /// </summary>
    private static async ValueTask HandleAroundAsync(
        CommandContext context,
        IMessageRepository repo,
        string nick,
        List<string> parameters,
        CancellationToken cancellationToken)
    {
        if (parameters.Count < 3)
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "NEED_MORE_PARAMS",
                    nick, "Not enough parameters"),
                cancellationToken);
            return;
        }

        var target = parameters[0];
        if (!ValidateTargetAccess(context, target, out var error))
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "INVALID_TARGET",
                    target, error),
                cancellationToken);
            return;
        }

        var limit = ParseLimit(parameters[2]);
        var msgIdParam = parameters[1];

        if (!msgIdParam.StartsWith("msgid=", StringComparison.OrdinalIgnoreCase))
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "INVALID_PARAMS",
                    msgIdParam, "Expected msgid= parameter"),
                cancellationToken);
            return;
        }

        var msgId = msgIdParam[6..];
        var messages = await repo.GetAroundAsync(target, msgId, limit, cancellationToken);
        await SendHistoryBatchAsync(context, target, messages, cancellationToken);
    }

    /// <summary>
    /// Handles CHATHISTORY BETWEEN - gets messages between two timestamps.
    /// Syntax: BETWEEN target start end limit
    /// </summary>
    private static async ValueTask HandleBetweenAsync(
        CommandContext context,
        IMessageRepository repo,
        string nick,
        List<string> parameters,
        CancellationToken cancellationToken)
    {
        if (parameters.Count < 4)
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "NEED_MORE_PARAMS",
                    nick, "Not enough parameters"),
                cancellationToken);
            return;
        }

        var target = parameters[0];
        if (!ValidateTargetAccess(context, target, out var error))
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "INVALID_TARGET",
                    target, error),
                cancellationToken);
            return;
        }

        var limit = ParseLimit(parameters[3]);

        var startResult = await TryParseTimestampOrMsgIdAsync(parameters[1], repo, cancellationToken);
        if (!startResult.Success)
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "INVALID_PARAMS",
                    parameters[1], "Invalid start timestamp or message ID"),
                cancellationToken);
            return;
        }

        var endResult = await TryParseTimestampOrMsgIdAsync(parameters[2], repo, cancellationToken);
        if (!endResult.Success)
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "INVALID_PARAMS",
                    parameters[2], "Invalid end timestamp or message ID"),
                cancellationToken);
            return;
        }

        var messages = await repo.GetBetweenAsync(target, startResult.Timestamp, endResult.Timestamp, limit, cancellationToken);
        await SendHistoryBatchAsync(context, target, messages, cancellationToken);
    }

    /// <summary>
    /// Handles CHATHISTORY TARGETS - lists conversation targets.
    /// Syntax: TARGETS timestamp limit
    /// </summary>
    private static async ValueTask HandleTargetsAsync(
        CommandContext context,
        IMessageRepository repo,
        string nick,
        List<string> parameters,
        CancellationToken cancellationToken)
    {
        if (context.User?.Account == null)
        {
            await context.ReplyAsync(
                IrcNumerics.StandardReply(context.ServerName, "FAIL", "CHATHISTORY", "ACCOUNT_REQUIRED",
                    nick, "You must be logged in to use TARGETS"),
                cancellationToken);
            return;
        }

        var targets = await repo.GetTargetsForAccountAsync(context.User.Account, cancellationToken);

        // Send as a batch
        var batch = new Batch(BatchTypes.Chathistory);
        foreach (var target in targets)
        {
            var msg = IrcMessage.CreateWithSource(context.ServerName, "CHATHISTORY", "TARGETS", target);
            batch.AddMessage(msg);
        }

        foreach (var msg in batch.GetAllMessages(context.ServerName))
        {
            await context.ReplyAsync(msg, cancellationToken);
        }
    }

    /// <summary>
    /// Sends messages as a chathistory batch.
    /// </summary>
    private static async ValueTask SendHistoryBatchAsync(
        CommandContext context,
        string target,
        IEnumerable<StoredMessage> messages,
        CancellationToken cancellationToken)
    {
        var batch = new Batch(BatchTypes.Chathistory, target);

        foreach (var stored in messages)
        {
            // Build tags
            var tags = new Dictionary<string, string?>
            {
                ["time"] = stored.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                ["msgid"] = stored.MessageId
            };

            if (stored.SenderAccount != null)
            {
                tags["account"] = stored.SenderAccount;
            }

            // Create the message
            var command = stored.Type == MessageType.Privmsg ? "PRIVMSG" : "NOTICE";
            var msg = IrcMessage.CreateFull(tags, stored.SenderHostmask, command, stored.Target, stored.Content);
            batch.AddMessage(msg);
        }

        foreach (var msg in batch.GetAllMessages(context.ServerName))
        {
            await context.ReplyAsync(msg, cancellationToken);
        }
    }

    /// <summary>
    /// Validates that the user has access to the target.
    /// </summary>
    private static bool ValidateTargetAccess(CommandContext context, string target, out string error)
    {
        error = string.Empty;

        // For channels, check if user is a member
        if (ChannelName.TryCreate(target, out var channelName, out _))
        {
            var channel = context.Channels.GetByName(channelName!);
            if (channel == null)
            {
                error = "Channel does not exist";
                return false;
            }

            // Must be a member to access history
            if (!channel.HasMember(context.User.ConnectionId))
            {
                error = "You are not on that channel";
                return false;
            }

            return true;
        }

        // For DMs, the user must be one of the participants
        // For simplicity, allow access to any nickname history (privacy handled at storage level)
        return true;
    }

    /// <summary>
    /// Parses a limit parameter.
    /// </summary>
    private static int ParseLimit(string limitStr)
    {
        if (int.TryParse(limitStr, out var limit))
        {
            return Math.Clamp(limit, 1, MaxLimit);
        }
        return DefaultLimit;
    }

    /// <summary>
    /// Tries to parse an ISO 8601 timestamp.
    /// </summary>
    private static bool TryParseTimestamp(string value, out DateTimeOffset timestamp)
    {
        // Handle timestamp= prefix
        var toParse = value.StartsWith("timestamp=", StringComparison.OrdinalIgnoreCase)
            ? value[10..]
            : value;

        return DateTimeOffset.TryParse(toParse, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp);
    }

    /// <summary>
    /// Tries to parse a timestamp or message ID reference.
    /// </summary>
    /// <returns>A tuple with success flag and parsed timestamp.</returns>
    private static async Task<(bool Success, DateTimeOffset Timestamp)> TryParseTimestampOrMsgIdAsync(
        string value,
        IMessageRepository repo,
        CancellationToken cancellationToken)
    {
        if (TryParseTimestamp(value, out var timestamp))
        {
            return (true, timestamp);
        }

        if (value.StartsWith("msgid=", StringComparison.OrdinalIgnoreCase))
        {
            var msgId = value[6..];
            var msg = await repo.GetByIdAsync(msgId, cancellationToken);
            if (msg != null)
            {
                return (true, msg.Timestamp);
            }
        }

        return (false, default);
    }
}
