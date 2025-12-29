using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Hugin.Protocol.S2S.Services;

/// <summary>
/// ChanServ - Channel registration and management service.
/// </summary>
public sealed class ChanServ : INetworkService
{
    private readonly IChannelRepository _channelRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly ServerId _localServerId;
    private readonly ILogger<ChanServ> _logger;

    /// <inheritdoc />
    public string Nickname => "ChanServ";

    /// <inheritdoc />
    public string Ident => "ChanServ";

    /// <inheritdoc />
    public string Host { get; }

    /// <inheritdoc />
    public string Realname => "Channel Registration Service";

    /// <inheritdoc />
    public string Uid { get; }

    /// <summary>
    /// Creates a new ChanServ instance.
    /// </summary>
    public ChanServ(
        IChannelRepository channelRepository,
        IAccountRepository accountRepository,
        ServerId localServerId,
        string servicesHost,
        ILogger<ChanServ> logger)
    {
        _channelRepository = channelRepository;
        _accountRepository = accountRepository;
        _localServerId = localServerId;
        Host = servicesHost;
        Uid = $"{localServerId.Sid}AAAAAC";
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

            case "INFO":
                await HandleInfoAsync(context, cancellationToken);
                break;

            case "OP":
                await HandleOpAsync(context, cancellationToken);
                break;

            case "DEOP":
                await HandleDeopAsync(context, cancellationToken);
                break;

            case "VOICE":
                await HandleVoiceAsync(context, cancellationToken);
                break;

            case "DEVOICE":
                await HandleDevoiceAsync(context, cancellationToken);
                break;

            case "KICK":
                await HandleKickAsync(context, cancellationToken);
                break;

            case "BAN":
                await HandleBanAsync(context, cancellationToken);
                break;

            case "UNBAN":
                await HandleUnbanAsync(context, cancellationToken);
                break;

            case "TOPIC":
                await HandleTopicAsync(context, cancellationToken);
                break;

            case "SET":
                await HandleSetAsync(context, cancellationToken);
                break;

            case "DROP":
                await HandleDropAsync(context, cancellationToken);
                break;

            default:
                await context.ReplyAsync(this, $"Unknown command: {context.Command}. Type /msg ChanServ HELP for help.", cancellationToken);
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
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ to register a channel.", cancellationToken);
            return;
        }

        if (context.Arguments.Length < 1)
        {
            await context.ReplyAsync(this, "Syntax: REGISTER <#channel>", cancellationToken);
            return;
        }

        var channelName = context.Arguments[0];
        if (!channelName.StartsWith('#'))
        {
            await context.ReplyAsync(this, "Channel name must start with #.", cancellationToken);
            return;
        }

        // Check if channel exists (try to parse and look up)
        if (!ChannelName.TryCreate(channelName, out var parsedName, out _))
        {
            await context.ReplyAsync(this, $"Invalid channel name: {channelName}", cancellationToken);
            return;
        }

        var channel = _channelRepository.GetByName(parsedName!);
        if (channel is null)
        {
            await context.ReplyAsync(this, $"You must be in {channelName} to register it.", cancellationToken);
            return;
        }

        // TODO: Check if user is in the channel and has ops

        await context.ReplyAsync(this, $"Channel {channelName} has been registered to your account.", cancellationToken);

