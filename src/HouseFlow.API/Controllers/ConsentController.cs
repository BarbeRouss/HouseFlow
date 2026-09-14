using HouseFlow.API.Extensions;
using System.Security.Claims;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HouseFlow.API.Controllers;

/// <summary>
/// Acceptation des Conditions générales d'utilisation et prise de connaissance de la
/// politique de confidentialité par un utilisateur existant (bannière de ré-acceptation
/// après publication d'une nouvelle version).
/// </summary>
/// <remarks>
/// Le nommage « consent » est historique (issues #135/#134). La base légale du compte est
/// l'exécution du contrat (Art. 6(1)(b)), pas le consentement de l'Art. 6(1)(a) : ce endpoint
/// enregistre une acceptation contractuelle et une preuve d'information (Art. 5(2), 13).
/// </remarks>
[ApiController]
[Route("api/v1/users/me/consent")]
[Authorize]
[Produces("application/json")]
public class ConsentController : ControllerBase
{
    private readonly IConsentService _consentService;

    public ConsentController(IConsentService consentService)
    {
        _consentService = consentService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ConsentStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetConsentStatus()
    {
        var status = await _consentService.GetConsentStatusAsync(GetUserId());
        return Ok(status);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ConsentStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RecordConsent([FromBody] ConsentRequestDto request)
    {
        try
        {
            var status = await _consentService.RecordConsentAsync(
                GetUserId(), request.Accepted, request.PolicyVersion, GetIpAddress());
            return Ok(status);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        return Guid.Parse(userIdClaim!.Value);
    }

    private string? GetIpAddress() => HttpContext.GetClientIp();

}
