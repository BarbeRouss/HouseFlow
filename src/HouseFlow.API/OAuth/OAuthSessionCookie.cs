using HouseFlow.API.Authentication;
using HouseFlow.Application.OAuth;

namespace HouseFlow.API.OAuth;

/// <summary>
/// The <c>oauthSession</c> cookie: who the user is when the browser reaches <c>/connect/authorize</c>
/// (a top-level navigation started by the OAuth client, which carries no bearer token). Posed by
/// <c>POST /api/v1/oauth/session</c> once the front end has a session, refreshed by the consent,
/// cleared by login, registration, logout and account deletion: on a shared browser, the next
/// person must not be taken for the previous one. Its value is an <see cref="OAuthSessionToken"/>:
/// short-lived, signed, with an audience of its own.
/// </summary>
public sealed class OAuthSessionCookie
{
    public const string Name = "oauthSession";

    /// <summary>Only sent to the protocol endpoints — the authorization endpoint is the only reader.</summary>
    public const string CookiePath = "/connect";

    private readonly string _jwtKey;
    private readonly string _issuer;
    private readonly SameSiteMode _sameSite;

    public OAuthSessionCookie(string jwtKey, string issuer, TimeSpan lifetime, SameSiteMode sameSite)
    {
        _jwtKey = jwtKey;
        _issuer = issuer;
        Lifetime = lifetime;
        _sameSite = sameSite;
    }

    public TimeSpan Lifetime { get; }

    /// <summary>
    /// A session cookie — no <c>Max-Age</c> nor <c>Expires</c>: the browser forgets it when it
    /// closes, and the expiry of the token it carries (<see cref="Lifetime"/>) bounds it before that.
    /// </summary>
    public void Append(HttpResponse response, Guid userId) =>
        response.Cookies.Append(Name, OAuthSessionToken.Create(userId, _jwtKey, _issuer, DateTime.UtcNow, Lifetime),
            Options(response.HttpContext.Request, _sameSite));

    /// <summary>The user of a valid session cookie, or <c>null</c> (absent, expired, forged, or another kind of token).</summary>
    public Guid? Read(HttpRequest request) =>
        OAuthSessionToken.Validate(request.Cookies[Name], _jwtKey, _issuer);

    /// <summary>
    /// Expires the cookie (same name and path, so from any endpoint). Static like
    /// <see cref="RefreshTokenCookie.Clear"/>: login, logout and account deletion need no key to forget it.
    /// </summary>
    public static void Clear(HttpResponse response, SameSiteMode sameSite) =>
        response.Cookies.Delete(Name, Options(response.HttpContext.Request, sameSite));

    /// <summary>Attributes shared by the cookie and its deletion (a browser only deletes a cookie whose path matches).</summary>
    private static CookieOptions Options(HttpRequest request, SameSiteMode sameSite) => new()
    {
        HttpOnly = true,
        // Same rule as the refresh cookie: always Secure outside Development, and with SameSite=None.
        Secure = RefreshTokenCookie.RequiresSecure(request, sameSite),
        // Lax (default) is still sent on the top-level GET navigation from the OAuth client to
        // /connect/authorize; None is for the deployments whose front end and API are cross-site.
        SameSite = sameSite,
        Path = CookiePath,
        IsEssential = true // strictly necessary to the authorization the user asked for — no consent needed
    };
}
