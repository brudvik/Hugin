// Hugin IRC Server - Operators and Bans Controller
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using Hugin.Server.Api.Auth;
using Hugin.Server.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hugin.Server.Api.Controllers;

/// <summary>
/// IRC Operator management endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = AdminRoles.Admin)]
public sealed class OperatorsController : ControllerBase
{
    private readonly ILogger<OperatorsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OperatorsController"/> class.
    /// </summary>
    public OperatorsController(ILogger<OperatorsController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets all configured operators.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<OperatorDto[]>), StatusCodes.Status200OK)]
    public IActionResult GetOperators()
    {
        // TODO: Get operators from configuration
        var operators = new List<OperatorDto>();
        return Ok(ApiResponse<OperatorDto[]>.Ok(operators.ToArray()));
    }

    /// <summary>
    /// Gets a specific operator.
    /// </summary>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(ApiResponse<OperatorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public IActionResult GetOperator(string name)
    {
        // TODO: Get operator from configuration
        return NotFound(ApiResponse.Fail($"Operator '{name}' not found"));
    }

    /// <summary>
    /// Creates a new operator.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<OperatorDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public IActionResult CreateOperator([FromBody] OperatorRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse.Fail("Invalid operator configuration"));
        }

        // TODO: Create operator
        _logger.LogInformation("Operator {Name} created by {Admin}", request.Name, User.Identity?.Name);

        var oper = new OperatorDto
        {
            Name = request.Name,
            OperClass = request.OperClass,
            Hostmasks = request.Hostmasks ?? [],
            IsOnline = false,
            Permissions = []
        };

        return CreatedAtAction(nameof(GetOperator), new { name = request.Name }, 
            ApiResponse<OperatorDto>.Ok(oper));
    }

    /// <summary>
    /// Updates an operator.
    /// </summary>
    [HttpPut("{name}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public IActionResult UpdateOperator(string name, [FromBody] OperatorRequest request)
    {
        // TODO: Update operator
        _logger.LogInformation("Operator {Name} updated by {Admin}", name, User.Identity?.Name);
        return Ok(ApiResponse.Ok("Operator updated"));
    }

    /// <summary>
    /// Deletes an operator.
    /// </summary>
    [HttpDelete("{name}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public IActionResult DeleteOperator(string name)
    {
        // TODO: Delete operator
        _logger.LogWarning("Operator {Name} deleted by {Admin}", name, User.Identity?.Name);
        return Ok(ApiResponse.Ok("Operator deleted"));
    }
}

/// <summary>
/// Server ban management endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{AdminRoles.Admin},{AdminRoles.Operator}")]
public sealed class BansController : ControllerBase
{
    private readonly ILogger<BansController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BansController"/> class.
    /// </summary>
    public BansController(ILogger<BansController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets all server bans.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ServerBanDto>>), StatusCodes.Status200OK)]
    public IActionResult GetBans(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? type = null,
        [FromQuery] string? search = null)
    {
        // TODO: Get bans from ban manager
        var bans = new List<ServerBanDto>();

        var result = new PagedResult<ServerBanDto>
        {
            Items = bans.ToArray(),
            TotalCount = 0,
            Page = page,
            PageSize = pageSize
        };

        return Ok(ApiResponse<PagedResult<ServerBanDto>>.Ok(result));
    }

    /// <summary>
    /// Gets a specific ban.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<ServerBanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public IActionResult GetBan(string id)
    {
        // TODO: Get ban from ban manager
        return NotFound(ApiResponse.Fail($"Ban '{id}' not found"));
    }

    /// <summary>
    /// Creates a new server ban.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ServerBanDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public IActionResult CreateBan([FromBody] CreateBanRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse.Fail("Invalid ban configuration"));
        }

        // TODO: Create ban
        var banId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogWarning("Ban created by {Admin}: {Type} {Mask} - {Reason}", 
            User.Identity?.Name, request.Type, request.Mask, request.Reason);

        var ban = new ServerBanDto
        {
            Id = banId,
            Type = request.Type,
            Mask = request.Mask,
            Reason = request.Reason,
            SetBy = User.Identity?.Name ?? "admin",
            SetAt = DateTimeOffset.UtcNow,
            ExpiresAt = request.DurationSeconds.HasValue 
                ? DateTimeOffset.UtcNow.AddSeconds(request.DurationSeconds.Value) 
                : null,
            AffectedCount = 0
        };

        return CreatedAtAction(nameof(GetBan), new { id = banId }, 
            ApiResponse<ServerBanDto>.Ok(ban));
    }

    /// <summary>
    /// Removes a server ban.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public IActionResult RemoveBan(string id)
    {
        // TODO: Remove ban
        _logger.LogInformation("Ban {Id} removed by {Admin}", id, User.Identity?.Name);
        return Ok(ApiResponse.Ok("Ban removed"));
    }

    /// <summary>
    /// Gets ban statistics.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(ApiResponse<BanStatsDto>), StatusCodes.Status200OK)]
    public IActionResult GetBanStats()
    {
        // TODO: Get stats from ban manager
        var stats = new BanStatsDto
        {
            TotalBans = 0,
            ActiveKLines = 0,
            ActiveGLines = 0,
            ActiveZLines = 0,
            ExpiredToday = 0
        };

        return Ok(ApiResponse<BanStatsDto>.Ok(stats));
    }
}

/// <summary>
/// Ban statistics DTO.
/// </summary>
public sealed class BanStatsDto
{
    /// <summary>
    /// Total number of active bans.
    /// </summary>
    public required int TotalBans { get; init; }

    /// <summary>
    /// Active K-lines (local bans).
    /// </summary>
    public required int ActiveKLines { get; init; }

    /// <summary>
    /// Active G-lines (global bans).
    /// </summary>
    public required int ActiveGLines { get; init; }

    /// <summary>
    /// Active Z-lines (IP bans).
    /// </summary>
    public required int ActiveZLines { get; init; }

    /// <summary>
    /// Bans expired today.
    /// </summary>
    public required int ExpiredToday { get; init; }
}
