// Hugin IRC Server - Users Controller
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using System.Text;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Hugin.Persistence;
using Hugin.Protocol;
using Hugin.Server.Api.Auth;
using Hugin.Server.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hugin.Server.Api.Controllers;

/// <summary>
/// User management endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly IMessageBroker _messageBroker;
    private readonly IConnectionManager _connectionManager;
    private readonly IConfiguration _configuration;
    private readonly HuginDbContext _dbContext;
    private readonly ILogger<UsersController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UsersController"/> class.
    /// </summary>
    public UsersController(
        IUserRepository userRepository,
        IChannelRepository channelRepository,
        IMessageBroker messageBroker,
        IConnectionManager connectionManager,
        IConfiguration configuration,
        HuginDbContext dbContext,
        ILogger<UsersController> logger)
    {
        _userRepository = userRepository;
        _channelRepository = channelRepository;
        _messageBroker = messageBroker;
        _connectionManager = connectionManager;
        _configuration = configuration;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Gets all connected users.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserDto>>), StatusCodes.Status200OK)]
    public IActionResult GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null,
        [FromQuery] string? channel = null,
        [FromQuery] bool? operators = null)
    {
        // Get all users from repository
        IEnumerable<User> allUsers = _userRepository.GetAll()
            .Where(u => u.IsRegistered);

        // Filter by search (nickname, username, or realname)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLowerInvariant();
            allUsers = allUsers.Where(u =>
                u.Nickname?.Value?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) == true ||
                u.Username.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                u.RealName.Contains(searchLower, StringComparison.OrdinalIgnoreCase));
        }

        // Filter by channel
        if (!string.IsNullOrWhiteSpace(channel))
        {
            if (ChannelName.TryCreate(channel, out var channelName, out _))
            {
                allUsers = allUsers.Where(u => u.Channels.ContainsKey(channelName!));
            }
        }

        // Filter operators only
        if (operators == true)
        {
            allUsers = allUsers.Where(u => u.IsOperator);
        }

        var userList = allUsers.ToList();
        var totalCount = userList.Count;

        // Paginate
        var pagedUsers = userList
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapUserToDto)
            .ToArray();

        var result = new PagedResult<UserDto>
        {
            Items = pagedUsers,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };

        return Ok(ApiResponse<PagedResult<UserDto>>.Ok(result));
    }

    /// <summary>
    /// Gets a specific user by nickname.
    /// </summary>
    [HttpGet("{nickname}")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public IActionResult GetUser(string nickname)
    {
        if (!Nickname.TryCreate(nickname, out var nick, out _))
        {
            return NotFound(ApiResponse.Fail($"Invalid nickname '{nickname}'"));
        }

        var user = _userRepository.GetByNickname(nick!);
        if (user == null || !user.IsRegistered)
        {
            return NotFound(ApiResponse.Fail($"User '{nickname}' not found"));
        }

        return Ok(ApiResponse<UserDto>.Ok(MapUserToDto(user)));
    }

    /// <summary>
    /// Maps a User entity to a UserDto.
    /// </summary>
    private UserDto MapUserToDto(User user)
    {
        return new UserDto
        {
            Nickname = user.Nickname?.Value ?? "?",
            Username = user.Username,
            Hostname = user.DisplayedHostname,
            RealName = user.RealName,
            Account = user.Account,
            IsOperator = user.IsOperator,
            Modes = FormatUserModes(user),
            Channels = user.Channels.Keys.Select(c => c.Value).ToArray(),
            ConnectedAt = user.ConnectedAt,
            LastActivity = user.LastActivity,
            IsAway = user.IsAway,
            AwayMessage = user.AwayMessage,
            IsSecure = user.IsSecure,
            RealIp = user.IpAddress.ToString()
        };
    }

    /// <summary>
    /// Formats user modes as a string.
    /// </summary>
    private static string FormatUserModes(User user)
    {
        var modes = new List<char>();
        if (user.IsOperator) modes.Add('o');
        if (user.IsSecure) modes.Add('z');
        if (user.Modes.HasFlag(UserMode.Invisible)) modes.Add('i');
        if (user.Modes.HasFlag(UserMode.Wallops)) modes.Add('w');
        return modes.Count > 0 ? "+" + new string(modes.ToArray()) : "";
    }

    /// <summary>
    /// Sends a message to a user.
    /// </summary>
    [HttpPost("{nickname}/message")]
    [Authorize(Roles = $"{AdminRoles.Admin},{AdminRoles.Moderator}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendMessage(string nickname, [FromBody] SendMessageRequest request, CancellationToken cancellationToken)
    {
        var decodedNick = System.Web.HttpUtility.UrlDecode(nickname);
        
        if (!Nickname.TryCreate(decodedNick, out var nickObj, out _))
        {
            return NotFound(ApiResponse.Fail($"Invalid nickname '{decodedNick}'"));
        }
        
        var targetUser = _userRepository.GetByNickname(nickObj!);
        
        if (targetUser == null)
        {
            return NotFound(ApiResponse.Fail($"User '{decodedNick}' not found"));
        }

        var serverName = _configuration["Hugin:Server:Name"] ?? "irc.hugin.local";
        var command = request.AsNotice ? "NOTICE" : "PRIVMSG";
        var message = IrcMessage.CreateWithSource(serverName, command, decodedNick, request.Message);
        
        await _messageBroker.SendToConnectionAsync(targetUser.ConnectionId, message.ToString(), cancellationToken);
        
        _logger.LogInformation("{Command} sent to {Nickname} by {Admin}: {Message}", 
            command, decodedNick, User.Identity?.Name, request.Message);
        return Ok(ApiResponse.Ok("Message sent"));
    }

    /// <summary>
    /// Kills (disconnects) a user.
    /// </summary>
    [HttpDelete("{nickname}")]
    [Authorize(Roles = $"{AdminRoles.Admin},{AdminRoles.Operator}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> KillUser(string nickname, [FromQuery] string reason = "Killed by administrator", CancellationToken cancellationToken = default)
    {
        var decodedNick = System.Web.HttpUtility.UrlDecode(nickname);
        
        if (!Nickname.TryCreate(decodedNick, out var nickObj, out _))
        {
            return NotFound(ApiResponse.Fail($"Invalid nickname '{decodedNick}'"));
        }
        
        var targetUser = _userRepository.GetByNickname(nickObj!);
        
        if (targetUser == null)
        {
            return NotFound(ApiResponse.Fail($"User '{decodedNick}' not found"));
        }

        var serverName = _configuration["Hugin:Server:Name"] ?? "irc.hugin.local";
        var adminName = User.Identity?.Name ?? "admin";
        var killReason = $"Killed by {adminName}: {reason}";
        
        // Send KILL message to the user
        var killMessage = IrcMessage.CreateWithSource(serverName, "KILL", decodedNick, killReason);
        await _messageBroker.SendToConnectionAsync(targetUser.ConnectionId, killMessage.ToString(), cancellationToken);
        
        // Close the connection
        if (targetUser.ConnectionId != Guid.Empty)
        {
            await _connectionManager.CloseConnectionAsync(targetUser.ConnectionId, killReason, cancellationToken);
        }
        
        _logger.LogWarning("User {Nickname} killed by {Admin}: {Reason}", 
            decodedNick, adminName, reason);
        return Ok(ApiResponse.Ok($"User {decodedNick} disconnected"));
    }

    /// <summary>
    /// Changes a user's mode.
    /// </summary>
    [HttpPost("{nickname}/mode")]
    [Authorize(Roles = $"{AdminRoles.Admin},{AdminRoles.Operator}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetUserMode(string nickname, [FromBody] SetModeRequest request, CancellationToken cancellationToken)
    {
        var decodedNick = System.Web.HttpUtility.UrlDecode(nickname);
        
        if (!Nickname.TryCreate(decodedNick, out var nickObj, out _))
        {
            return NotFound(ApiResponse.Fail($"Invalid nickname '{decodedNick}'"));
        }
        
        var targetUser = _userRepository.GetByNickname(nickObj!);
        
        if (targetUser == null)
        {
            return NotFound(ApiResponse.Fail($"User '{decodedNick}' not found"));
        }

        // Parse and apply modes
        var modes = request.Mode;
        var adding = true;
        foreach (var c in modes)
        {
            if (c == '+') { adding = true; continue; }
            if (c == '-') { adding = false; continue; }
            
            var userMode = c switch
            {
                'i' => UserMode.Invisible,
                'o' => UserMode.Operator,
                'w' => UserMode.Wallops,
                's' => UserMode.ServerNotices,
                'r' => UserMode.Registered,
                'B' => UserMode.Bot,
                'Z' => UserMode.Secure,
                _ => (UserMode?)null
            };
            
            if (userMode.HasValue)
            {
                if (adding)
                    targetUser.AddMode(userMode.Value);
                else
                    targetUser.RemoveMode(userMode.Value);
            }
        }

        var serverName = _configuration["Hugin:Server:Name"] ?? "irc.hugin.local";
        var modeMessage = IrcMessage.CreateWithSource(serverName, "MODE", decodedNick, modes);
        await _messageBroker.SendToConnectionAsync(targetUser.ConnectionId, modeMessage.ToString(), cancellationToken);
        
        _logger.LogInformation("Mode {Mode} set on {Nickname} by {Admin}", 
            modes, decodedNick, User.Identity?.Name);
        return Ok(ApiResponse.Ok($"Mode set on {decodedNick}"));
    }
}

/// <summary>
/// Channels management controller.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class ChannelsController : ControllerBase
{
    private readonly IChannelRepository _channelRepository;
    private readonly IUserRepository _userRepository;
    private readonly IMessageBroker _messageBroker;
    private readonly IConfiguration _configuration;
    private readonly HuginDbContext _dbContext;
    private readonly ILogger<ChannelsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelsController"/> class.
    /// </summary>
    public ChannelsController(
        IChannelRepository channelRepository,
        IUserRepository userRepository,
        IMessageBroker messageBroker,
        IConfiguration configuration,
        HuginDbContext dbContext,
        ILogger<ChannelsController> logger)
    {
        _channelRepository = channelRepository;
        _userRepository = userRepository;
        _messageBroker = messageBroker;
        _configuration = configuration;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Gets all channels.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ChannelDto>>), StatusCodes.Status200OK)]
    public IActionResult GetChannels(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null,
        [FromQuery] int? minUsers = null)
    {
        // Get all channels from repository
        IEnumerable<Channel> allChannels = _channelRepository.GetAll();

        // Filter by search (channel name)
        if (!string.IsNullOrWhiteSpace(search))
        {
            allChannels = allChannels.Where(c =>
                c.Name.Value.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (c.Topic?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // Filter by minimum users
        if (minUsers.HasValue && minUsers > 0)
        {
            allChannels = allChannels.Where(c => c.MemberCount >= minUsers.Value);
        }

        var channelList = allChannels.ToList();
        var totalCount = channelList.Count;
        
        // Get all channel registrations for lookup - load all registrations first, then filter in memory
        var channelNames = channelList.Select(c => c.Name.Value.ToUpperInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var registrations = _dbContext.RegisteredChannels
            .AsEnumerable()
            .Where(r => channelNames.Contains(r.Name))
            .ToDictionary(r => r.Name.ToUpperInvariant(), r => r, StringComparer.OrdinalIgnoreCase);

        // Paginate
        var pagedChannels = channelList
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => MapChannelToDto(c, registrations.GetValueOrDefault(c.Name.Value.ToUpperInvariant())))
            .ToArray();

        var result = new PagedResult<ChannelDto>
        {
            Items = pagedChannels,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };

        return Ok(ApiResponse<PagedResult<ChannelDto>>.Ok(result));
    }

    /// <summary>
    /// Gets a specific channel.
    /// </summary>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(ApiResponse<ChannelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public IActionResult GetChannel(string name)
    {
        // Handle URL encoding - # is encoded as %23
        var decodedName = Uri.UnescapeDataString(name);
        if (!decodedName.StartsWith('#'))
        {
            decodedName = "#" + decodedName;
        }

        if (!ChannelName.TryCreate(decodedName, out var channelName, out _))
        {
            return NotFound(ApiResponse.Fail($"Invalid channel name '{name}'"));
        }

        var channel = _channelRepository.GetByName(channelName!);
        if (channel == null)
        {
            return NotFound(ApiResponse.Fail($"Channel '{name}' not found"));
        }

        // Check if channel is registered
        var registration = _dbContext.RegisteredChannels
            .FirstOrDefault(r => r.Name.Equals(channel.Name.Value, StringComparison.OrdinalIgnoreCase));

        return Ok(ApiResponse<ChannelDto>.Ok(MapChannelToDto(channel, registration)));
    }

    /// <summary>
    /// Gets channel members.
    /// </summary>
    [HttpGet("{name}/members")]
    [ProducesResponseType(typeof(ApiResponse<ChannelMemberDto[]>), StatusCodes.Status200OK)]
    public IActionResult GetChannelMembers(string name)
    {
        var decodedName = Uri.UnescapeDataString(name);
        if (!decodedName.StartsWith('#'))
        {
            decodedName = "#" + decodedName;
        }

        if (!ChannelName.TryCreate(decodedName, out var channelName, out _))
        {
            return Ok(ApiResponse<ChannelMemberDto[]>.Ok([]));
        }

        var channel = _channelRepository.GetByName(channelName!);
        if (channel == null)
        {
            return Ok(ApiResponse<ChannelMemberDto[]>.Ok([]));
        }

        var members = new List<ChannelMemberDto>();
        foreach (var (connectionId, member) in channel.Members)
        {
            var user = _userRepository.GetByConnectionId(connectionId);
            if (user != null)
            {
                members.Add(new ChannelMemberDto
                {
                    Nickname = user.Nickname?.Value ?? "?",
                    Prefixes = FormatMemberPrefixes(member.Modes),
                    JoinedAt = member.JoinedAt,
                    IsAway = user.IsAway
                });
            }
        }

        return Ok(ApiResponse<ChannelMemberDto[]>.Ok(members.ToArray()));
    }

    /// <summary>
    /// Maps a Channel entity to a ChannelDto.
    /// </summary>
    private ChannelDto MapChannelToDto(Channel channel, ChannelRegistrationEntity? registration)
    {
        return new ChannelDto
        {
            Name = channel.Name.Value,
            Topic = channel.Topic,
            TopicSetBy = channel.TopicSetBy,
            TopicSetAt = channel.TopicSetAt,
            Modes = FormatChannelModes(channel),
            UserCount = channel.MemberCount,
            CreatedAt = channel.CreatedAt,
            IsRegistered = registration != null,
            Founder = registration != null ? GetFounderNickname(registration.FounderId) : null
        };
    }
    
    /// <summary>
    /// Gets the founder nickname from account ID.
    /// </summary>
    private string? GetFounderNickname(Guid founderId)
    {
        var account = _dbContext.Accounts.FirstOrDefault(a => a.Id == founderId);
        return account?.Name;
    }

    /// <summary>
    /// Formats channel modes as a string.
    /// </summary>
    private static string FormatChannelModes(Channel channel)
    {
        var modes = new List<char>();
        var modeFlags = channel.Modes;

        if (modeFlags.HasFlag(ChannelMode.NoExternalMessages)) modes.Add('n');
        if (modeFlags.HasFlag(ChannelMode.TopicProtected)) modes.Add('t');
        if (modeFlags.HasFlag(ChannelMode.InviteOnly)) modes.Add('i');
        if (modeFlags.HasFlag(ChannelMode.Moderated)) modes.Add('m');
        if (modeFlags.HasFlag(ChannelMode.Secret)) modes.Add('s');
        if (modeFlags.HasFlag(ChannelMode.Private)) modes.Add('p');
        if (channel.Key != null) modes.Add('k');
        if (channel.UserLimit.HasValue) modes.Add('l');

        return modes.Count > 0 ? "+" + new string(modes.ToArray()) : "";
    }

    /// <summary>
    /// Formats member prefix modes.
    /// </summary>
    private static string FormatMemberPrefixes(ChannelMemberMode modes)
    {
        var prefixes = new StringBuilder();
        if (modes.HasFlag(ChannelMemberMode.Owner)) prefixes.Append('~');
        if (modes.HasFlag(ChannelMemberMode.Admin)) prefixes.Append('&');
        if (modes.HasFlag(ChannelMemberMode.Op)) prefixes.Append('@');
        if (modes.HasFlag(ChannelMemberMode.HalfOp)) prefixes.Append('%');
        if (modes.HasFlag(ChannelMemberMode.Voice)) prefixes.Append('+');
        return prefixes.ToString();
    }

    /// <summary>
    /// Sets channel topic.
    /// </summary>
    [HttpPut("{name}/topic")]
    [Authorize(Roles = $"{AdminRoles.Admin},{AdminRoles.Moderator}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetTopic(string name, [FromBody] SetTopicRequest request, CancellationToken cancellationToken)
    {
        // Decode channel name
        var decodedName = Uri.UnescapeDataString(name);
        if (!decodedName.StartsWith('#'))
        {
            decodedName = "#" + decodedName;
        }

        if (!ChannelName.TryCreate(decodedName, out var channelName, out _))
        {
            return NotFound(ApiResponse.Fail($"Invalid channel name '{name}'"));
        }

        var channel = _channelRepository.GetByName(channelName!);
        if (channel == null)
        {
            return NotFound(ApiResponse.Fail($"Channel '{name}' not found"));
        }

        // Set topic on the channel
        var serverName = _configuration["Hugin:Server:Name"] ?? "irc.hugin.local";
        var adminUser = User.Identity?.Name ?? "admin";
        var setBy = $"{adminUser}!admin@{serverName}";
        
        channel.SetTopic(request.Topic, setBy);

        // Broadcast TOPIC message to all channel members
        var topicMessage = $":{setBy} TOPIC {channel.Name.Value} :{request.Topic}";
        await _messageBroker.SendToChannelAsync(channel.Name.Value, topicMessage, null, cancellationToken);

        _logger.LogInformation("Topic set on {Channel} by {Admin}: {Topic}", 
            channel.Name.Value, adminUser, request.Topic);
        
        return Ok(ApiResponse.Ok("Topic set"));
    }

    /// <summary>
    /// Sets channel mode.
    /// </summary>
    [HttpPost("{name}/mode")]
    [Authorize(Roles = $"{AdminRoles.Admin},{AdminRoles.Operator}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetChannelMode(string name, [FromBody] SetModeRequest request, CancellationToken cancellationToken)
    {
        var decodedName = Uri.UnescapeDataString(name);
        if (!decodedName.StartsWith('#'))
        {
            decodedName = "#" + decodedName;
        }

        if (!ChannelName.TryCreate(decodedName, out var channelName, out _))
        {
            return NotFound(ApiResponse.Fail($"Invalid channel name '{name}'"));
        }

        var channel = _channelRepository.GetByName(channelName!);
        if (channel == null)
        {
            return NotFound(ApiResponse.Fail($"Channel '{name}' not found"));
        }

        // Parse and apply modes
        var modes = request.Mode;
        var adding = true;
        foreach (var c in modes)
        {
            if (c == '+') { adding = true; continue; }
            if (c == '-') { adding = false; continue; }
            
            var channelMode = c switch
            {
                'n' => ChannelMode.NoExternalMessages,
                't' => ChannelMode.TopicProtected,
                'i' => ChannelMode.InviteOnly,
                'm' => ChannelMode.Moderated,
                's' => ChannelMode.Secret,
                'p' => ChannelMode.Private,
                _ => (ChannelMode?)null
            };
            
            if (channelMode.HasValue)
            {
                if (adding)
                    channel.AddMode(channelMode.Value);
                else
                    channel.RemoveMode(channelMode.Value);
            }
        }

        // Broadcast MODE change to all channel members
        var serverName = _configuration["Hugin:Server:Name"] ?? "irc.hugin.local";
        var adminUser = User.Identity?.Name ?? "admin";
        var modeMessage = $":{serverName} MODE {channel.Name.Value} {modes}";
        if (!string.IsNullOrWhiteSpace(request.Parameter))
        {
            modeMessage += $" {request.Parameter}";
        }
        await _messageBroker.SendToChannelAsync(channel.Name.Value, modeMessage, null, cancellationToken);

        _logger.LogInformation("Mode {Mode} set on {Channel} by {Admin}", 
            modes, channel.Name.Value, adminUser);
        return Ok(ApiResponse.Ok($"Mode set on {channel.Name.Value}"));
    }

    /// <summary>
    /// Clears a channel (kicks all users).
    /// </summary>
    [HttpPost("{name}/clear")]
    [Authorize(Roles = AdminRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearChannel(string name, [FromQuery] string reason = "Channel cleared by administrator", CancellationToken cancellationToken = default)
    {
        var decodedName = Uri.UnescapeDataString(name);
        if (!decodedName.StartsWith('#'))
        {
            decodedName = "#" + decodedName;
        }

        if (!ChannelName.TryCreate(decodedName, out var channelName, out _))
        {
            return NotFound(ApiResponse.Fail($"Invalid channel name '{name}'"));
        }

        var channel = _channelRepository.GetByName(channelName!);
        if (channel == null)
        {
            return NotFound(ApiResponse.Fail($"Channel '{name}' not found"));
        }

        var serverName = _configuration["Hugin:Server:Name"] ?? "irc.hugin.local";
        var adminUser = User.Identity?.Name ?? "admin";
        var kickBy = $"{adminUser}!admin@{serverName}";
        var kickedCount = 0;

        // Get all members and kick them one by one
        var members = channel.Members.Values.ToList();
        foreach (var member in members)
        {
            // Send KICK message
            var kickMessage = $":{kickBy} KICK {channel.Name.Value} {member.Nickname.Value} :{reason}";
            await _messageBroker.SendToChannelAsync(channel.Name.Value, kickMessage, null, cancellationToken);
            
            // Remove member from channel using connectionId
            channel.RemoveMember(member.ConnectionId);
            
            // Look up user and update their channel list
            var user = _userRepository.GetByNickname(member.Nickname);
            if (user != null)
            {
                user.PartChannel(channel.Name);
            }
            kickedCount++;
        }

        _logger.LogWarning("Channel {Channel} cleared by {Admin}: {Reason} ({Count} users kicked)", 
            channel.Name.Value, adminUser, reason, kickedCount);
        return Ok(ApiResponse.Ok($"Channel {channel.Name.Value} cleared ({kickedCount} users kicked)"));
    }
}

/// <summary>
/// Request to send a message.
/// </summary>
public sealed class SendMessageRequest
{
    /// <summary>
    /// Message content.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Whether to send as NOTICE instead of PRIVMSG.
    /// </summary>
    public bool AsNotice { get; init; }
}

/// <summary>
/// Request to set a mode.
/// </summary>
public sealed class SetModeRequest
{
    /// <summary>
    /// Mode string (e.g., "+o" or "-v").
    /// </summary>
    public required string Mode { get; init; }

    /// <summary>
    /// Optional mode parameter.
    /// </summary>
    public string? Parameter { get; init; }
}

/// <summary>
/// Request to set topic.
/// </summary>
public sealed class SetTopicRequest
{
    /// <summary>
    /// New topic content.
    /// </summary>
    public required string Topic { get; init; }
}
