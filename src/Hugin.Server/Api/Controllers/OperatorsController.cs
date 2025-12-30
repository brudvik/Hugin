// Hugin IRC Server - Operators and Bans Controller
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
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
    private readonly IOperatorConfigService _operatorService;
    private readonly ILogger<OperatorsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OperatorsController"/> class.
    /// </summary>
    public OperatorsController(
        IOperatorConfigService operatorService,
        ILogger<OperatorsController> logger)
    {
        _operatorService = operatorService;
        _logger = logger;
    }

    /// <summary>
    /// Gets all configured operators.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<OperatorDto[]>), StatusCodes.Status200OK)]
    public IActionResult GetOperators()
    {
        var operators = _operatorService.GetAllOperators()
            .Select(MapOperatorToDto)
            .ToArray();
        return Ok(ApiResponse<OperatorDto[]>.Ok(operators));
    }

    /// <summary>
    /// Gets a specific operator.
    /// </summary>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(ApiResponse<OperatorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public IActionResult GetOperator(string name)
    {
        var decodedName = System.Web.HttpUtility.UrlDecode(name);
        var oper = _operatorService.GetOperator(decodedName);
        
        if (oper == null)
        {
            return NotFound(ApiResponse.Fail($"Operator '{decodedName}' not found"));
        }

        return Ok(ApiResponse<OperatorDto>.Ok(MapOperatorToDto(oper)));
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

        // Check if operator already exists
        if (_operatorService.GetOperator(request.Name) != null)
        {
            return BadRequest(ApiResponse.Fail($"Operator '{request.Name}' already exists"));
        }

        // Password is required for new operators
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(ApiResponse.Fail("Password is required for new operators"));
        }

        var operConfig = new OperatorConfig
        {
            Name = request.Name,
            PasswordHash = Hugin.Security.PasswordHasher.HashPassword(request.Password),
            OperClass = request.OperClass,
            Hostmasks = request.Hostmasks ?? [],
            Permissions = GetDefaultPermissions(request.OperClass)
        };

        _operatorService.AddOrUpdateOperator(operConfig);
        _logger.LogInformation("Operator {Name} created by {Admin}", request.Name, User.Identity?.Name);

        return CreatedAtAction(nameof(GetOperator), new { name = request.Name }, 
            ApiResponse<OperatorDto>.Ok(MapOperatorToDto(operConfig)));
    }

    /// <summary>
    /// Updates an operator.
    /// </summary>
    [HttpPut("{name}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public IActionResult UpdateOperator(string name, [FromBody] OperatorRequest request)
    {
        var decodedName = System.Web.HttpUtility.UrlDecode(name);
        var existingOper = _operatorService.GetOperator(decodedName);
        
        if (existingOper == null)
        {
            return NotFound(ApiResponse.Fail($"Operator '{decodedName}' not found"));
        }

        // Update fields
        existingOper.OperClass = request.OperClass;
        if (request.Hostmasks != null)
        {
            existingOper.Hostmasks = request.Hostmasks;
        }
        
        // Update password only if provided
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            existingOper.PasswordHash = Hugin.Security.PasswordHasher.HashPassword(request.Password);
        }
        
        existingOper.Permissions = GetDefaultPermissions(request.OperClass);

        _operatorService.AddOrUpdateOperator(existingOper);
        _logger.LogInformation("Operator {Name} updated by {Admin}", decodedName, User.Identity?.Name);
        
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
        var decodedName = System.Web.HttpUtility.UrlDecode(name);
        
        if (!_operatorService.RemoveOperator(decodedName))
        {
            return NotFound(ApiResponse.Fail($"Operator '{decodedName}' not found"));
        }

        _logger.LogWarning("Operator {Name} deleted by {Admin}", decodedName, User.Identity?.Name);
        return Ok(ApiResponse.Ok("Operator deleted"));
    }

    /// <summary>
    /// Maps an OperatorConfig to OperatorDto.
    /// </summary>
    private static OperatorDto MapOperatorToDto(OperatorConfig config)
    {
        return new OperatorDto
        {
            Name = config.Name,
            OperClass = config.OperClass,
            Hostmasks = config.Hostmasks,
            IsOnline = config.IsOnline,
            LastSeen = config.LastSeen,
            Permissions = config.Permissions
        };
    }

    /// <summary>
    /// Gets default permissions based on operator class.
    /// </summary>
    private static string[] GetDefaultPermissions(string operClass)
    {
        return operClass.ToLowerInvariant() switch
        {
            "admin" => ["kill", "kline", "gline", "zline", "rehash", "restart", "die", "wallops"],
            "global" => ["kill", "kline", "gline", "wallops"],
            "local" => ["kill", "kline"],
            _ => []
        };
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
    private readonly IServerBanRepository _banRepository;
    private readonly ILogger<BansController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BansController"/> class.
    /// </summary>
    public BansController(
        IServerBanRepository banRepository,
        ILogger<BansController> logger)
    {
        _banRepository = banRepository;
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
        var allBans = _banRepository.GetAllActive();

        // Filter by type
        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<BanType>(type, true, out var banType))
        {
            allBans = allBans.Where(b => b.Type == banType).ToList();
        }

        // Filter by search
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLowerInvariant();
            allBans = allBans.Where(b =>
                b.Pattern.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                b.Reason.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                b.SetBy.Contains(searchLower, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var totalCount = allBans.Count;
        var bans = allBans
            .OrderByDescending(b => b.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapBanToDto)
            .ToArray();

        var result = new PagedResult<ServerBanDto>
        {
            Items = bans,
            TotalCount = totalCount,
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
        if (!Guid.TryParse(id, out var banId))
        {
            return NotFound(ApiResponse.Fail($"Invalid ban ID format '{id}'"));
        }

        var allBans = _banRepository.GetAllActive();
        var ban = allBans.FirstOrDefault(b => b.Id == banId);

        if (ban == null)
        {
            return NotFound(ApiResponse.Fail($"Ban '{id}' not found"));
        }

        return Ok(ApiResponse<ServerBanDto>.Ok(MapBanToDto(ban)));
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

        if (!Enum.TryParse<BanType>(request.Type, true, out var banType))
        {
            return BadRequest(ApiResponse.Fail($"Invalid ban type: {request.Type}. Valid types: KLine, GLine, ZLine, Jupe"));
        }

        var adminUser = User.Identity?.Name ?? "admin";
        var duration = request.DurationSeconds.HasValue
            ? TimeSpan.FromSeconds(request.DurationSeconds.Value)
            : (TimeSpan?)null;

        var serverBan = new ServerBan(
            banType,
            request.Mask,
            request.Reason,
            adminUser,
            duration);

        _banRepository.Add(serverBan);

        _logger.LogWarning("Ban created by {Admin}: {Type} {Mask} - {Reason}",
            adminUser, request.Type, request.Mask, request.Reason);

        return CreatedAtAction(nameof(GetBan), new { id = serverBan.Id.ToString() },
            ApiResponse<ServerBanDto>.Ok(MapBanToDto(serverBan)));
    }

    /// <summary>
    /// Removes a server ban.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public IActionResult RemoveBan(string id)
    {
        if (!Guid.TryParse(id, out var banId))
        {
            return NotFound(ApiResponse.Fail($"Invalid ban ID format '{id}'"));
        }

        var removed = _banRepository.Remove(banId);
        if (!removed)
        {
            return NotFound(ApiResponse.Fail($"Ban '{id}' not found"));
        }

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
        var allBans = _banRepository.GetAllActive();

        var stats = new BanStatsDto
        {
            TotalBans = allBans.Count,
            ActiveKLines = allBans.Count(b => b.Type == BanType.KLine),
            ActiveGLines = allBans.Count(b => b.Type == BanType.GLine),
            ActiveZLines = allBans.Count(b => b.Type == BanType.ZLine),
            ExpiredToday = 0 // Would need to track expired bans separately
        };

        return Ok(ApiResponse<BanStatsDto>.Ok(stats));
    }

    /// <summary>
    /// Maps a ServerBan entity to DTO.
    /// </summary>
    private static ServerBanDto MapBanToDto(ServerBan ban)
    {
        return new ServerBanDto
        {
            Id = ban.Id.ToString(),
            Type = ban.Type.ToString(),
            Mask = ban.Pattern,
            Reason = ban.Reason,
            SetBy = ban.SetBy,
            SetAt = ban.CreatedAt,
            ExpiresAt = ban.ExpiresAt,
            AffectedCount = 0
        };
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
