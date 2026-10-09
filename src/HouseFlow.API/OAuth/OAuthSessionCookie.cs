using HouseFlow.API.Authentication;
using HouseFlow.Application.OAuth;

namespace HouseFlow.API.OAuth;

/// <summary>
/// The <c>oauthSession</c> cookie: who the user is when the browser reaches <c>/connect/authorize</c>
/// (a top-level navigation started by the OAuth client, which carries no bearer token). Posed by
/// <c>POST /api/v1/oauth/session</c> once the front end has a session, refreshed by the consent.
/// Its value is an <see cref="OAuthSessionToken"/>: short-lived, signed, with an audience of its own.
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

    public void Append(HttpResponse response, Guid userId) =>
        response.Cookies.Append(Name,
            OAuthSessionToken.Create(userId, _jwtKey, _issuer, DateTime.UtcNow, Lifetime),
            Options(response.HttpContext.Request));

    /// <summary>The user of a valid session cookie, or <c>null</c> (absent, expired, forged, or another kind of token).</summary>
    public Guid? Read(HttpRequest request) =>
        OAuthSessionToken.Validate(request.Cookies[Name], _jwtKey, _issuer);

    public CookieOptions Options(HttpRequest request) => new()
    {
        HttpOnly = true,
        // Same rule as the refresh cookie: always Secure outside Development, and with SameSite=None.
        Secure = RefreshTokenCookie.RequiresSecure(request, _sameSite),
        // Lax (default) is still sent on the top-level GET navigation from the OAuth client to
        // /connect/authorize; None is for the deployments whose front end and API are cross-site.
        SameSite = _sameSite,
        MaxAge = Lifetime,
        Path = CookiePath,
        IsEssential = true // strictly necessary to the authorization the user asked for — no consent needed
    };
}
