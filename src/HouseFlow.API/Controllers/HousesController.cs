using System.Security.Claims;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HouseFlow.API.Controllers;

[ApiController]
[Route("api/v1/houses")]
[Authorize]
[Produces("application/json")]
public class HousesController : ControllerBase
{
    private readonly IHouseService _houseService;

    public HousesController(IHouseService houseService)
    {
        _houseService = houseService;
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim ?? throw new UnauthorizedAccessException());
    }

    /// <summary>
    /// Liste des maisons de l'utilisateur avec scores
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(HousesListResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHouses()
    {
        var userId = GetUserId();
        var result = await _houseService.GetUserHousesAsync(userId);
        return Ok(result);
    }

    /// <summary>
    /// Créer une nouvelle maison
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(HouseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateHouse([FromBody] CreateHouseRequestDto request)
    {
        var userId = GetUserId();
        var house = await _houseService.CreateHouseAsync(request, userId);
        return CreatedAtAction(nameof(GetHouse), new { houseId = house.Id }, house);
    }

    /// <summary>
    /// Détail d'une maison avec ses appareils
    /// </summary>
    [HttpGet("{houseId}")]
    [ProducesResponseType(typeof(HouseDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHouse(Guid houseId)
    {
        var userId = GetUserId();
        var house = await _houseService.GetHouseDetailAsync(houseId, userId);

        if (house == null)
        {
            return NotFound();
        }

        return Ok(house);
    }

    /// <summary>
    /// Modifier une maison
    /// </summary>
    [HttpPut("{houseId}")]
    [ProducesResponseType(typeof(HouseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateHouse(Guid houseId, [FromBody] UpdateHouseRequestDto request)
    {
        var userId = GetUserId();
        var house = await _houseService.UpdateHouseAsync(houseId, request, userId);

        if (house == null)
        {
            return NotFound();
        }

        return Ok(house);
    }

    /// <summary>
    /// Supprimer une maison
    /// </summary>
    [HttpDelete("{houseId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteHouse(Guid houseId)
    {
        var userId = GetUserId();
        var deleted = await _houseService.DeleteHouseAsync(houseId, userId);

        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }
}
