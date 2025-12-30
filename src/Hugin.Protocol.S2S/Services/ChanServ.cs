using Hugin.Core.Enums;
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
    private readonly Func<IAccountRepository> _accountRepositoryFactory;
    private readonly Func<IRegisteredChannelRepository> _registeredChannelRepositoryFactory;
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
    /// <param name="channelRepository">The channel repository (in-memory, singleton).</param>
    /// <param name="accountRepositoryFactory">Factory for creating scoped account repositories.</param>
    /// <param name="registeredChannelRepositoryFactory">Factory for creating scoped registered channel repositories.</param>
    /// <param name="localServerId">The local server ID.</param>
    /// <param name="servicesHost">The services hostname.</param>
    /// <param name="logger">The logger.</param>
    public ChanServ(
        IChannelRepository channelRepository,
        Func<IAccountRepository> accountRepositoryFactory,
        Func<IRegisteredChannelRepository> registeredChannelRepositoryFactory,
        ServerId localServerId,
        string servicesHost,
        ILogger<ChanServ> logger)
    {
        _channelRepository = channelRepository;
        _accountRepositoryFactory = accountRepositoryFactory;
        _registeredChannelRepositoryFactory = registeredChannelRepositoryFactory;
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

        // Check if user is in the channel and has ops
        var member = channel.Members.Values.FirstOrDefault(m => 
            m.Nickname.Value.Equals(context.SourceNick, StringComparison.OrdinalIgnoreCase));
        
        if (member is null)
        {
            await context.ReplyAsync(this, $"You must be in {channelName} to register it.", cancellationToken);
            return;
        }

        if (!member.IsOpOrHigher)
        {
            await context.ReplyAsync(this, $"You must be a channel operator in {channelName} to register it.", cancellationToken);
            return;
        }

        // Get scoped repositories for database operations
        var registeredChannelRepo = _registeredChannelRepositoryFactory();
        var accountRepo = _accountRepositoryFactory();

        // Check if channel is already registered
        if (await registeredChannelRepo.ExistsAsync(channelName, cancellationToken))
        {
            await context.ReplyAsync(this, $"Channel {channelName} is already registered.", cancellationToken);
            return;
        }

        // Get the founder's account ID
        var account = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Unable to find your account. Please re-identify with NickServ.", cancellationToken);
            return;
        }

        // Register the channel in the database
        var registration = await registeredChannelRepo.CreateAsync(channelName, account.Id, cancellationToken);

        // Store current topic if any
        if (!string.IsNullOrEmpty(channel.Topic))
        {
            registration.Topic = channel.Topic;
            await registeredChannelRepo.UpdateAsync(registration, cancellationToken);
        }

        await context.ReplyAsync(this, $"Channel {channelName} has been registered to your account.", cancellationToken);

        _logger.LogInformation("ChanServ: {Account} registered channel {Channel} (ID: {Id})",
            context.SourceAccount, channelName, registration.Id);
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
        if (!ChannelName.TryCreate(channelName, out _, out _))
        {
            await context.ReplyAsync(this, $"Invalid channel name: {channelName}", cancellationToken);
            return;
        }

        // Get scoped repositories for database operations
        var registeredChannelRepo = _registeredChannelRepositoryFactory();
        var accountRepo = _accountRepositoryFactory();

        // Look up registration in database
        var registration = await registeredChannelRepo.GetByNameAsync(channelName, cancellationToken);
        if (registration is null)
        {
            await context.ReplyAsync(this, $"Channel {channelName} is not registered.", cancellationToken);
            return;
        }

        // Get founder account name
        var founder = await accountRepo.GetByIdAsync(registration.FounderId, cancellationToken);
        var founderName = founder?.Name ?? "(unknown)";

        // Get successor account name if set
        string? successorName = null;
        if (registration.SuccessorId.HasValue)
        {
            var successor = await accountRepo.GetByIdAsync(registration.SuccessorId.Value, cancellationToken);
            successorName = successor?.Name;
        }

        // Display channel info
        await context.ReplyAsync(this, $"Information for \x02{channelName}\x02:", cancellationToken);
        await context.ReplyAsync(this, $"  Founder    : {founderName}", cancellationToken);
        if (successorName is not null)
        {
            await context.ReplyAsync(this, $"  Successor  : {successorName}", cancellationToken);
        }
        await context.ReplyAsync(this, $"  Registered : {registration.RegisteredAt:yyyy-MM-dd HH:mm:ss} UTC", cancellationToken);
        await context.ReplyAsync(this, $"  Last used  : {registration.LastUsedAt:yyyy-MM-dd HH:mm:ss} UTC", cancellationToken);
        if (!string.IsNullOrEmpty(registration.Topic))
        {
            await context.ReplyAsync(this, $"  Topic      : {registration.Topic}", cancellationToken);
        }
        if (!string.IsNullOrEmpty(registration.Modes))
        {
            await context.ReplyAsync(this, $"  Modes      : +{registration.Modes}", cancellationToken);
        }
        await context.ReplyAsync(this, $"  KeepTopic  : {(registration.KeepTopic ? "ON" : "OFF")}", cancellationToken);
        await context.ReplyAsync(this, $"  Secure     : {(registration.Secure ? "ON" : "OFF")}", cancellationToken);
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

        // Parse and validate channel name
        if (!ChannelName.TryCreate(channelName, out var parsedName, out _))
        {
            await context.ReplyAsync(this, $"Invalid channel name: {channelName}", cancellationToken);
            return;
        }

        var channel = _channelRepository.GetByName(parsedName!);
        if (channel is null)
        {
            await context.ReplyAsync(this, $"Channel {channelName} does not exist.", cancellationToken);
            return;
        }

        // Check if requester has permissions (must be op or have access)
        var requesterMember = channel.Members.Values.FirstOrDefault(m =>
            m.Nickname.Value.Equals(context.SourceNick, StringComparison.OrdinalIgnoreCase));
        
        if (requesterMember is null || (!requesterMember.IsOpOrHigher && !context.IsOperator))
        {
            await context.ReplyAsync(this, $"You don't have permission to op users in {channelName}.", cancellationToken);
            return;
        }

        // Find target user in channel
        var targetMember = channel.Members.Values.FirstOrDefault(m =>
            m.Nickname.Value.Equals(targetNick, StringComparison.OrdinalIgnoreCase));
        
        if (targetMember is null)
        {
            await context.ReplyAsync(this, $"{targetNick} is not in {channelName}.", cancellationToken);
            return;
        }

        // Grant operator status
        targetMember.AddMode(ChannelMemberMode.Op);

        // Send MODE change via services
        await context.Services.SendPrivmsgAsync(Uid, channelName, $"MODE {channelName} +o {targetNick}", cancellationToken);
        await context.ReplyAsync(this, $"{targetNick} has been opped in {channelName}.", cancellationToken);
        
        _logger.LogInformation("ChanServ: {Requester} opped {Target} in {Channel}", 
            context.SourceNick, targetNick, channelName);
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

        // Parse and validate channel name
        if (!ChannelName.TryCreate(channelName, out var parsedName, out _))
        {
            await context.ReplyAsync(this, $"Invalid channel name: {channelName}", cancellationToken);
            return;
        }

        var channel = _channelRepository.GetByName(parsedName!);
        if (channel is null)
        {
            await context.ReplyAsync(this, $"Channel {channelName} does not exist.", cancellationToken);
            return;
        }

        // Find target user in channel
        var targetMember = channel.Members.Values.FirstOrDefault(m =>
            m.Nickname.Value.Equals(targetNick, StringComparison.OrdinalIgnoreCase));

        if (targetMember is null)
        {
            await context.ReplyAsync(this, $"{targetNick} is not in {channelName}.", cancellationToken);
            return;
        }

        // Remove operator status
        targetMember.RemoveMode(ChannelMemberMode.Op);

        // Send MODE change notification
        await context.Services.SendPrivmsgAsync(Uid, channelName, $"MODE {channelName} -o {targetNick}", cancellationToken);
        await context.ReplyAsync(this, $"{targetNick} has been deopped in {channelName}.", cancellationToken);
        
        _logger.LogInformation("ChanServ: {Requester} deopped {Target} in {Channel}", 
            context.SourceNick, targetNick, channelName);
    }

    private async ValueTask HandleVoiceAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: VOICE <#channel> <nick>", cancellationToken);
            return;
        }

        var channelName = context.Arguments[0];
        var targetNick = context.Arguments[1];

        // Parse and validate channel name
        if (!ChannelName.TryCreate(channelName, out var parsedName, out _))
        {
            await context.ReplyAsync(this, $"Invalid channel name: {channelName}", cancellationToken);
            return;
        }

        var channel = _channelRepository.GetByName(parsedName!);
        if (channel is null)
        {
            await context.ReplyAsync(this, $"Channel {channelName} does not exist.", cancellationToken);
            return;
        }

        // Find target user in channel
        var targetMember = channel.Members.Values.FirstOrDefault(m =>
            m.Nickname.Value.Equals(targetNick, StringComparison.OrdinalIgnoreCase));

        if (targetMember is null)
        {
            await context.ReplyAsync(this, $"{targetNick} is not in {channelName}.", cancellationToken);
            return;
        }

        // Grant voice status
        targetMember.AddMode(ChannelMemberMode.Voice);

        // Send MODE change notification
        await context.Services.SendPrivmsgAsync(Uid, channelName, $"MODE {channelName} +v {targetNick}", cancellationToken);
        await context.ReplyAsync(this, $"{targetNick} has been voiced in {channelName}.", cancellationToken);
        
        _logger.LogInformation("ChanServ: {Requester} voiced {Target} in {Channel}", 
            context.SourceNick, targetNick, channelName);
    }

    private async ValueTask HandleDevoiceAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: DEVOICE <#channel> <nick>", cancellationToken);
            return;
        }

        var channelName = context.Arguments[0];
        var targetNick = context.Arguments[1];

        // Parse and validate channel name
        if (!ChannelName.TryCreate(channelName, out var parsedName, out _))
        {
            await context.ReplyAsync(this, $"Invalid channel name: {channelName}", cancellationToken);
            return;
        }

        var channel = _channelRepository.GetByName(parsedName!);
        if (channel is null)
        {
            await context.ReplyAsync(this, $"Channel {channelName} does not exist.", cancellationToken);
            return;
        }

        // Find target user in channel
        var targetMember = channel.Members.Values.FirstOrDefault(m =>
            m.Nickname.Value.Equals(targetNick, StringComparison.OrdinalIgnoreCase));

        if (targetMember is null)
        {
            await context.ReplyAsync(this, $"{targetNick} is not in {channelName}.", cancellationToken);
            return;
        }

        // Remove voice status
        targetMember.RemoveMode(ChannelMemberMode.Voice);

        // Send MODE change notification
        await context.Services.SendPrivmsgAsync(Uid, channelName, $"MODE {channelName} -v {targetNick}", cancellationToken);
        await context.ReplyAsync(this, $"{targetNick} has been devoiced in {channelName}.", cancellationToken);
        
        _logger.LogInformation("ChanServ: {Requester} devoiced {Target} in {Channel}", 
            context.SourceNick, targetNick, channelName);
    }

    private async ValueTask HandleKickAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: KICK <#channel> <nick> [reason]", cancellationToken);
            return;
        }

        var channelName = context.Arguments[0];
        var targetNick = context.Arguments[1];
        var reason = context.Arguments.Length > 2 
            ? string.Join(" ", context.Arguments.Skip(2)) 
            : "Requested by ChanServ";

        // Parse and validate channel name
        if (!ChannelName.TryCreate(channelName, out var parsedName, out _))
        {
            await context.ReplyAsync(this, $"Invalid channel name: {channelName}", cancellationToken);
            return;
        }

        var channel = _channelRepository.GetByName(parsedName!);
        if (channel is null)
        {
            await context.ReplyAsync(this, $"Channel {channelName} does not exist.", cancellationToken);
            return;
        }

        // Find target user in channel
        var targetMember = channel.Members.Values.FirstOrDefault(m =>
            m.Nickname.Value.Equals(targetNick, StringComparison.OrdinalIgnoreCase));

        if (targetMember is null)
        {
            await context.ReplyAsync(this, $"{targetNick} is not in {channelName}.", cancellationToken);
            return;
        }

        // Remove user from channel
        channel.RemoveMember(targetMember.ConnectionId);

        // Send KICK notification
        await context.Services.SendPrivmsgAsync(Uid, channelName, $"KICK {channelName} {targetNick} :{reason}", cancellationToken);
        await context.ReplyAsync(this, $"{targetNick} has been kicked from {channelName}.", cancellationToken);
        
        _logger.LogInformation("ChanServ: {Requester} kicked {Target} from {Channel} ({Reason})", 
            context.SourceNick, targetNick, channelName, reason);
    }

    private async ValueTask HandleBanAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: BAN <#channel> <nick|mask>", cancellationToken);
            return;
        }

        var channelName = context.Arguments[0];
        var target = context.Arguments[1];

        // Parse and validate channel name
        if (!ChannelName.TryCreate(channelName, out var parsedName, out _))
        {
            await context.ReplyAsync(this, $"Invalid channel name: {channelName}", cancellationToken);
            return;
        }

        var channel = _channelRepository.GetByName(parsedName!);
        if (channel is null)
        {
            await context.ReplyAsync(this, $"Channel {channelName} does not exist.", cancellationToken);
            return;
        }

        // Determine ban mask - if it's a nick, convert to hostmask
        string banMask = target;
        if (!target.Contains('!') && !target.Contains('@'))
        {
            // It's a nickname - try to look up the user
            var targetMember = channel.Members.Values.FirstOrDefault(m =>
                m.Nickname.Value.Equals(target, StringComparison.OrdinalIgnoreCase));
            
            if (targetMember is not null)
            {
                banMask = $"*!*@{targetMember.Nickname.Value}.user";
            }
            else
            {
                banMask = $"{target}!*@*";
            }
        }

        // Add ban to channel
        channel.AddBan(banMask, Nickname);

        // Send MODE change notification
        await context.Services.SendPrivmsgAsync(Uid, channelName, $"MODE {channelName} +b {banMask}", cancellationToken);
        await context.ReplyAsync(this, $"Ban mask {banMask} set on {channelName}.", cancellationToken);
        
        _logger.LogInformation("ChanServ: {Requester} banned {Mask} in {Channel}", 
            context.SourceNick, banMask, channelName);
    }

    private async ValueTask HandleUnbanAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: UNBAN <#channel> <mask>", cancellationToken);
            return;
        }

        var channelName = context.Arguments[0];
        var banMask = context.Arguments[1];

        // Parse and validate channel name
        if (!ChannelName.TryCreate(channelName, out var parsedName, out _))
        {
            await context.ReplyAsync(this, $"Invalid channel name: {channelName}", cancellationToken);
            return;
        }

        var channel = _channelRepository.GetByName(parsedName!);
        if (channel is null)
        {
            await context.ReplyAsync(this, $"Channel {channelName} does not exist.", cancellationToken);
            return;
        }

        // Remove ban from channel
        var removed = channel.RemoveBan(banMask);
        if (!removed)
        {
            await context.ReplyAsync(this, $"Ban mask {banMask} was not found on {channelName}.", cancellationToken);
            return;
        }

        // Send MODE change notification
        await context.Services.SendPrivmsgAsync(Uid, channelName, $"MODE {channelName} -b {banMask}", cancellationToken);
        await context.ReplyAsync(this, $"Ban mask {banMask} removed from {channelName}.", cancellationToken);
        
        _logger.LogInformation("ChanServ: {Requester} unbanned {Mask} in {Channel}", 
            context.SourceNick, banMask, channelName);
    }

    private async ValueTask HandleTopicAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            await context.ReplyAsync(this, "Syntax: TOPIC <#channel> <topic>", cancellationToken);
            return;
        }

        var channelName = context.Arguments[0];
        var newTopic = string.Join(" ", context.Arguments.Skip(1));

        // Parse and validate channel name
        if (!ChannelName.TryCreate(channelName, out var parsedName, out _))
        {
            await context.ReplyAsync(this, $"Invalid channel name: {channelName}", cancellationToken);
            return;
        }

        var channel = _channelRepository.GetByName(parsedName!);
        if (channel is null)
        {
            await context.ReplyAsync(this, $"Channel {channelName} does not exist.", cancellationToken);
            return;
        }

        // Set the topic
        channel.SetTopic(newTopic, Nickname);

        // Update registration if channel is registered
        var registeredChannelRepo = _registeredChannelRepositoryFactory();
        var registration = await registeredChannelRepo.GetByNameAsync(channelName, cancellationToken);
        if (registration is not null)
        {
            registration.Topic = newTopic;
            await registeredChannelRepo.UpdateAsync(registration, cancellationToken);
        }

        // Send TOPIC notification
        await context.Services.SendPrivmsgAsync(Uid, channelName, $"TOPIC {channelName} :{newTopic}", cancellationToken);
        await context.ReplyAsync(this, $"Topic for {channelName} has been set.", cancellationToken);
        
        _logger.LogInformation("ChanServ: {Requester} set topic of {Channel} to: {Topic}", 
            context.SourceNick, channelName, newTopic);
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

        var channelName = context.Arguments[0];
        var option = context.Arguments[1].ToUpperInvariant();
        var value = context.Arguments[2];

        // Parse and validate channel name
        if (!ChannelName.TryCreate(channelName, out _, out _))
        {
            await context.ReplyAsync(this, $"Invalid channel name: {channelName}", cancellationToken);
            return;
        }

        // Get scoped repositories for database operations
        var registeredChannelRepo = _registeredChannelRepositoryFactory();
        var accountRepo = _accountRepositoryFactory();

        // Look up registration in database
        var registration = await registeredChannelRepo.GetByNameAsync(channelName, cancellationToken);
        if (registration is null)
        {
            await context.ReplyAsync(this, $"Channel {channelName} is not registered.", cancellationToken);
            return;
        }

        // Get the user's account to check permissions
        var account = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Unable to find your account. Please re-identify with NickServ.", cancellationToken);
            return;
        }

        // Check if user is the channel founder or an IRC operator
        var isFounder = registration.FounderId == account.Id;
        if (!isFounder && !context.IsOperator)
        {
            await context.ReplyAsync(this, $"Only the channel founder can modify settings for {channelName}.", cancellationToken);
            return;
        }

        switch (option)
        {
            case "FOUNDER":
                var newFounder = await accountRepo.GetByNameAsync(value, cancellationToken);
                if (newFounder is null)
                {
                    await context.ReplyAsync(this, $"Account '{value}' not found.", cancellationToken);
                    return;
                }
                registration.FounderId = newFounder.Id;
                await registeredChannelRepo.UpdateAsync(registration, cancellationToken);
                await context.ReplyAsync(this, $"Founder of {channelName} changed to {value}.", cancellationToken);
                _logger.LogInformation("ChanServ: {User} changed founder of {Channel} to {NewFounder}",
                    context.SourceAccount, channelName, value);
                break;

            case "SUCCESSOR":
                if (value.Equals("NONE", StringComparison.OrdinalIgnoreCase) || value.Equals("-", StringComparison.OrdinalIgnoreCase))
                {
                    registration.SuccessorId = null;
                    await registeredChannelRepo.UpdateAsync(registration, cancellationToken);
                    await context.ReplyAsync(this, $"Successor for {channelName} has been cleared.", cancellationToken);
                }
                else
                {
                    var newSuccessor = await accountRepo.GetByNameAsync(value, cancellationToken);
                    if (newSuccessor is null)
                    {
                        await context.ReplyAsync(this, $"Account '{value}' not found.", cancellationToken);
                        return;
                    }
                    registration.SuccessorId = newSuccessor.Id;
                    await registeredChannelRepo.UpdateAsync(registration, cancellationToken);
                    await context.ReplyAsync(this, $"Successor of {channelName} set to {value}.", cancellationToken);
                }
                _logger.LogInformation("ChanServ: {User} changed successor of {Channel} to {Successor}",
                    context.SourceAccount, channelName, value);
                break;

            case "KEEPTOPIC":
                if (value.Equals("ON", StringComparison.OrdinalIgnoreCase) || value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || value == "1")
                {
                    registration.KeepTopic = true;
                    await registeredChannelRepo.UpdateAsync(registration, cancellationToken);
                    await context.ReplyAsync(this, $"KEEPTOPIC for {channelName} is now ON.", cancellationToken);
                }
                else if (value.Equals("OFF", StringComparison.OrdinalIgnoreCase) || value.Equals("FALSE", StringComparison.OrdinalIgnoreCase) || value == "0")
                {
                    registration.KeepTopic = false;
                    await registeredChannelRepo.UpdateAsync(registration, cancellationToken);
                    await context.ReplyAsync(this, $"KEEPTOPIC for {channelName} is now OFF.", cancellationToken);
                }
                else
                {
                    await context.ReplyAsync(this, "Value for KEEPTOPIC must be ON or OFF.", cancellationToken);
                }
                break;

            case "SECURE":
                if (value.Equals("ON", StringComparison.OrdinalIgnoreCase) || value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || value == "1")
                {
                    registration.Secure = true;
                    await registeredChannelRepo.UpdateAsync(registration, cancellationToken);
                    await context.ReplyAsync(this, $"SECURE for {channelName} is now ON. Only identified users may join.", cancellationToken);
                }
                else if (value.Equals("OFF", StringComparison.OrdinalIgnoreCase) || value.Equals("FALSE", StringComparison.OrdinalIgnoreCase) || value == "0")
                {
                    registration.Secure = false;
                    await registeredChannelRepo.UpdateAsync(registration, cancellationToken);
                    await context.ReplyAsync(this, $"SECURE for {channelName} is now OFF.", cancellationToken);
                }
                else
                {
                    await context.ReplyAsync(this, "Value for SECURE must be ON or OFF.", cancellationToken);
                }
                break;

            default:
                await context.ReplyAsync(this, $"Unknown option: {option}", cancellationToken);
                await context.ReplyAsync(this, "Valid options: FOUNDER, SUCCESSOR, KEEPTOPIC, SECURE", cancellationToken);
                break;
        }
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

        // Parse channel name
        if (!ChannelName.TryCreate(channelName, out var parsedName, out _))
        {
            await context.ReplyAsync(this, $"Invalid channel name: {channelName}", cancellationToken);
            return;
        }

        // Get scoped repositories for database operations
        var registeredChannelRepo = _registeredChannelRepositoryFactory();
        var accountRepo = _accountRepositoryFactory();

        // Check if channel is registered in database
        var registration = await registeredChannelRepo.GetByNameAsync(channelName, cancellationToken);
        if (registration is null)
        {
            await context.ReplyAsync(this, $"Channel {channelName} is not registered.", cancellationToken);
            return;
        }

        // Get the user's account to check founder status
        var account = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Unable to find your account. Please re-identify with NickServ.", cancellationToken);
            return;
        }

        // Check if user is the channel founder or an IRC operator
        var isFounder = registration.FounderId == account.Id;
        var isSuccessor = registration.SuccessorId.HasValue && registration.SuccessorId == account.Id;
        
        if (!isFounder && !isSuccessor && !context.IsOperator)
        {
            await context.ReplyAsync(this, $"Only the channel founder can drop {channelName}. Contact network staff for assistance.", cancellationToken);
            return;
        }

        // Remove channel registration from database
        var deleted = await registeredChannelRepo.DeleteAsync(registration.Id, cancellationToken);
        if (!deleted)
        {
            await context.ReplyAsync(this, $"Failed to drop channel {channelName}. Please try again.", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, $"Channel {channelName} has been dropped.", cancellationToken);

        _logger.LogInformation("ChanServ: {Account} dropped channel {Channel} (was registered by account {FounderId})",
            context.SourceAccount, channelName, registration.FounderId);
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
