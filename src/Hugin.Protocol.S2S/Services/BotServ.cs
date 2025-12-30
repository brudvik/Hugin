using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Hugin.Protocol.S2S.Services;

/// <summary>
/// BotServ - Bot hosting service for channels.
/// Allows channel administrators to assign bots to their channels.
/// </summary>
public sealed class BotServ : INetworkService
{
    private readonly Func<IBotRepository> _botRepositoryFactory;
    private readonly Func<IChannelBotRepository> _channelBotRepositoryFactory;
    private readonly Func<IRegisteredChannelRepository> _channelRepositoryFactory;
    private readonly Func<IAccountRepository> _accountRepositoryFactory;
    private readonly ServerId _localServerId;
    private readonly ILogger<BotServ> _logger;

    /// <inheritdoc />
    public string Nickname => "BotServ";

    /// <inheritdoc />
    public string Ident => "BotServ";

    /// <inheritdoc />
    public string Host { get; }

    /// <inheritdoc />
    public string Realname => "Bot Hosting Service";

    /// <inheritdoc />
    public string Uid { get; }

    /// <summary>
    /// Creates a new BotServ instance.
    /// </summary>
    public BotServ(
        Func<IBotRepository> botRepositoryFactory,
        Func<IChannelBotRepository> channelBotRepositoryFactory,
        Func<IRegisteredChannelRepository> channelRepositoryFactory,
        Func<IAccountRepository> accountRepositoryFactory,
        ServerId localServerId,
        string servicesHost,
        ILogger<BotServ> logger)
    {
        _botRepositoryFactory = botRepositoryFactory;
        _channelBotRepositoryFactory = channelBotRepositoryFactory;
        _channelRepositoryFactory = channelRepositoryFactory;
        _accountRepositoryFactory = accountRepositoryFactory;
        _localServerId = localServerId;
        Host = servicesHost;
        // UID format: SID + "AAAAAA" (services get predictable UIDs)
        Uid = $"{localServerId.Sid}AAAAAB";
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

            case "BOTLIST":
            case "LIST":
                await HandleBotListAsync(context, cancellationToken);
                break;

            case "ASSIGN":
                await HandleAssignAsync(context, cancellationToken);
                break;

            case "UNASSIGN":
                await HandleUnassignAsync(context, cancellationToken);
                break;

            case "SAY":
                await HandleSayAsync(context, cancellationToken);
                break;

            case "ACT":
            case "ACTION":
                await HandleActAsync(context, cancellationToken);
                break;

            case "INFO":
                await HandleInfoAsync(context, cancellationToken);
                break;

            case "SET":
                await HandleSetAsync(context, cancellationToken);
                break;

            default:
                await context.ReplyAsync(this, $"Unknown command: {context.Command}. Type /msg BotServ HELP for help.", cancellationToken);
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
                "*** BotServ Help ***",
                " ",
                "BotServ allows channel administrators to assign bots",
                "to their registered channels for various purposes.",
                " ",
                "Available commands:",
                "  BOTLIST  - List available bots",
                "  ASSIGN   - Assign a bot to your channel",
                "  UNASSIGN - Remove a bot from your channel",
                "  INFO     - View bot assignment info for a channel",
                "  SAY      - Make the bot say something",
                "  ACT      - Make the bot perform an action",
                "  SET      - Configure bot settings",
                " ",
                "For help on a specific command, type:",
                "  /msg BotServ HELP <command>"
            };
        }

        return command.ToUpperInvariant() switch
        {
            "BOTLIST" or "LIST" => new[]
            {
                "*** Help for BOTLIST ***",
                "Syntax: BOTLIST",
                " ",
                "Lists all available bots that can be assigned",
                "to your registered channel."
            },
            "ASSIGN" => new[]
            {
                "*** Help for ASSIGN ***",
                "Syntax: ASSIGN <#channel> <bot>",
                " ",
                "Assigns a bot to your registered channel.",
                "You must be the channel founder or have founder access.",
                " ",
                "Example: ASSIGN #mychannel FriendlyBot"
            },
            "UNASSIGN" => new[]
            {
                "*** Help for UNASSIGN ***",
                "Syntax: UNASSIGN <#channel> <bot>",
                " ",
                "Removes a bot from your channel.",
                "You must be the channel founder or have founder access.",
                " ",
                "Example: UNASSIGN #mychannel FriendlyBot"
            },
            "INFO" => new[]
            {
                "*** Help for INFO ***",
                "Syntax: INFO <#channel>",
                " ",
                "Shows which bots are assigned to a channel",
                "and their configuration settings."
            },
            "SAY" => new[]
            {
                "*** Help for SAY ***",
                "Syntax: SAY <#channel> <bot> <message>",
                " ",
                "Makes the bot send a message to the channel.",
                "You must be a channel operator.",
                " ",
                "Example: SAY #mychannel FriendlyBot Hello everyone!"
            },
            "ACT" or "ACTION" => new[]
            {
                "*** Help for ACT ***",
                "Syntax: ACT <#channel> <bot> <action>",
                " ",
                "Makes the bot perform a CTCP ACTION in the channel.",
                "You must be a channel operator.",
                " ",
                "Example: ACT #mychannel FriendlyBot waves at everyone"
            },
            "SET" => new[]
            {
                "*** Help for SET ***",
                "Syntax: SET <#channel> <bot> <option> <value>",
                " ",
                "Options:",
                "  GREET <on|off>     - Enable/disable greet messages",
                "  GREETMSG <message> - Set the greet message",
                " ",
                "Example: SET #mychannel FriendlyBot GREET ON",
                "         SET #mychannel FriendlyBot GREETMSG Welcome to %channel%!"
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

    private async ValueTask HandleBotListAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        var botRepo = _botRepositoryFactory();
        var bots = (await botRepo.GetAllActiveAsync(cancellationToken)).ToList();

        if (bots.Count == 0)
        {
            await context.ReplyAsync(this, "No bots are currently available.", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, $"Available bots ({bots.Count}):", cancellationToken);

        foreach (var bot in bots)
        {
            await context.ReplyAsync(this, $"  \x02{bot.Nickname}\x02 - {bot.Realname}", cancellationToken);
        }

        await context.ReplyAsync(this, "Use ASSIGN <#channel> <bot> to assign a bot to your channel.", cancellationToken);
    }

    private async ValueTask HandleAssignAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        // Must be identified
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ to assign bots.", cancellationToken);
            return;
        }

        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: ASSIGN <#channel> <bot>", cancellationToken);
            return;
        }

        var channelName = context.Arguments[0];
        var botNickname = context.Arguments[1];

        // Validate channel name
        if (!channelName.StartsWith('#'))
        {
            await context.ReplyAsync(this, "Invalid channel name. Channel names must start with #.", cancellationToken);
            return;
        }

        // Get user's account
        var accountRepo = _accountRepositoryFactory();
        var account = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Unable to find your account.", cancellationToken);
            return;
        }

        // Check if channel is registered and user is founder
        var channelRepo = _channelRepositoryFactory();
        var channel = await channelRepo.GetByNameAsync(channelName, cancellationToken);
        if (channel is null)
        {
            await context.ReplyAsync(this, $"The channel \x02{channelName}\x02 is not registered.", cancellationToken);
            return;
        }

        if (channel.FounderId != account.Id && !context.IsOperator)
        {
            await context.ReplyAsync(this, "Access denied. You must be the channel founder.", cancellationToken);
            return;
        }

        // Check if bot exists
        var botRepo = _botRepositoryFactory();
        var bot = await botRepo.GetByNicknameAsync(botNickname, cancellationToken);
        if (bot is null)
        {
            await context.ReplyAsync(this, $"Bot \x02{botNickname}\x02 not found. Use BOTLIST to see available bots.", cancellationToken);
            return;
        }

        if (!bot.IsActive)
        {
            await context.ReplyAsync(this, $"Bot \x02{botNickname}\x02 is currently inactive.", cancellationToken);
            return;
        }

        // Check if already assigned
        var channelBotRepo = _channelBotRepositoryFactory();
        var existingAssignment = await channelBotRepo.GetAssignmentAsync(bot.Id, channelName, cancellationToken);
        if (existingAssignment is not null)
        {
            await context.ReplyAsync(this, $"Bot \x02{botNickname}\x02 is already assigned to \x02{channelName}\x02.", cancellationToken);
            return;
        }

        // Assign the bot
        await channelBotRepo.AssignAsync(bot.Id, channelName, account.Id, cancellationToken);

        await context.ReplyAsync(this, $"Bot \x02{botNickname}\x02 has been assigned to \x02{channelName}\x02.", cancellationToken);
        
        // Note: Actual bot JOIN would be sent via IRC protocol here
        _logger.LogInformation("BotServ: {User} assigned bot {Bot} to {Channel}", 
            context.SourceAccount, botNickname, channelName);
    }

    private async ValueTask HandleUnassignAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        // Must be identified
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ to unassign bots.", cancellationToken);
            return;
        }

        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: UNASSIGN <#channel> <bot>", cancellationToken);
            return;
        }

        var channelName = context.Arguments[0];
        var botNickname = context.Arguments[1];

        // Get user's account
        var accountRepo = _accountRepositoryFactory();
        var account = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Unable to find your account.", cancellationToken);
            return;
        }

        // Check if channel is registered and user is founder
        var channelRepo = _channelRepositoryFactory();
        var channel = await channelRepo.GetByNameAsync(channelName, cancellationToken);
        if (channel is null)
        {
            await context.ReplyAsync(this, $"The channel \x02{channelName}\x02 is not registered.", cancellationToken);
            return;
        }

        if (channel.FounderId != account.Id && !context.IsOperator)
        {
            await context.ReplyAsync(this, "Access denied. You must be the channel founder.", cancellationToken);
            return;
        }

        // Check if bot exists
        var botRepo = _botRepositoryFactory();
        var bot = await botRepo.GetByNicknameAsync(botNickname, cancellationToken);
        if (bot is null)
        {
            await context.ReplyAsync(this, $"Bot \x02{botNickname}\x02 not found.", cancellationToken);
            return;
        }

        // Unassign the bot
        var channelBotRepo = _channelBotRepositoryFactory();
        var success = await channelBotRepo.UnassignAsync(bot.Id, channelName, cancellationToken);

        if (success)
        {
            await context.ReplyAsync(this, $"Bot \x02{botNickname}\x02 has been removed from \x02{channelName}\x02.", cancellationToken);
            
            // Note: Actual bot PART would be sent via IRC protocol here
            _logger.LogInformation("BotServ: {User} unassigned bot {Bot} from {Channel}", 
                context.SourceAccount, botNickname, channelName);
        }
        else
        {
            await context.ReplyAsync(this, $"Bot \x02{botNickname}\x02 is not assigned to \x02{channelName}\x02.", cancellationToken);
        }
    }

    private async ValueTask HandleSayAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        // Must be identified
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ to use this command.", cancellationToken);
            return;
        }

        if (context.Arguments.Length < 3)
        {
            await context.ReplyAsync(this, "Syntax: SAY <#channel> <bot> <message>", cancellationToken);
            return;
        }

        var channelName = context.Arguments[0];
        var botNickname = context.Arguments[1];
        var message = string.Join(" ", context.Arguments.Skip(2));

        // Verify bot exists and is assigned to channel
        var botRepo = _botRepositoryFactory();
        var bot = await botRepo.GetByNicknameAsync(botNickname, cancellationToken);
        if (bot is null)
        {
            await context.ReplyAsync(this, $"Bot \x02{botNickname}\x02 not found.", cancellationToken);
            return;
        }

        var channelBotRepo = _channelBotRepositoryFactory();
        var assignment = await channelBotRepo.GetAssignmentAsync(bot.Id, channelName, cancellationToken);
        if (assignment is null)
        {
            await context.ReplyAsync(this, $"Bot \x02{botNickname}\x02 is not assigned to \x02{channelName}\x02.", cancellationToken);
            return;
        }

        // Verify user has access (channel founder or operator)
        var accountRepo = _accountRepositoryFactory();
        var account = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Unable to find your account.", cancellationToken);
            return;
        }

        var channelRepo = _channelRepositoryFactory();
        var channel = await channelRepo.GetByNameAsync(channelName, cancellationToken);
        if (channel is not null && channel.FounderId != account.Id && !context.IsOperator)
        {
            await context.ReplyAsync(this, "Access denied. You must be a channel operator or founder.", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, $"Bot \x02{botNickname}\x02 will say: {message}", cancellationToken);
        
        // Note: Actual PRIVMSG would be sent via IRC protocol here
        _logger.LogInformation("BotServ: {User} made bot {Bot} say in {Channel}: {Message}", 
            context.SourceAccount, botNickname, channelName, message);
    }

    private async ValueTask HandleActAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        // Must be identified
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ to use this command.", cancellationToken);
            return;
        }

        if (context.Arguments.Length < 3)
        {
            await context.ReplyAsync(this, "Syntax: ACT <#channel> <bot> <action>", cancellationToken);
            return;
        }

        var channelName = context.Arguments[0];
        var botNickname = context.Arguments[1];
        var action = string.Join(" ", context.Arguments.Skip(2));

        // Verify bot exists and is assigned to channel
        var botRepo = _botRepositoryFactory();
        var bot = await botRepo.GetByNicknameAsync(botNickname, cancellationToken);
        if (bot is null)
        {
            await context.ReplyAsync(this, $"Bot \x02{botNickname}\x02 not found.", cancellationToken);
            return;
        }

        var channelBotRepo = _channelBotRepositoryFactory();
        var assignment = await channelBotRepo.GetAssignmentAsync(bot.Id, channelName, cancellationToken);
        if (assignment is null)
        {
            await context.ReplyAsync(this, $"Bot \x02{botNickname}\x02 is not assigned to \x02{channelName}\x02.", cancellationToken);
            return;
        }

        // Verify user has access
        var accountRepo = _accountRepositoryFactory();
        var account = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Unable to find your account.", cancellationToken);
            return;
        }

        var channelRepo = _channelRepositoryFactory();
        var channel = await channelRepo.GetByNameAsync(channelName, cancellationToken);
        if (channel is not null && channel.FounderId != account.Id && !context.IsOperator)
        {
            await context.ReplyAsync(this, "Access denied. You must be a channel operator or founder.", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, $"Bot \x02{botNickname}\x02 will perform action: {action}", cancellationToken);
        
        // Note: Actual CTCP ACTION would be sent via IRC protocol here
        _logger.LogInformation("BotServ: {User} made bot {Bot} act in {Channel}: {Action}", 
            context.SourceAccount, botNickname, channelName, action);
    }

    private async ValueTask HandleInfoAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 1)
        {
            await context.ReplyAsync(this, "Syntax: INFO <#channel>", cancellationToken);
            return;
        }

        var channelName = context.Arguments[0];

        // Get assigned bots
        var channelBotRepo = _channelBotRepositoryFactory();
        var assignments = (await channelBotRepo.GetByChannelAsync(channelName, cancellationToken)).ToList();

        if (assignments.Count == 0)
        {
            await context.ReplyAsync(this, $"No bots are assigned to \x02{channelName}\x02.", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, $"Bots assigned to \x02{channelName}\x02:", cancellationToken);

        var botRepo = _botRepositoryFactory();
        foreach (var assignment in assignments)
        {
            var bot = await botRepo.GetByIdAsync(assignment.BotId, cancellationToken);
            if (bot is not null)
            {
                var greetStatus = assignment.AutoGreet ? "ON" : "OFF";
                await context.ReplyAsync(this, $"  \x02{bot.Nickname}\x02 (Greet: {greetStatus})", cancellationToken);
                if (assignment.AutoGreet && !string.IsNullOrEmpty(assignment.GreetMessage))
                {
                    await context.ReplyAsync(this, $"    Greet message: {assignment.GreetMessage}", cancellationToken);
                }
            }
        }
    }

    private async ValueTask HandleSetAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        // Must be identified
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ to use this command.", cancellationToken);
            return;
        }

        if (context.Arguments.Length < 3)
        {
            await context.ReplyAsync(this, "Syntax: SET <#channel> <bot> <option> <value>", cancellationToken);
            return;
        }

        var channelName = context.Arguments[0];
        var botNickname = context.Arguments[1];
        var option = context.Arguments[2].ToUpperInvariant();

        // Verify user has access
        var accountRepo = _accountRepositoryFactory();
        var account = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Unable to find your account.", cancellationToken);
            return;
        }

        var channelRepo = _channelRepositoryFactory();
        var channel = await channelRepo.GetByNameAsync(channelName, cancellationToken);
        if (channel is null)
        {
            await context.ReplyAsync(this, $"The channel \x02{channelName}\x02 is not registered.", cancellationToken);
            return;
        }

        if (channel.FounderId != account.Id && !context.IsOperator)
        {
            await context.ReplyAsync(this, "Access denied. You must be the channel founder.", cancellationToken);
            return;
        }

        // Get bot and assignment
        var botRepo = _botRepositoryFactory();
        var bot = await botRepo.GetByNicknameAsync(botNickname, cancellationToken);
        if (bot is null)
        {
            await context.ReplyAsync(this, $"Bot \x02{botNickname}\x02 not found.", cancellationToken);
            return;
        }

        var channelBotRepo = _channelBotRepositoryFactory();
        var assignment = await channelBotRepo.GetAssignmentAsync(bot.Id, channelName, cancellationToken);
        if (assignment is null)
        {
            await context.ReplyAsync(this, $"Bot \x02{botNickname}\x02 is not assigned to \x02{channelName}\x02.", cancellationToken);
            return;
        }

        switch (option)
        {
            case "GREET":
                if (context.Arguments.Length < 4)
                {
                    await context.ReplyAsync(this, "Syntax: SET <#channel> <bot> GREET <ON|OFF>", cancellationToken);
                    return;
                }
                var greetValue = context.Arguments[3].ToUpperInvariant();
                if (greetValue != "ON" && greetValue != "OFF")
                {
                    await context.ReplyAsync(this, "Invalid value. Use ON or OFF.", cancellationToken);
                    return;
                }
                assignment.AutoGreet = greetValue == "ON";
                await channelBotRepo.UpdateAsync(assignment, cancellationToken);
                await context.ReplyAsync(this, $"Greet for \x02{botNickname}\x02 in \x02{channelName}\x02 is now {greetValue}.", cancellationToken);
                break;

            case "GREETMSG":
                if (context.Arguments.Length < 4)
                {
                    await context.ReplyAsync(this, "Syntax: SET <#channel> <bot> GREETMSG <message>", cancellationToken);
                    return;
                }
                var greetMsg = string.Join(" ", context.Arguments.Skip(3));
                assignment.GreetMessage = greetMsg;
                await channelBotRepo.UpdateAsync(assignment, cancellationToken);
                await context.ReplyAsync(this, $"Greet message updated for \x02{botNickname}\x02 in \x02{channelName}\x02.", cancellationToken);
                break;

            default:
                await context.ReplyAsync(this, $"Unknown option: {option}. Valid options: GREET, GREETMSG", cancellationToken);
                break;
        }
    }
}
