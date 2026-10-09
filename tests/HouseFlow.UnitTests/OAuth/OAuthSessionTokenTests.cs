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

        OAuthSessionToken.Validate(token, Key, Issuer).Should().Be(userId);
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
