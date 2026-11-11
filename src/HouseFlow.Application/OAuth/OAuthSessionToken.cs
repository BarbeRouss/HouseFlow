using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace HouseFlow.Application.OAuth;

/// <summary>
/// What an <c>oauthSession</c> cookie says: the user, and the OAuth client they have just consented
/// to on the consent screen (<c>null</c> otherwise).
/// </summary>
public sealed record OAuthSession(Guid UserId, string? ConsentedClientId);

/// <summary>
/// Value of the <c>oauthSession</c> cookie that identifies the user on <c>/connect/authorize</c>: a
/// short-lived JWT (HS256, <c>Jwt:Key</c>, issuer <c>Jwt:Issuer</c>) whose audience is distinct from
/// the API's access tokens. Neither token is accepted in place of the other: the API's JWT bearer
/// handler expects <c>Jwt:Audience</c>, and <see cref="Validate"/> expects <see cref="Audience"/>
/// plus the <see cref="PurposeClaim"/> marker.
/// </summary>
public static class OAuthSessionToken
{
    public const string Audience = "HouseFlowOAuthSession";
    public const string PurposeClaim = "purpose";
    public const string Purpose = "oauth_session";

    /// <summary>
    /// The client the user has just consented to (<c>POST /api/v1/oauth/authorizations</c>): the
    /// only thing that lets <c>/connect/authorize</c> issue a code without asking again where it
    /// otherwise would. Signed, so the client — which controls the authorization URL — cannot forge it.
    /// </summary>
    public const string ConsentedClientClaim = "consent_client";

    /// <summary>Far above a real token (~300 bytes): a larger cookie is refused before any parsing.</summary>
    private const int MaxTokenLength = 4096;

    public static string Create(Guid userId, string jwtKey, string issuer, DateTime issuedAtUtc, TimeSpan lifetime,
        string? consentedClientId = null)
    {
        List<Claim> claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(PurposeClaim, Purpose),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        ];
        if (!string.IsNullOrEmpty(consentedClientId))
            claims.Add(new Claim(ConsentedClientClaim, consentedClientId));

        var credentials = new SigningCredentials(SigningKey(jwtKey), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: Audience,
            claims: claims,
            notBefore: issuedAtUtc,
            expires: issuedAtUtc + lifetime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>The session the token was issued for, or <c>null</c> if it is not a valid, unexpired session token.</summary>
    public static OAuthSession? Validate(string? token, string jwtKey, string issuer)
    {
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength) return null;

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = SigningKey(jwtKey),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            // Same as the API's bearer tokens: no tolerance beyond the stated lifetime.
            ClockSkew = TimeSpan.Zero
        };

        try
        {
            var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }
                .ValidateToken(token, parameters, out _);

            if (principal.FindFirst(PurposeClaim)?.Value != Purpose) return null;
            if (!Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId)) return null;

            var consentedClientId = principal.FindFirst(ConsentedClientClaim)?.Value;
            return new OAuthSession(userId, string.IsNullOrEmpty(consentedClientId) ? null : consentedClientId);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }

    private static SymmetricSecurityKey SigningKey(string jwtKey) => new(Encoding.UTF8.GetBytes(jwtKey));
}
