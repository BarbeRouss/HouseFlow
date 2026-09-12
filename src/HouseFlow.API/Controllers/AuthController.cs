using HouseFlow.API.Extensions;
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
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
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
            SetRefreshTokenCookie(response.RefreshToken!);

            // Don't return refresh token in response body (security)
            var sanitizedResponse = response with { RefreshToken = null };
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
            SetRefreshTokenCookie(response.RefreshToken!);

            // Don't return refresh token in response body (security)
            var sanitizedResponse = response with { RefreshToken = null };
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
            var refreshToken = Request.Cookies["refreshToken"];

            if (string.IsNullOrEmpty(refreshToken))
            {
                return Unauthorized(new { error = "Refresh token not found" });
            }

            var ipAddress = GetIpAddress();
            var response = await _authService.RefreshTokenAsync(refreshToken, ipAddress);

            // Set new refresh token in HttpOnly cookie
            SetRefreshTokenCookie(response.RefreshToken!);

            // Don't return refresh token in response body (security)
            var sanitizedResponse = response with { RefreshToken = null };
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
            var refreshToken = Request.Cookies["refreshToken"];

            if (string.IsNullOrEmpty(refreshToken))
            {
                return BadRequest(new { error = "Refresh token not found" });
            }

            var ipAddress = GetIpAddress();
            await _authService.RevokeTokenAsync(refreshToken, ipAddress);

            // Clear refresh token cookie
            ClearRefreshTokenCookie(Response);

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
            var refreshToken = Request.Cookies["refreshToken"];

            if (!string.IsNullOrEmpty(refreshToken))
            {
                var ipAddress = GetIpAddress();
                await _authService.RevokeTokenAsync(refreshToken, ipAddress);
            }

            // Clear refresh token cookie
            ClearRefreshTokenCookie(Response);

            return Ok(new { message = "Logged out successfully" });
        }
        catch
        {
            // Even if revoke fails, clear the cookie
            ClearRefreshTokenCookie(Response);
            return Ok(new { message = "Logged out successfully" });
        }
    }

    private void SetRefreshTokenCookie(string refreshToken)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,  // Cannot be accessed by JavaScript (XSS protection)
            Secure = HttpContext.Request.IsHttps,  // Only sent over HTTPS (in production)
            SameSite = SameSiteMode.Lax, // CSRF protection (Lax for development compatibility)
            Expires = DateTime.UtcNow.AddDays(7), // 7 days
            Path = RefreshTokenCookiePath, // Only sent to the auth endpoints that need it (minimisation, Art. 25/32)
            IsEssential = true // Strictly necessary cookie — exempt from consent (art. 82 loi Informatique et Libertés)
        };

        Response.Cookies.Append(RefreshTokenCookieName, refreshToken, cookieOptions);
    }

    /// <summary>Name of the HttpOnly refresh-token cookie.</summary>
    public const string RefreshTokenCookieName = "refreshToken";

    /// <summary>
    /// Path scope of the refresh-token cookie. Must be reused (with the same value) by any
    /// endpoint that deletes the cookie, e.g. account deletion.
    /// </summary>
    public const string RefreshTokenCookiePath = "/api/v1/auth";

    /// <summary>Removes the refresh-token cookie (same path as when it was set, otherwise browsers keep it).</summary>
    public static void ClearRefreshTokenCookie(HttpResponse response) =>
        response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions { Path = RefreshTokenCookiePath });

    private string? GetIpAddress() => HttpContext.GetClientIp();
}
