using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using HouseFlow.Application.OAuth;
using HouseFlow.Application.Services;
using HouseFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace HouseFlow.UnitTests.OAuth;

/// <summary>
/// The <c>oauthSession</c> cookie value: valid only as what it is — never an API access token in
/// disguise, never past its lifetime, never under another key or issuer.
/// </summary>
public class OAuthSessionTokenTests
{
    private const string Key = "TestSecretKeyForJWTTokenGeneration123456TestSecretKeyForJWTTokenGeneration123456";
    private const string Issuer = "TestIssuer";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    [Fact]
    public void Valid_RoundTripsTheUser()
    {
        var userId = Guid.NewGuid();

        var token = OAuthSessionToken.Create(userId, Key, Issuer, DateTime.UtcNow, Lifetime);

        OAuthSessionToken.Validate(token, Key, Issuer).Should().Be(new OAuthSession(userId, ConsentedClientId: null));
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .Should().NotContain(c => c.Type == OAuthSessionToken.ConsentedClientClaim, "no consent was just given");
    }

    [Fact]
    public void ConsentJustGiven_RoundTripsTheClient()
    {
        var userId = Guid.NewGuid();

        var token = OAuthSessionToken.Create(userId, Key, Issuer, DateTime.UtcNow, Lifetime, consentedClientId: "0123456789abcdef0123456789abcdef");

        OAuthSessionToken.Validate(token, Key, Issuer).Should().Be(new OAuthSession(userId, "0123456789abcdef0123456789abcdef"));
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .Should().ContainSingle(c => c.Type == OAuthSessionToken.ConsentedClientClaim)
            .Which.Value.Should().Be("0123456789abcdef0123456789abcdef");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoConsentedClient_CarriesNoClaim(string? consentedClientId)
    {
        var token = OAuthSessionToken.Create(Guid.NewGuid(), Key, Issuer, DateTime.UtcNow, Lifetime, consentedClientId);

        OAuthSessionToken.Validate(token, Key, Issuer)!.ConsentedClientId.Should().BeNull();
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.Should().NotContain(c => c.Type == OAuthSessionToken.ConsentedClientClaim);
    }

    [Fact]
    public void Token_CarriesItsOwnAudienceAndPurpose_AndExpiresAfterTheLifetime()
    {
        var issuedAt = DateTime.UtcNow;

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(
            OAuthSessionToken.Create(Guid.NewGuid(), Key, Issuer, issuedAt, Lifetime));

        jwt.Audiences.Should().Equal(OAuthSessionToken.Audience);
        jwt.Issuer.Should().Be(Issuer);
        jwt.Header.Alg.Should().Be(SecurityAlgorithms.HmacSha256);
        jwt.Claims.Should().Contain(c => c.Type == OAuthSessionToken.PurposeClaim && c.Value == OAuthSessionToken.Purpose);
        jwt.ValidTo.Should().BeCloseTo(issuedAt + Lifetime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Expired_IsRefused()
    {
        var token = OAuthSessionToken.Create(Guid.NewGuid(), Key, Issuer, DateTime.UtcNow.AddMinutes(-11), Lifetime);

        OAuthSessionToken.Validate(token, Key, Issuer).Should().BeNull("no clock skew beyond the 10 minutes");
    }

    [Fact]
    public void ApiAccessToken_IsRefused()
    {
        // The JWT the API hands to the front end: same key and issuer, but the API's audience.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = Key,
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = "TestAudience"
        }).Build();
        using var context = new HouseFlowDbContext(new DbContextOptionsBuilder<HouseFlowDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var accessToken = new AuthService(context, configuration, NullLogger<AuthService>.Instance)
            .GenerateJwtToken(Guid.NewGuid(), "user@example.com");

        OAuthSessionToken.Validate(accessToken, Key, Issuer).Should().BeNull();
    }

    /// <summary>A token of the API's audience is refused even when it names a consent: only the session audience counts.</summary>
    [Fact]
    public void ConsentClaimUnderTheApiAudience_IsRefused()
    {
        var token = Sign(new JwtSecurityToken(Issuer, "TestAudience",
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim(OAuthSessionToken.PurposeClaim, OAuthSessionToken.Purpose),
                new Claim(OAuthSessionToken.ConsentedClientClaim, "0123456789abcdef0123456789abcdef")
            ],
            DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5)), Key);

        OAuthSessionToken.Validate(token, Key, Issuer).Should().BeNull();
    }

    [Fact]
    public void RightAudienceWithoutThePurpose_IsRefused()
    {
        var token = Sign(new JwtSecurityToken(Issuer, OAuthSessionToken.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5)), Key);

        OAuthSessionToken.Validate(token, Key, Issuer).Should().BeNull();
    }

    [Fact]
    public void OtherKey_IsRefused()
    {
        var token = OAuthSessionToken.Create(Guid.NewGuid(), Key + "-other", Issuer, DateTime.UtcNow, Lifetime);

        OAuthSessionToken.Validate(token, Key, Issuer).Should().BeNull();
    }

    [Fact]
    public void OtherIssuer_IsRefused()
    {
        var token = OAuthSessionToken.Create(Guid.NewGuid(), Key, "SomeoneElse", DateTime.UtcNow, Lifetime);

        OAuthSessionToken.Validate(token, Key, Issuer).Should().BeNull();
    }

    [Fact]
    public void UnsignedToken_IsRefused()
    {
        var payload = new JwtPayload(Issuer, OAuthSessionToken.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()), new Claim(OAuthSessionToken.PurposeClaim, OAuthSessionToken.Purpose)],
            DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5));
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(new JwtHeader(), payload));

        OAuthSessionToken.Validate(token, Key, Issuer).Should().BeNull("alg=none");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    public void Garbage_IsRefused(string? token) =>
        OAuthSessionToken.Validate(token, Key, Issuer).Should().BeNull();

    [Fact]
    public void OversizedValue_IsRefused() =>
        OAuthSessionToken.Validate(new string('a', 10_000), Key, Issuer).Should().BeNull();

    private static string Sign(JwtSecurityToken unsigned, string key)
    {
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(unsigned.Issuer, unsigned.Audiences.Single(), unsigned.Claims
            .Where(c => c.Type is not (JwtRegisteredClaimNames.Aud or JwtRegisteredClaimNames.Iss or JwtRegisteredClaimNames.Exp or JwtRegisteredClaimNames.Nbf)),
            unsigned.ValidFrom, unsigned.ValidTo, credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
