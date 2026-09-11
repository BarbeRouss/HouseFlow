using System.Security.Claims;
using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HouseFlow.API.Controllers;

/// <summary>
/// Platform administration. Restricted to users carrying the Admin role claim, which is
/// only issued in JWTs of users flagged <c>IsAdmin</c> (never via API keys).
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = AdminBootstrap.AdminRole)]
[Produces("application/json")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;

    public AdminController(IAdminService adminService)
    {
        _adminService = adminService;
    }

    [HttpGet("stats")]
    [ProducesResponseType(typeof(AdminStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStats()
    {
        return Ok(await _adminService.GetStatsAsync());
    }

    [HttpGet("users")]
    [ProducesResponseType(typeof(AdminUsersPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsers([FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        return Ok(await _adminService.GetUsersAsync(search, page, pageSize));
    }

    [HttpPut("users/{id:guid}/admin")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetAdmin(Guid id, [FromBody] SetUserAdminRequestDto request)
    {
        var user = await _adminService.SetAdminAsync(GetUserId(), id, request.IsAdmin);
        return Ok(user);
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return Guid.Parse(userIdClaim!.Value);
    }
}
