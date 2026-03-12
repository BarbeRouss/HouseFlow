using System.Security.Claims;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HouseFlow.API.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
public class DevicesController : ControllerBase
{
    private readonly IDeviceService _deviceService;
    private readonly IMaintenanceService _maintenanceService;

    public DevicesController(IDeviceService deviceService, IMaintenanceService maintenanceService)
    {
        _deviceService = deviceService;
        _maintenanceService = maintenanceService;
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim ?? throw new UnauthorizedAccessException());
    }

    /// <summary>
    /// Liste des appareils d'une maison
    /// </summary>
    [HttpGet("api/v1/houses/{houseId}/devices")]
    [ProducesResponseType(typeof(IEnumerable<DeviceSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHouseDevices(Guid houseId)
    {
        try
        {
            var userId = GetUserId();
            var devices = await _deviceService.GetHouseDevicesAsync(houseId, userId);
            return Ok(devices);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Créer un appareil
    /// </summary>
    [HttpPost("api/v1/houses/{houseId}/devices")]
    [ProducesResponseType(typeof(DeviceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateDevice(Guid houseId, [FromBody] CreateDeviceRequestDto request)
    {
        try
        {
            var userId = GetUserId();
            var device = await _deviceService.CreateDeviceAsync(houseId, request, userId);
            return CreatedAtAction(nameof(GetDevice), new { deviceId = device.Id }, device);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Détail d'un appareil avec ses types d'entretien
    /// </summary>
    [HttpGet("api/v1/devices/{deviceId}")]
    [ProducesResponseType(typeof(DeviceDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDevice(Guid deviceId)
    {
        try
        {
            var userId = GetUserId();
            var device = await _deviceService.GetDeviceDetailAsync(deviceId, userId);

            if (device == null)
            {
                return NotFound();
            }

            return Ok(device);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Modifier un appareil
    /// </summary>
    [HttpPut("api/v1/devices/{deviceId}")]
    [ProducesResponseType(typeof(DeviceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateDevice(Guid deviceId, [FromBody] UpdateDeviceRequestDto request)
    {
        try
        {
            var userId = GetUserId();
            var device = await _deviceService.UpdateDeviceAsync(deviceId, request, userId);

            if (device == null)
            {
                return NotFound();
            }

            return Ok(device);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Supprimer un appareil
    /// </summary>
    [HttpDelete("api/v1/devices/{deviceId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDevice(Guid deviceId)
    {
        try
        {
            var userId = GetUserId();
            var deleted = await _deviceService.DeleteDeviceAsync(deviceId, userId);

            if (!deleted)
            {
                return NotFound();
            }

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Liste des types d'entretien d'un appareil
    /// </summary>
    [HttpGet("api/v1/devices/{deviceId}/maintenance-types")]
    [ProducesResponseType(typeof(IEnumerable<MaintenanceTypeWithStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDeviceMaintenanceTypes(Guid deviceId)
    {
        try
        {
            var userId = GetUserId();
            var types = await _maintenanceService.GetDeviceMaintenanceTypesAsync(deviceId, userId);
            return Ok(types);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Créer un type d'entretien
    /// </summary>
    [HttpPost("api/v1/devices/{deviceId}/maintenance-types")]
    [ProducesResponseType(typeof(MaintenanceTypeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateMaintenanceType(Guid deviceId, [FromBody] CreateMaintenanceTypeRequestDto request)
    {
        try
        {
            var userId = GetUserId();
            var type = await _maintenanceService.CreateMaintenanceTypeAsync(deviceId, request, userId);
            return CreatedAtAction(nameof(GetDeviceMaintenanceTypes), new { deviceId }, type);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Historique des entretiens d'un appareil
    /// </summary>
    [HttpGet("api/v1/devices/{deviceId}/maintenance-history")]
    [ProducesResponseType(typeof(MaintenanceHistoryResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDeviceMaintenanceHistory(Guid deviceId)
    {
        try
        {
            var userId = GetUserId();
            var history = await _maintenanceService.GetDeviceMaintenanceHistoryAsync(deviceId, userId);
            return Ok(history);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
