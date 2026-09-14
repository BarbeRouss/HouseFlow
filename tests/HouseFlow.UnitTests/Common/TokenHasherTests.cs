using FluentAssertions;
using HouseFlow.Application.Common;

namespace HouseFlow.UnitTests.Common;

/// <summary>
/// RGPD Art. 32(1)(a) — les refresh tokens sont stockés hashés. Le hash doit être
/// déterministe (lookup indexé), irréversible et distinct pour deux tokens distincts.
/// </summary>
public class TokenHasherTests
{
    [Fact]
    public void Hash_ShouldBeDeterministic()
    {
        const string token = "n0t-a-real-token";

        TokenHasher.Hash(token).Should().Be(TokenHasher.Hash(token));
    }

    [Fact]
    public void Hash_ShouldDifferForDifferentTokens()
    {
        TokenHasher.Hash("token-a").Should().NotBe(TokenHasher.Hash("token-b"));
    }

    [Fact]
    public void Hash_ShouldNotContainThePlaintext()
    {
        const string token = "super-secret-refresh-token";

        TokenHasher.Hash(token).Should().NotContain(token);
    }

    [Fact]
    public void Hash_ShouldBeLowercaseHexSha256()
    {
        var hash = TokenHasher.Hash("whatever");

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Hash_ShouldMatchTheReferenceSha256Vector()
    {
        // Vecteur de test SHA-256 connu : garantit qu'on ne change pas d'algorithme
        // silencieusement (ce qui invaliderait tous les tokens en base).
        TokenHasher.Hash("abc").Should()
            .Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void Hash_ShouldRejectNull()
    {
        var act = () => TokenHasher.Hash(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
