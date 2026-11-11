using HouseFlow.API.Authentication;
using HouseFlow.API.Configuration;
using HouseFlow.API.Extensions;
using HouseFlow.API.Filters;
using HouseFlow.API.OAuth;
using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HouseFlow.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
// Credential checks (login, register): 5/min per client. The session endpoints below override it
// with RateLimitPolicies.Session — see there.
[EnableRateLimiting(RateLimitPolicies.Credentials)]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly SameSiteMode _cookieSameSite;

    public AuthController(IAuthService authService, IConfiguration configuration)
    {
        _authService = authService;
        _cookieSameSite = RefreshTokenCookie.ResolveSameSite(configuration);
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request, [FromQuery] string? invitationToken = null)
    {
        try
        {
            var ipAddress = GetIpAddress();
            var response = await _authService.RegisterAsync(request, ipAddress, invitationToken);

            // Set refresh token in HttpOnly cookie (and forget the OAuth session, see StartSession)
            StartSession(response);

            // Don't return refresh token in response body (security)
            var sanitizedResponse = response with { RefreshToken = null, RefreshCookieExpiresAt = null };
            return Ok(sanitizedResponse);
        }
        catch (ConflictException ex)
        {
            return ApiProblem.FromException(HttpContext, StatusCodes.Status409Conflict, ex);
        }
        catch (InvalidOperationException ex)
        {
            return ApiProblem.FromException(HttpContext, StatusCodes.Status400BadRequest, ex);
        }
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        try
        {
            var ipAddress = GetIpAddress();
            var response = await _authService.LoginAsync(request, ipAddress);

            // Set refresh token in HttpOnly cookie (and forget the OAuth session, see StartSession)
            StartSession(response);

            // Don't return refresh token in response body (security)
            var sanitizedResponse = response with { RefreshToken = null, RefreshCookieExpiresAt = null };
            return Ok(sanitizedResponse);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiProblem.FromException(HttpContext, StatusCodes.Status401Unauthorized, ex);
        }
    }

    [HttpPost("refresh")]
    [EnableRateLimiting(RateLimitPolicies.Session)]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken()
    {
        try
        {
            // Get refresh token from cookie
            var refreshToken = Request.Cookies[RefreshTokenCookie.Name];

            if (string.IsNullOrEmpty(refreshToken))
            {
                return ApiProblem.Create(HttpContext, StatusCodes.Status401Unauthorized,
                    "Refresh token not found", ErrorCodes.InvalidRefreshToken);
            }

            var ipAddress = GetIpAddress();
            var response = await _authService.RefreshTokenAsync(refreshToken, ipAddress);

            // Set new refresh token in HttpOnly cookie
            SetRefreshTokenCookie(response.RefreshToken!, response.RefreshCookieExpiresAt);

            // Don't return refresh token in response body (security)
            var sanitizedResponse = response with { RefreshToken = null, RefreshCookieExpiresAt = null };
            return Ok(sanitizedResponse);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiProblem.FromException(HttpContext, StatusCodes.Status401Unauthorized, ex);
        }
    }

    [HttpPost("revoke")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Session)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RevokeToken()
    {
        try
        {
            // Get refresh token from cookie
            var refreshToken = Request.Cookies[RefreshTokenCookie.Name];

            if (string.IsNullOrEmpty(refreshToken))
            {
                return ApiProblem.Create(HttpContext, StatusCodes.Status400BadRequest,
                    "Refresh token not found", ErrorCodes.InvalidRefreshToken);
            }

            var ipAddress = GetIpAddress();
            await _authService.RevokeTokenAsync(refreshToken, ipAddress);

            // Clear refresh token cookie (and the OAuth session, see ClearSessionCookies)
            ClearSessionCookies();

            return Ok(new { message = "Token revoked successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return ApiProblem.Create(HttpContext, StatusCodes.Status400BadRequest,
                ex.Message, ErrorCodes.InvalidRefreshToken);
        }
    }

    [HttpPost("logout")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Session)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout()
    {
        try
        {
            // Get refresh token from cookie and revoke it
            var refreshToken = Request.Cookies[RefreshTokenCookie.Name];

            if (!string.IsNullOrEmpty(refreshToken))
            {
                var ipAddress = GetIpAddress();
                await _authService.RevokeTokenAsync(refreshToken, ipAddress);
            }

            // Clear refresh token cookie (and the OAuth session, see ClearSessionCookies)
            ClearSessionCookies();

            return Ok(new { message = "Logged out successfully" });
        }
        catch
        {
            // Even if revoke fails, clear the cookies
            ClearSessionCookies();
            return Ok(new { message = "Logged out successfully" });
        }
    }

    /// <summary>
    /// The refresh cookie, and the OAuth session cookie (<c>oauthSession</c>, 10 min): on a shared
    /// browser, the next person to start an OAuth authorization must not be taken for the one who
    /// just logged out — an application they had authorized would get a code for their account.
    /// </summary>
    private void ClearSessionCookies()
    {
        RefreshTokenCookie.Clear(Response, _cookieSameSite);
        OAuthSessionCookie.Clear(Response, _cookieSameSite);
    }

    /// <summary>
    /// A login or a registration: a new identity in this browser. The OAuth session cookie of the
    /// previous one (<c>oauthSession</c>, up to 10 min) is cleared before the refresh cookie is set:
    /// on a shared browser, an application the new user connects would otherwise be handed a code
    /// for the previous user's account.
    /// </summary>
    private void StartSession(AuthResponseDto response)
    {
        OAuthSessionCookie.Clear(Response, _cookieSameSite);
        SetRefreshTokenCookie(response.RefreshToken!, response.RefreshCookieExpiresAt);
    }

    /// <param name="expires">
    /// Expiration d'un cookie persistant (« se souvenir de moi ») ; null pour un cookie de session.
    /// </param>
    private void SetRefreshTokenCookie(string refreshToken, DateTime? expires) =>
        RefreshTokenCookie.Append(Response, refreshToken, _cookieSameSite, expires);

    private string? GetIpAddress() => HttpContext.GetClientIp();
}
