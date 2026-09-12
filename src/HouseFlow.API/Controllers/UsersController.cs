using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using HouseFlow.API.Extensions;
using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HouseFlow.API.Controllers;

/// <summary>
/// Exercice en self-service des droits RGPD de l'utilisateur connecté :
/// accès (Art. 15), rectification (Art. 16), effacement (Art. 17), portabilité (Art. 20).
/// </summary>
[ApiController]
[Route("api/v1/users/me")]
[Authorize]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private const string JsonFormat = "json";
    private const string CsvFormat = "csv";

    /// <summary>Le DTO d'export porte déjà ses énumérations en chaînes ; seul le camelCase est requis.</summary>
    private static readonly JsonSerializerOptions ExportJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IUserAccountService _userAccountService;

    public UsersController(IUserAccountService userAccountService)
    {
        _userAccountService = userAccountService;
    }

    /// <summary>RGPD Art. 15 — profil de l'utilisateur connecté.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken)
    {
        var profile = await _userAccountService.GetProfileAsync(GetUserId(), cancellationToken);
        return Ok(profile);
    }

    /// <summary>RGPD Art. 16 — rectification du prénom, du nom et de l'email.</summary>
    [HttpPut]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateProfileRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var profile = await _userAccountService.UpdateProfileAsync(GetUserId(), request, cancellationToken);
            return Ok(profile);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already used", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>
    /// RGPD Art. 17 — suppression immédiate et définitive du compte, confirmée par
    /// ressaisie du mot de passe. Le cookie refreshToken est effacé.
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteMyAccount([FromBody] DeleteAccountRequestDto request, CancellationToken cancellationToken)
    {
        await _userAccountService.DeleteAccountAsync(GetUserId(), request.Password, HttpContext.GetClientIp(), cancellationToken);

        AuthController.ClearRefreshTokenCookie(Response);
        return NoContent();
    }

    /// <summary>
    /// RGPD Art. 15 + 20 — copie complète des données personnelles, en JSON (défaut)
    /// ou en archive ZIP de CSV. Limité à un export par heure (429).
    /// </summary>
    [HttpGet("export")]
    [ProducesResponseType(typeof(UserDataExportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ExportMyData([FromQuery] string? format, CancellationToken cancellationToken)
    {
        var requestedFormat = string.IsNullOrWhiteSpace(format) ? JsonFormat : format.Trim().ToLowerInvariant();

        // Validé avant l'export : une requête malformée ne doit pas consommer le quota horaire.
        if (requestedFormat is not (JsonFormat or CsvFormat))
        {
            return BadRequest(new { error = "Unsupported export format. Use 'json' or 'csv'." });
        }

        var export = await _userAccountService.ExportDataAsync(GetUserId(), HttpContext.GetClientIp(), cancellationToken);
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (requestedFormat == CsvFormat)
        {
            var archive = CsvExportWriter.CreateZipArchive(export);
            return File(archive, "application/zip", $"houseflow-data-export-{date}.zip");
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(export, ExportJsonOptions);
        return File(json, "application/json", $"houseflow-data-export-{date}.json");
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return Guid.Parse(userIdClaim!.Value);
    }
}
