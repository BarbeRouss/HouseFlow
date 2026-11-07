using System.Text;
using FluentAssertions;
using HouseFlow.Application.OAuth;

namespace HouseFlow.UnitTests.OAuth;

/// <summary>
/// The OAuth server keys must be the same on every replica and after every restart (tokens stay
/// valid), distinct per purpose, and never the JWT key itself.
/// </summary>
public class OAuthKeyDerivationTests
{
    private const string JwtKey = "TestSecretKeyForJWTTokenGeneration123456TestSecretKeyForJWTTokenGeneration123456";

    [Fact]
    public void Keys_AreDeterministic() =>
        (OAuthKeyDerivation.SigningKey(JwtKey), OAuthKeyDerivation.EncryptionKey(JwtKey))
            .Should().BeEquivalentTo((OAuthKeyDerivation.SigningKey(JwtKey), OAuthKeyDerivation.EncryptionKey(JwtKey)));

    [Fact]
    public void Keys_HaveTheSizeTheirAlgorithmNeeds()
    {
        OAuthKeyDerivation.SigningKey(JwtKey).Should().HaveCount(64, "HS512");
        OAuthKeyDerivation.EncryptionKey(JwtKey).Should().HaveCount(32, "A256KW");
    }

    [Fact]
    public void Keys_AreDistinctPerPurpose_AndFromTheJwtKey()
    {
        var signing = OAuthKeyDerivation.SigningKey(JwtKey);
        var encryption = OAuthKeyDerivation.EncryptionKey(JwtKey);
        var raw = Encoding.UTF8.GetBytes(JwtKey);

        signing.Take(32).Should().NotEqual(encryption);
        signing.Should().NotEqual(raw.Take(64));
        encryption.Should().NotEqual(raw.Take(32));
    }

    [Fact]
    public void Keys_FollowTheJwtKey() =>
        OAuthKeyDerivation.SigningKey(JwtKey + "x").Should().NotEqual(OAuthKeyDerivation.SigningKey(JwtKey));
}
