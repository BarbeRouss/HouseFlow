using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HouseFlow.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
[EnableRateLimiting("auth")] // 5 requests per minute for auth endpoints
public class AuthController : ControllerBase
{
    private const string RefreshCookieName = "refreshToken";

    private readonly IAuthService _authService;
    private readonly SameSiteMode _cookieSameSite;

    public AuthController(IAuthService authService, IConfiguration configuration)
    {
        _authService = authService;
        // "Lax" (default) protects /auth/refresh and /auth/logout against CSRF. "None" is only
        // for deployments where the frontend and the API live on different sites (PR previews:
        // Static Web App + Container App) — a Lax cookie is neither stored nor sent cross-site.
        _cookieSameSite = Enum.TryParse<SameSiteMode>(configuration["Auth:CookieSameSite"], ignoreCase: true, out var mode)
            ? mode
            : SameSiteMode.Lax;
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request, [FromQuery] string? invitationToken = null)
    {
        try
        {
            var ipAddress = GetIpAddress();
            var response = await _authService.RegisterAsync(request, ipAddress, invitationToken);

            // Set refresh token in HttpOnly cookie
            SetRefreshTokenCookie(response.RefreshToken!, response.RefreshCookieExpiresAt);

            // Don't return refresh token in response body (security)
            var sanitizedResponse = response with { RefreshToken = null, RefreshCookieExpiresAt = null };
            return Ok(sanitizedResponse);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already registered"))
        {
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
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

            // Set refresh token in HttpOnly cookie
            SetRefreshTokenCookie(response.RefreshToken!, response.RefreshCookieExpiresAt);

            // Don't return refresh token in response body (security)
            var sanitizedResponse = response with { RefreshToken = null, RefreshCookieExpiresAt = null };
            return Ok(sanitizedResponse);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken()
    {
        try
        {
            // Get refresh token from cookie
            var refreshToken = Request.Cookies[RefreshCookieName];

            if (string.IsNullOrEmpty(refreshToken))
            {
                return Unauthorized(new { error = "Refresh token not found" });
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
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpPost("revoke")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RevokeToken()
    {
        try
        {
            // Get refresh token from cookie
            var refreshToken = Request.Cookies[RefreshCookieName];

            if (string.IsNullOrEmpty(refreshToken))
            {
                return BadRequest(new { error = "Refresh token not found" });
            }

            var ipAddress = GetIpAddress();
            await _authService.RevokeTokenAsync(refreshToken, ipAddress);

            // Clear refresh token cookie
            Response.Cookies.Delete(RefreshCookieName, RefreshCookieOptions(expires: null));

            return Ok(new { message = "Token revoked successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout()
    {
        try
        {
            // Get refresh token from cookie and revoke it
            var refreshToken = Request.Cookies[RefreshCookieName];

            if (!string.IsNullOrEmpty(refreshToken))
            {
                var ipAddress = GetIpAddress();
                await _authService.RevokeTokenAsync(refreshToken, ipAddress);
            }

            // Clear refresh token cookie
            Response.Cookies.Delete(RefreshCookieName, RefreshCookieOptions(expires: null));

            return Ok(new { message = "Logged out successfully" });
        }
        catch
        {
            // Even if revoke fails, clear the cookie
            Response.Cookies.Delete(RefreshCookieName, RefreshCookieOptions(expires: null));
            return Ok(new { message = "Logged out successfully" });
        }
    }

    /// <param name="expires">
    /// Persistent cookie expiry ("remember me"); null makes it a session cookie that
    /// the browser drops when closed.
    /// </param>
    private void SetRefreshTokenCookie(string refreshToken, DateTime? expires) =>
        Response.Cookies.Append(RefreshCookieName, refreshToken, RefreshCookieOptions(expires));

    /// <summary>
    /// Options shared by Append and Delete: a browser only honours the deletion when the
    /// attributes (path, SameSite, Secure) match the cookie it stored.
    /// </summary>
    private CookieOptions RefreshCookieOptions(DateTime? expires) => new()
    {
        HttpOnly = true,  // Cannot be accessed by JavaScript (XSS protection)
        // Browsers require Secure with SameSite=None; loopback hosts accept it over plain HTTP
        Secure = HttpContext.Request.IsHttps || _cookieSameSite == SameSiteMode.None,
        SameSite = _cookieSameSite,
        Expires = expires,
        Path = "/",
        IsEssential = true
    };

    private string? GetIpAddress()
    {
        // Try to get IP from X-Forwarded-For header (if behind proxy)
        if (Request.Headers.ContainsKey("X-Forwarded-For"))
        {
            return Request.Headers["X-Forwarded-For"].ToString().Split(',').FirstOrDefault()?.Trim();
        }

        // Fall back to RemoteIpAddress
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}
