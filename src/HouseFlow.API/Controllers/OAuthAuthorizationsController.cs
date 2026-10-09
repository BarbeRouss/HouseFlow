using System.Security.Claims;
using HouseFlow.API.Configuration;
using HouseFlow.API.Filters;
using HouseFlow.API.OAuth;
using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.OAuth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HouseFlow.API.Controllers;

/// <summary>
/// The front end's side of the OAuth flow (issue #304): opening the <c>oauthSession</c> used by
/// <c>/connect/authorize</c>, the consent screen, and the « Applications connectées » settings.
/// </summary>
[ApiController]
[Route("api/v1/oauth")]
// Granting a third party access to the account is a personal act: like the RGPD actions, it only
// travels in the app's JWT — never in an API key handed to an integration.
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[EnableRateLimiting(RateLimitPolicies.Session)]
public class OAuthAuthorizationsController : ControllerBase
{
    private readonly OAuthConsentService _consents;
    private readonly OAuthSessionCookie _sessionCookie;
    private readonly ILogger<OAuthAuthorizationsController> _logger;

    public OAuthAuthorizationsController(
        OAuthConsentService consents, OAuthSessionCookie sessionCookie, ILogger<OAuthAuthorizationsController> logger)
    {
        _consents = consents;
        _sessionCookie = sessionCookie;
        _logger = logger;
    }

    /// <summary>Opens the OAuth session of the logged-in user: the <c>oauthSession</c> cookie (10 min, path /connect).</summary>
    [HttpPost("session")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult CreateSession()
    {
        _sessionCookie.Append(Response, GetUserId());
        return NoContent();
    }

    /// <summary>The client shown on the consent screen.</summary>
    [HttpGet("clients/{clientId}")]
    [ProducesResponseType(typeof(OAuthClientInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClient(string clientId, CancellationToken cancellationToken)
    {
        var application = await _consents.FindClientAsync(clientId, cancellationToken);
        if (application is null)
            return ApiProblem.Create(HttpContext, StatusCodes.Status404NotFound, "Unknown OAuth client", ErrorCodes.NotFound);

        return Ok(await _consents.DescribeClientAsync(application, cancellationToken));
    }

    /// <summary>The applications the user has authorized (valid consents), most recent first.</summary>
    [HttpGet("authorizations")]
    [ProducesResponseType(typeof(List<OAuthAuthorizationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListAuthorizations(CancellationToken cancellationToken) =>
        Ok(await _consents.ListAsync(GetUserId(), cancellationToken));

    /// <summary>
    /// « Autoriser » on the consent screen: records the checked scopes (added to an existing consent)
    /// and refreshes the <c>oauthSession</c> cookie for the return to <c>/connect/authorize</c>.
    /// </summary>
    [HttpPost("authorizations")]
    [ProducesResponseType(typeof(OAuthAuthorizationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GrantAuthorization(
        [FromBody] GrantOAuthAuthorizationRequestDto request, CancellationToken cancellationToken)
    {
        var application = await _consents.FindClientAsync(request.ClientId, cancellationToken);
        if (application is null)
            return ApiProblem.Create(HttpContext, StatusCodes.Status404NotFound, "Unknown OAuth client", ErrorCodes.NotFound);

        var clientScopes = await _consents.GetClientScopesAsync(application, cancellationToken);
        var scopes = request.Scopes.Distinct(StringComparer.Ordinal).ToList();
        if (scopes.Count == 0 || !scopes.All(scope => clientScopes.Contains(scope, StringComparer.Ordinal)))
            return ApiProblem.Create(HttpContext, StatusCodes.Status400BadRequest,
                $"scopes must be a non-empty subset of the client's scopes ({string.Join(' ', clientScopes)})",
                ErrorCodes.ValidationFailed);

        var userId = GetUserId();
        var authorization = await _consents.GrantAsync(userId, application, scopes, cancellationToken);

        _logger.LogInformation("OAuth consent granted to client {ClientId} by user {UserId}", request.ClientId, userId);

        _sessionCookie.Append(Response, userId);
        return StatusCode(StatusCodes.Status201Created, authorization);
    }

    /// <summary>« Révoquer » in the settings: the consent and every token issued under it stop working at once.</summary>
    [HttpDelete("authorizations/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeAuthorization(string id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (!await _consents.RevokeAsync(userId, id, cancellationToken))
            return ApiProblem.Create(HttpContext, StatusCodes.Status404NotFound, "Unknown OAuth authorization", ErrorCodes.NotFound);

        _logger.LogInformation("OAuth authorization {AuthorizationId} revoked by user {UserId}", id, userId);
        return NoContent();
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        return Guid.Parse(userIdClaim!.Value);
    }
}
