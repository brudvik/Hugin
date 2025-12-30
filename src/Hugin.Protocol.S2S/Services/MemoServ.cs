using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hugin.Protocol.S2S.Services;

/// <summary>
/// MemoServ - Offline messaging service between registered users.
/// </summary>
public sealed class MemoServ : INetworkService
{
    private readonly Func<IMemoRepository> _memoRepositoryFactory;
    private readonly Func<IAccountRepository> _accountRepositoryFactory;
    private readonly ServerId _localServerId;
    private readonly ILogger<MemoServ> _logger;

    /// <inheritdoc />
    public string Nickname => "MemoServ";

    /// <inheritdoc />
    public string Ident => "MemoServ";

    /// <inheritdoc />
    public string Host { get; }

    /// <inheritdoc />
    public string Realname => "Memo/Offline Message Service";

    /// <inheritdoc />
    public string Uid { get; }

    /// <summary>
    /// Creates a new MemoServ instance.
    /// </summary>
    public MemoServ(
        Func<IMemoRepository> memoRepositoryFactory,
        Func<IAccountRepository> accountRepositoryFactory,
        ServerId localServerId,
        string servicesHost,
        ILogger<MemoServ> logger)
    {
        _memoRepositoryFactory = memoRepositoryFactory;
        _accountRepositoryFactory = accountRepositoryFactory;
        _localServerId = localServerId;
        Host = servicesHost;
        // UID format: SID + "AAAAAA" (services get predictable UIDs)
        Uid = $"{localServerId.Sid}AAAAAM";
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask HandleMessageAsync(ServiceMessageContext context, CancellationToken cancellationToken = default)
    {
        switch (context.Command)
        {
            case "HELP":
                await ShowHelpAsync(context, context.Arguments.Length > 0 ? context.Arguments[0] : null, cancellationToken);
                break;

            case "SEND":
                await HandleSendAsync(context, cancellationToken);
                break;

            case "LIST":
                await HandleListAsync(context, cancellationToken);
                break;

            case "READ":
                await HandleReadAsync(context, cancellationToken);
                break;

            case "DEL":
            case "DELETE":
                await HandleDeleteAsync(context, cancellationToken);
                break;

            case "CLEAR":
                await HandleClearAsync(context, cancellationToken);
                break;

            default:
                await context.ReplyAsync(this, $"Unknown command: {context.Command}. Type /msg MemoServ HELP for help.", cancellationToken);
                break;
        }
    }

    /// <inheritdoc />
    public IEnumerable<string> GetHelp(string? command = null)
    {
        if (string.IsNullOrEmpty(command))
        {
            return new[]
            {
                "*** MemoServ Help ***",
                " ",
                "MemoServ allows registered users to send messages",
                "to other registered users who are currently offline.",
                " ",
                "Available commands:",
                "  SEND    - Send a memo to a user",
                "  LIST    - List your memos",
                "  READ    - Read a specific memo",
                "  DEL     - Delete a memo",
                "  CLEAR   - Delete all your memos",
                " ",
                "For help on a specific command, type:",
                "  /msg MemoServ HELP <command>"
            };
        }

        return command.ToUpperInvariant() switch
        {
            "SEND" => new[]
            {
                "*** Help for SEND ***",
                "Syntax: SEND <nickname> <message>",
                " ",
                "Sends a memo to a registered user.",
                "The user will be notified when they identify.",
                " ",
                "Example: SEND JohnDoe Please contact me when you're online."
            },
            "LIST" => new[]
            {
                "*** Help for LIST ***",
                "Syntax: LIST",
                " ",
                "Lists all memos in your inbox.",
                "New memos are marked with [NEW]."
            },
            "READ" => new[]
            {
                "*** Help for READ ***",
                "Syntax: READ <number>",
                " ",
                "Reads the memo with the specified number.",
                "Use LIST to see memo numbers."
            },
            "DEL" or "DELETE" => new[]
            {
                "*** Help for DEL ***",
                "Syntax: DEL <number>",
                " ",
                "Deletes the memo with the specified number.",
                "Use LIST to see memo numbers."
            },
            "CLEAR" => new[]
            {
                "*** Help for CLEAR ***",
                "Syntax: CLEAR",
                " ",
                "Deletes all memos in your inbox.",
                "This cannot be undone!"
            },
            _ => new[] { $"No help available for {command}." }
        };
    }

    private async ValueTask ShowHelpAsync(ServiceMessageContext context, string? command, CancellationToken cancellationToken)
    {
        foreach (var line in GetHelp(command))
        {
            await context.ReplyAsync(this, line, cancellationToken);
        }
    }

    private async ValueTask HandleSendAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        // Must be identified
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ to send memos.", cancellationToken);
            return;
        }

        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: SEND <nickname> <message>", cancellationToken);
            return;
        }

        var targetNick = context.Arguments[0];
        var message = string.Join(" ", context.Arguments.Skip(1));

        if (message.Length > 1000)
        {
            await context.ReplyAsync(this, "Memo is too long. Maximum length is 1000 characters.", cancellationToken);
            return;
        }

        // Find sender's account
        var accountRepo = _accountRepositoryFactory();
        var senderAccount = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (senderAccount is null)
        {
            await context.ReplyAsync(this, "Unable to find your account. Please re-identify with NickServ.", cancellationToken);
            return;
        }

        // Find recipient's account (by registered nickname)
        var recipientAccount = await accountRepo.GetByNicknameAsync(targetNick, cancellationToken);
        if (recipientAccount is null)
        {
            await context.ReplyAsync(this, $"The nickname \x02{targetNick}\x02 is not registered.", cancellationToken);
            return;
        }

        // Can't send to yourself
        if (recipientAccount.Id == senderAccount.Id)
        {
            await context.ReplyAsync(this, "You cannot send a memo to yourself.", cancellationToken);
            return;
        }

        // Send the memo
        var memoRepo = _memoRepositoryFactory();
        await memoRepo.CreateAsync(
            senderAccount.Id,
            context.SourceNick ?? "Unknown",
            recipientAccount.Id,
            message,
            cancellationToken);

        await context.ReplyAsync(this, $"Memo sent to \x02{targetNick}\x02.", cancellationToken);
        _logger.LogInformation("Memo sent from {Sender} to {Recipient}", context.SourceAccount, targetNick);
    }

    private async ValueTask HandleListAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ to list memos.", cancellationToken);
            return;
        }

        var accountRepo = _accountRepositoryFactory();
        var account = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Unable to find your account.", cancellationToken);
            return;
        }

        var memoRepo = _memoRepositoryFactory();
        var memos = (await memoRepo.GetByRecipientAsync(account.Id, cancellationToken)).ToList();

        if (memos.Count == 0)
        {
            await context.ReplyAsync(this, "You have no memos.", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, $"You have {memos.Count} memo(s):", cancellationToken);

        for (int i = 0; i < memos.Count; i++)
        {
            var memo = memos[i];
            var status = memo.IsRead ? "    " : "[NEW]";
            var preview = memo.Text.Length > 30 ? memo.Text[..30] + "..." : memo.Text;
            var timeAgo = FormatTimeAgo(memo.SentAt);
            
            await context.ReplyAsync(this, $" {i + 1}. {status} From \x02{memo.SenderNickname}\x02 ({timeAgo}): {preview}", cancellationToken);
        }

        await context.ReplyAsync(this, "Use READ <number> to read a memo.", cancellationToken);
    }

    private async ValueTask HandleReadAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ to read memos.", cancellationToken);
            return;
        }

        if (context.Arguments.Length < 1 || !int.TryParse(context.Arguments[0], out var memoNumber) || memoNumber < 1)
        {
            await context.ReplyAsync(this, "Syntax: READ <number>", cancellationToken);
            return;
        }

        var accountRepo = _accountRepositoryFactory();
        var account = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Unable to find your account.", cancellationToken);
            return;
        }

        var memoRepo = _memoRepositoryFactory();
        var memos = (await memoRepo.GetByRecipientAsync(account.Id, cancellationToken)).ToList();

        if (memoNumber > memos.Count)
        {
            await context.ReplyAsync(this, $"Invalid memo number. You have {memos.Count} memo(s).", cancellationToken);
            return;
        }

        var memo = memos[memoNumber - 1];

        await context.ReplyAsync(this, $"Memo {memoNumber} from \x02{memo.SenderNickname}\x02:", cancellationToken);
        await context.ReplyAsync(this, $"Sent: {memo.SentAt:yyyy-MM-dd HH:mm:ss} UTC", cancellationToken);
        await context.ReplyAsync(this, $"Message: {memo.Text}", cancellationToken);

        // Mark as read
        if (!memo.IsRead)
        {
            await memoRepo.MarkAsReadAsync(memo.Id, cancellationToken);
        }
    }

    private async ValueTask HandleDeleteAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ to delete memos.", cancellationToken);
            return;
        }

        if (context.Arguments.Length < 1 || !int.TryParse(context.Arguments[0], out var memoNumber) || memoNumber < 1)
        {
            await context.ReplyAsync(this, "Syntax: DEL <number>", cancellationToken);
            return;
        }

        var accountRepo = _accountRepositoryFactory();
        var account = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Unable to find your account.", cancellationToken);
            return;
        }

        var memoRepo = _memoRepositoryFactory();
        var memos = (await memoRepo.GetByRecipientAsync(account.Id, cancellationToken)).ToList();

        if (memoNumber > memos.Count)
        {
            await context.ReplyAsync(this, $"Invalid memo number. You have {memos.Count} memo(s).", cancellationToken);
            return;
        }

        var memo = memos[memoNumber - 1];
        await memoRepo.DeleteAsync(memo.Id, cancellationToken);

        await context.ReplyAsync(this, $"Memo {memoNumber} from \x02{memo.SenderNickname}\x02 has been deleted.", cancellationToken);
    }

    private async ValueTask HandleClearAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ to clear memos.", cancellationToken);
            return;
        }

        var accountRepo = _accountRepositoryFactory();
        var account = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Unable to find your account.", cancellationToken);
            return;
        }

        var memoRepo = _memoRepositoryFactory();
        var count = await memoRepo.DeleteAllByRecipientAsync(account.Id, cancellationToken);

        if (count == 0)
        {
            await context.ReplyAsync(this, "You have no memos to delete.", cancellationToken);
        }
        else
        {
            await context.ReplyAsync(this, $"Deleted {count} memo(s).", cancellationToken);
        }
    }

    private static string FormatTimeAgo(DateTimeOffset time)
    {
        var elapsed = DateTimeOffset.UtcNow - time;

        if (elapsed.TotalMinutes < 1)
            return "just now";
        if (elapsed.TotalMinutes < 60)
            return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24)
            return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 7)
            return $"{(int)elapsed.TotalDays}d ago";
        
        return time.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }
}
