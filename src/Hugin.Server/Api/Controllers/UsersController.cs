// Hugin IRC Server - Users Controller
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using Hugin.Server.Api.Auth;
using Hugin.Server.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hugin.Server.Api.Controllers;

/// <summary>
/// User management endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly ILogger<UsersController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UsersController"/> class.
    /// </summary>
    public UsersController(ILogger<UsersController> logger)
    {
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
        // TODO: Get users from user manager
        var users = new List<UserDto>();

        var result = new PagedResult<UserDto>
        {
            Items = users.ToArray(),
            TotalCount = 0,
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
        // TODO: Get user from user manager
        return NotFound(ApiResponse.Fail($"User '{nickname}' not found"));
    }

    /// <summary>
    /// Sends a message to a user.
    /// </summary>
    [HttpPost("{nickname}/message")]
    [Authorize(Roles = $"{AdminRoles.Admin},{AdminRoles.Moderator}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public IActionResult SendMessage(string nickname, [FromBody] SendMessageRequest request)
    {
        // TODO: Send message via IRC
        _logger.LogInformation("Message sent to {Nickname} by {Admin}", nickname, User.Identity?.Name);
        return Ok(ApiResponse.Ok("Message sent"));
    }

    /// <summary>
    /// Kills (disconnects) a user.
    /// </summary>
    [HttpDelete("{nickname}")]
    [Authorize(Roles = $"{AdminRoles.Admin},{AdminRoles.Operator}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public IActionResult KillUser(string nickname, [FromQuery] string reason = "Killed by administrator")
    {
        // TODO: Kill user via IRC
        _logger.LogWarning("User {Nickname} killed by {Admin}: {Reason}", 
            nickname, User.Identity?.Name, reason);
        return Ok(ApiResponse.Ok($"User {nickname} disconnected"));
    }

    /// <summary>
    /// Changes a user's mode.
    /// </summary>
    [HttpPost("{nickname}/mode")]
    [Authorize(Roles = $"{AdminRoles.Admin},{AdminRoles.Operator}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public IActionResult SetUserMode(string nickname, [FromBody] SetModeRequest request)
    {
        // TODO: Set mode via IRC
        _logger.LogInformation("Mode {Mode} set on {Nickname} by {Admin}", 
            request.Mode, nickname, User.Identity?.Name);
        return Ok(ApiResponse.Ok($"Mode set on {nickname}"));
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
    private readonly ILogger<ChannelsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelsController"/> class.
    /// </summary>
    public ChannelsController(ILogger<ChannelsController> logger)
    {
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
        // TODO: Get channels from channel manager
        var channels = new List<ChannelDto>();

        var result = new PagedResult<ChannelDto>
        {
            Items = channels.ToArray(),
            TotalCount = 0,
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
        // TODO: Get channel from channel manager
        return NotFound(ApiResponse.Fail($"Channel '{name}' not found"));
    }

    /// <summary>
    /// Gets channel members.
    /// </summary>
    [HttpGet("{name}/members")]
    [ProducesResponseType(typeof(ApiResponse<ChannelMemberDto[]>), StatusCodes.Status200OK)]
    public IActionResult GetChannelMembers(string name)
    {
        // TODO: Get members from channel
        var members = new List<ChannelMemberDto>();
        return Ok(ApiResponse<ChannelMemberDto[]>.Ok(members.ToArray()));
    }

    /// <summary>
    /// Sets channel topic.
    /// </summary>
    [HttpPut("{name}/topic")]
    [Authorize(Roles = $"{AdminRoles.Admin},{AdminRoles.Moderator}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public IActionResult SetTopic(string name, [FromBody] SetTopicRequest request)
    {
        // TODO: Set topic via IRC
        _logger.LogInformation("Topic set on {Channel} by {Admin}", name, User.Identity?.Name);
        return Ok(ApiResponse.Ok("Topic set"));
    }

    /// <summary>
    /// Sets channel mode.
    /// </summary>
    [HttpPost("{name}/mode")]
    [Authorize(Roles = $"{AdminRoles.Admin},{AdminRoles.Operator}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public IActionResult SetChannelMode(string name, [FromBody] SetModeRequest request)
    {
        // TODO: Set mode via IRC
        _logger.LogInformation("Mode {Mode} set on {Channel} by {Admin}", 
            request.Mode, name, User.Identity?.Name);
        return Ok(ApiResponse.Ok($"Mode set on {name}"));
    }

    /// <summary>
    /// Clears a channel (kicks all users).
    /// </summary>
    [HttpPost("{name}/clear")]
    [Authorize(Roles = AdminRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public IActionResult ClearChannel(string name, [FromQuery] string reason = "Channel cleared by administrator")
    {
        // TODO: Clear channel via IRC
        _logger.LogWarning("Channel {Channel} cleared by {Admin}: {Reason}", 
            name, User.Identity?.Name, reason);
        return Ok(ApiResponse.Ok($"Channel {name} cleared"));
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