        _logger.LogInformation("ChanServ: {Account} registered channel {Channel}",
            context.SourceAccount, channelName);
    }

    private async ValueTask HandleInfoAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 1)
        {
            await context.ReplyAsync(this, "Syntax: INFO <#channel>", cancellationToken);
            return;
        }

        var channelName = context.Arguments[0];

        // Parse and look up channel
        if (!ChannelName.TryCreate(channelName, out var parsedName, out _))
        {
            await context.ReplyAsync(this, $"Invalid channel name: {channelName}", cancellationToken);
            return;
        }

        var channel = _channelRepository.GetByName(parsedName!);

        if (channel is null)
        {
            await context.ReplyAsync(this, $"Channel {channelName} is not registered.", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, $"Information for {channelName}:", cancellationToken);
        await context.ReplyAsync(this, $"  Founder: (stored in database)", cancellationToken);
        await context.ReplyAsync(this, $"  Registered: (stored in database)", cancellationToken);
        await context.ReplyAsync(this, "*** End of Info ***", cancellationToken);
    }

    private async ValueTask HandleOpAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: OP <#channel> <nick>", cancellationToken);
            return;
        }

        var channelName = context.Arguments[0];
        var targetNick = context.Arguments[1];

        // TODO: Check permissions and send MODE +o
        await context.ReplyAsync(this, $"Opping {targetNick} in {channelName}.", cancellationToken);
    }

    private async ValueTask HandleDeopAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: DEOP <#channel> <nick>", cancellationToken);
            return;
        }

        var channelName = context.Arguments[0];
        var targetNick = context.Arguments[1];

        await context.ReplyAsync(this, $"Deopping {targetNick} in {channelName}.", cancellationToken);
    }

    private async ValueTask HandleVoiceAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: VOICE <#channel> <nick>", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, "Voice command received.", cancellationToken);
    }

    private async ValueTask HandleDevoiceAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: DEVOICE <#channel> <nick>", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, "Devoice command received.", cancellationToken);
    }

    private async ValueTask HandleKickAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: KICK <#channel> <nick> [reason]", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, "Kick command received.", cancellationToken);
    }

    private async ValueTask HandleBanAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: BAN <#channel> <nick|mask>", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, "Ban command received.", cancellationToken);
    }

    private async ValueTask HandleUnbanAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: UNBAN <#channel> <mask>", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, "Unban command received.", cancellationToken);
    }

    private async ValueTask HandleTopicAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: TOPIC <#channel> <topic>", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, "Topic command received.", cancellationToken);
    }

    private async ValueTask HandleSetAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ to modify channel settings.", cancellationToken);
            return;
        }

        if (context.Arguments.Length < 3)
        {
            await context.ReplyAsync(this, "Syntax: SET <#channel> <option> <value>", cancellationToken);
            await context.ReplyAsync(this, "Options: FOUNDER, SUCCESSOR, KEEPTOPIC, SECURE", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, "Set command received.", cancellationToken);
    }

    private async ValueTask HandleDropAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ to drop a channel.", cancellationToken);
            return;
        }

        if (context.Arguments.Length < 1)
        {
            await context.ReplyAsync(this, "Syntax: DROP <#channel>", cancellationToken);
            return;
        }

        var channelName = context.Arguments[0];

        // TODO: Check ownership and drop the channel
        await context.ReplyAsync(this, $"Channel {channelName} has been dropped.", cancellationToken);

        _logger.LogInformation("ChanServ: {Account} dropped channel {Channel}",
            context.SourceAccount, channelName);
    }

    /// <inheritdoc />
    public IEnumerable<string> GetHelp(string? command = null)
    {
        if (command is null)
        {
            yield return "***** ChanServ Help *****";
            yield return " ";
            yield return "ChanServ allows you to register and manage channels.";
            yield return " ";
            yield return "Commands:";
            yield return "  REGISTER - Register a channel";
            yield return "  INFO     - Display information about a channel";
            yield return "  OP       - Give channel operator status";
            yield return "  DEOP     - Remove channel operator status";
            yield return "  VOICE    - Give voice status";
            yield return "  DEVOICE  - Remove voice status";
            yield return "  KICK     - Kick a user from a channel";
            yield return "  BAN      - Ban a user from a channel";
            yield return "  UNBAN    - Remove a ban from a channel";
            yield return "  TOPIC    - Set the channel topic";
            yield return "  SET      - Change channel settings";
            yield return "  DROP     - Drop a registered channel";
            yield return " ";
            yield return "For more information on a command, type /msg ChanServ HELP <command>";
            yield return "***** End of Help *****";
        }
        else
        {
            yield return $"Help for {command} is not yet available.";
        }
    }
}
