using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace HouseFlow.Application.OAuth;

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

    /// <summary>Far above a real token (~300 bytes): a larger cookie is refused before any parsing.</summary>
    private const int MaxTokenLength = 4096;

    public static string Create(Guid userId, string jwtKey, string issuer, DateTime issuedAtUtc, TimeSpan lifetime)
    {
        var credentials = new SigningCredentials(SigningKey(jwtKey), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(PurposeClaim, Purpose),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ],
            notBefore: issuedAtUtc,
            expires: issuedAtUtc + lifetime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>The user the token was issued to, or <c>null</c> if it is not a valid, unexpired session token.</summary>
    public static Guid? Validate(string? token, string jwtKey, string issuer)
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
            return Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId) ? userId : null;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }

    private static SymmetricSecurityKey SigningKey(string jwtKey) => new(Encoding.UTF8.GetBytes(jwtKey));
}
