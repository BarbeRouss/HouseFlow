using System.Security.Claims;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HouseFlow.API.Controllers;

[ApiController]
[Route("api/v1/maintenance-types")]
[Authorize]
[Produces("application/json")]
public class MaintenanceController : ControllerBase
{
    private readonly IMaintenanceService _maintenanceService;

    public MaintenanceController(IMaintenanceService maintenanceService)
    {
        _maintenanceService = maintenanceService;
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim ?? throw new UnauthorizedAccessException());
    }

    /// <summary>
    /// Modifier un type d'entretien
    /// </summary>
    [HttpPut("{typeId}")]
    [ProducesResponseType(typeof(MaintenanceTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMaintenanceType(Guid typeId, [FromBody] UpdateMaintenanceTypeRequestDto request)
    {
        var userId = GetUserId();
        var type = await _maintenanceService.UpdateMaintenanceTypeAsync(typeId, request, userId);

        if (type == null)
        {
            return NotFound();
        }

        return Ok(type);
    }

    /// <summary>
    /// Supprimer un type d'entretien
    /// </summary>
    [HttpDelete("{typeId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMaintenanceType(Guid typeId)
    {
        var userId = GetUserId();
        var deleted = await _maintenanceService.DeleteMaintenanceTypeAsync(typeId, userId);

        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpPost("{typeId}/instances")]
    [ProducesResponseType(typeof(MaintenanceInstanceDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> LogMaintenance(Guid typeId, [FromBody] LogMaintenanceRequestDto request)
    {
        var userId = GetUserId();
        var instance = await _maintenanceService.LogMaintenanceAsync(typeId, request, userId);
        return CreatedAtAction(nameof(LogMaintenance), new { typeId }, instance);
    }
}

[ApiController]
[Route("api/v1/upcoming-tasks")]
[Authorize]
[Produces("application/json")]
public class UpcomingTasksController : ControllerBase
{
    private readonly IMaintenanceService _maintenanceService;

    public UpcomingTasksController(IMaintenanceService maintenanceService)
    {
        _maintenanceService = maintenanceService;
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim ?? throw new UnauthorizedAccessException());
    }

    /// <summary>
    /// Liste des tâches d'entretien à venir (pending et overdue)
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(UpcomingTasksResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUpcomingTasks([FromQuery] int? limit = null)
    {
        var userId = GetUserId();
        var result = await _maintenanceService.GetUpcomingTasksAsync(userId, limit);
        return Ok(result);
    }
}

[ApiController]
[Route("api/v1/maintenance-instances")]
[Authorize]
[Produces("application/json")]
public class MaintenanceInstancesController : ControllerBase
{
    private readonly IMaintenanceService _maintenanceService;

    public MaintenanceInstancesController(IMaintenanceService maintenanceService)
    {
        _maintenanceService = maintenanceService;
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim ?? throw new UnauthorizedAccessException());
    }

    /// <summary>
    /// Modifier une instance d'entretien
    /// </summary>
    [HttpPut("{instanceId}")]
    [ProducesResponseType(typeof(MaintenanceInstanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMaintenanceInstance(Guid instanceId, [FromBody] UpdateMaintenanceInstanceRequestDto request)
    {
        var userId = GetUserId();
        var instance = await _maintenanceService.UpdateMaintenanceInstanceAsync(instanceId, request, userId);

        if (instance == null)
        {
            return NotFound();
        }

        return Ok(instance);
    }

    /// <summary>
    /// Supprimer une instance d'entretien
    /// </summary>
    [HttpDelete("{instanceId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMaintenanceInstance(Guid instanceId)
    {
        var userId = GetUserId();
        var deleted = await _maintenanceService.DeleteMaintenanceInstanceAsync(instanceId, userId);

        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }
}
