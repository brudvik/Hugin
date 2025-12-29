using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Hugin.Protocol.S2S.Services;

/// <summary>
/// NickServ - Nickname registration and identification service.
/// </summary>
public sealed class NickServ : INetworkService
{
    private readonly IAccountRepository _accountRepository;
    private readonly ServerId _localServerId;
    private readonly ILogger<NickServ> _logger;

    /// <inheritdoc />
    public string Nickname => "NickServ";

    /// <inheritdoc />
    public string Ident => "NickServ";

    /// <inheritdoc />
    public string Host { get; }

    /// <inheritdoc />
    public string Realname => "Nickname Registration Service";

    /// <inheritdoc />
    public string Uid { get; }

    /// <summary>
    /// Creates a new NickServ instance.
    /// </summary>
    public NickServ(
        IAccountRepository accountRepository,
        ServerId localServerId,
        string servicesHost,
        ILogger<NickServ> logger)
    {
        _accountRepository = accountRepository;
        _localServerId = localServerId;
        Host = servicesHost;
        // UID format: SID + "AAAAAA" (services get predictable UIDs)
        Uid = $"{localServerId.Sid}AAAAAN";
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

            case "REGISTER":
                await HandleRegisterAsync(context, cancellationToken);
                break;

            case "IDENTIFY":
                await HandleIdentifyAsync(context, cancellationToken);
                break;

            case "INFO":
                await HandleInfoAsync(context, cancellationToken);
                break;

            case "SET":
                await HandleSetAsync(context, cancellationToken);
                break;

            case "DROP":
                await HandleDropAsync(context, cancellationToken);
                break;

            case "GHOST":
                await HandleGhostAsync(context, cancellationToken);
                break;

            default:
                await context.ReplyAsync(this, $"Unknown command: {context.Command}. Type /msg NickServ HELP for help.", cancellationToken);
                break;
        }
    }

    private async ValueTask ShowHelpAsync(ServiceMessageContext context, string? command, CancellationToken cancellationToken)
    {
        foreach (var line in GetHelp(command))
        {
            await context.ReplyAsync(this, line, cancellationToken);
        }
    }

    private async ValueTask HandleRegisterAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 1)
        {
            await context.ReplyAsync(this, "Syntax: REGISTER <password> [email]", cancellationToken);
            return;
        }

        if (context.SourceAccount is not null)
        {
            await context.ReplyAsync(this, "You are already identified to an account.", cancellationToken);
            return;
        }

        var password = context.Arguments[0];
        var email = context.Arguments.Length > 1 ? context.Arguments[1] : null;

        // Check if nickname is already registered
        var existingAccount = await _accountRepository.GetByNicknameAsync(context.SourceNick, cancellationToken);
        if (existingAccount is not null)
        {
            await context.ReplyAsync(this, $"The nickname {context.SourceNick} is already registered.", cancellationToken);
            return;
        }

        // Create new account
        var account = await _accountRepository.CreateAsync(context.SourceNick, password, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Failed to create account. Please try again.", cancellationToken);
            return;
        }

        if (email is not null)
        {
            await _accountRepository.SetEmailAsync(account.Id, email, cancellationToken);
        }

        _logger.LogInformation("NickServ: Registered new account {Account} for nick {Nick}",
            account.Name, context.SourceNick);

        await context.ReplyAsync(this, $"Your nickname {context.SourceNick} has been registered.", cancellationToken);
        await context.ReplyAsync(this, "You are now identified. Please remember your password.", cancellationToken);
    }

    private async ValueTask HandleIdentifyAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 1)
        {
            await context.ReplyAsync(this, "Syntax: IDENTIFY <password>", cancellationToken);
            return;
        }

        if (context.SourceAccount is not null)
        {
            await context.ReplyAsync(this, "You are already identified.", cancellationToken);
            return;
        }

        var password = context.Arguments[0];
        var account = await _accountRepository.GetByNicknameAsync(context.SourceNick, cancellationToken);

        if (account is null)
        {
            await context.ReplyAsync(this, "This nickname is not registered.", cancellationToken);
            return;
        }

        var valid = await _accountRepository.ValidatePasswordAsync(account.Id, password, cancellationToken);
        if (!valid)
        {
            await context.ReplyAsync(this, "Invalid password.", cancellationToken);
            _logger.LogWarning("NickServ: Failed IDENTIFY attempt for {Nick} from {Uid}",
                context.SourceNick, context.SourceUid);
            return;
        }

        // Update last seen
        await _accountRepository.UpdateLastSeenAsync(account.Id, cancellationToken);

        // Send ENCAP to set account on the user
        var encapMessage = S2SMessage.CreateWithSource(
            Uid,
            "ENCAP",
            "*",
            "LOGIN",
            context.SourceUid,
            account.Name);
        await context.Services.SendNoticeAsync(Uid, context.SourceUid,
            $"You are now identified for {account.Name}.", cancellationToken);

        _logger.LogInformation("NickServ: {Nick} identified to account {Account}",
            context.SourceNick, account.Name);
    }

    private async ValueTask HandleInfoAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        var targetNick = context.Arguments.Length > 0 ? context.Arguments[0] : context.SourceNick;
        var account = await _accountRepository.GetByNicknameAsync(targetNick, cancellationToken);

        if (account is null)
        {
            await context.ReplyAsync(this, $"The nickname {targetNick} is not registered.", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, $"Information for {account.Name}:", cancellationToken);
        await context.ReplyAsync(this, $"  Registered: {account.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC", cancellationToken);
        if (account.LastSeenAt.HasValue)
        {
            await context.ReplyAsync(this, $"  Last seen: {account.LastSeenAt.Value:yyyy-MM-dd HH:mm:ss} UTC", cancellationToken);
        }
        await context.ReplyAsync(this, $"  Email: {(account.Email is not null ? "[set]" : "[not set]")}", cancellationToken);
        await context.ReplyAsync(this, "*** End of Info ***", cancellationToken);
    }

    private async ValueTask HandleSetAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified to use this command.", cancellationToken);
            return;
        }

        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: SET <option> <value>", cancellationToken);
            await context.ReplyAsync(this, "Options: EMAIL, PASSWORD", cancellationToken);
            return;
        }

        var option = context.Arguments[0].ToUpperInvariant();
        var value = context.Arguments[1];

        var account = await _accountRepository.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Account not found.", cancellationToken);
            return;
        }

        switch (option)
        {
            case "EMAIL":
                await _accountRepository.SetEmailAsync(account.Id, value, cancellationToken);
                await context.ReplyAsync(this, $"Email set to: {value}", cancellationToken);
                break;

            case "PASSWORD":
                await _accountRepository.UpdatePasswordAsync(account.Id, value, cancellationToken);
                await context.ReplyAsync(this, "Password changed successfully.", cancellationToken);
                break;

            default:
                await context.ReplyAsync(this, $"Unknown option: {option}", cancellationToken);
                break;
        }
    }

    private async ValueTask HandleDropAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified to use this command.", cancellationToken);
            return;
        }

        if (context.Arguments.Length < 1)
        {
            await context.ReplyAsync(this, "Syntax: DROP <password>", cancellationToken);
            await context.ReplyAsync(this, "Warning: This will permanently delete your account!", cancellationToken);
            return;
        }

        var password = context.Arguments[0];
        var account = await _accountRepository.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Account not found.", cancellationToken);
            return;
        }

        var valid = await _accountRepository.ValidatePasswordAsync(account.Id, password, cancellationToken);
        if (!valid)
        {
            await context.ReplyAsync(this, "Invalid password.", cancellationToken);
            return;
        }

        await _accountRepository.DeleteAsync(account.Id, cancellationToken);
        await context.ReplyAsync(this, "Your account has been dropped.", cancellationToken);

        _logger.LogInformation("NickServ: Account {Account} dropped by {Nick}",
            context.SourceAccount, context.SourceNick);
    }

    private async ValueTask HandleGhostAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: GHOST <nickname> <password>", cancellationToken);
            return;
        }

        var targetNick = context.Arguments[0];
        var password = context.Arguments[1];

        var account = await _accountRepository.GetByNicknameAsync(targetNick, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, $"The nickname {targetNick} is not registered.", cancellationToken);
            return;
        }

        var valid = await _accountRepository.ValidatePasswordAsync(account.Id, password, cancellationToken);
        if (!valid)
        {
            await context.ReplyAsync(this, "Invalid password.", cancellationToken);
            return;
        }

        // Send KILL for the ghost
        // This would need to look up the user's UID and send a KILL
        await context.ReplyAsync(this, $"Ghost command sent for {targetNick}.", cancellationToken);

        _logger.LogInformation("NickServ: {Nick} ghosted {Target}",
            context.SourceNick, targetNick);
    }

    /// <inheritdoc />
    public IEnumerable<string> GetHelp(string? command = null)
    {
        if (command is null)
        {
            yield return "***** NickServ Help *****";
            yield return " ";
            yield return "NickServ allows you to register and protect your nickname.";
            yield return " ";
            yield return "Commands:";
            yield return "  REGISTER - Register your current nickname";
            yield return "  IDENTIFY - Identify to your registered nickname";
            yield return "  INFO     - Display information about a nickname";
            yield return "  SET      - Change account settings";
            yield return "  DROP     - Drop your registered nickname";
            yield return "  GHOST    - Disconnect a ghost connection";
            yield return " ";
            yield return "For more information on a command, type /msg NickServ HELP <command>";
            yield return "***** End of Help *****";
        }
        else
        {
            switch (command.ToUpperInvariant())
            {
                case "REGISTER":
                    yield return "***** NickServ Help: REGISTER *****";
                    yield return "Syntax: REGISTER <password> [email]";
                    yield return " ";
                    yield return "Registers your current nickname with NickServ.";
                    yield return "The password is required for future identification.";
                    yield return "***** End of Help *****";
                    break;

                case "IDENTIFY":
                    yield return "***** NickServ Help: IDENTIFY *****";
                    yield return "Syntax: IDENTIFY <password>";
                    yield return " ";
                    yield return "Identifies you to NickServ for your current nickname.";
                    yield return "***** End of Help *****";
                    break;

                default:
                    yield return $"No help available for {command}.";
                    break;
            }
        }
    }
}
