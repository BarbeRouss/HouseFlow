using FluentAssertions;
using HouseFlow.Application.OAuth;

namespace HouseFlow.UnitTests.OAuth;

/// <summary>Which <c>client_id</c> HouseFlow agrees to download a metadata document from (#308).</summary>
public class ClientIdMetadataUrlTests
{
    [Theory]
    [InlineData("https://claude.ai/.well-known/oauth-client-metadata")]
    [InlineData("https://claude.ai/client.json")]
    [InlineData("HTTPS://Claude.AI/client.json")]
    [InlineData("https://claude.ai:443/client.json")]
    [InlineData("https://claude.ai/client.json?tenant=a")]
    [InlineData("https://xn--mnchen-3ya.de/client.json")]
    public void Valid(string value)
    {
        ClientIdMetadataUrl.TryParse(value, out var url).Should().BeTrue();
        url.Should().NotBeNull();
    }

    [Fact]
    public void Valid_UpToTheColumnSize()
    {
        var path = "https://claude.ai/" + new string('a', ClientIdMetadataUrl.MaxLength - "https://claude.ai/".Length);
        ClientIdMetadataUrl.TryParse(path, out _).Should().BeTrue();
        ClientIdMetadataUrl.TryParse(path + "a", out _).Should().BeFalse("the ClientId column is varchar(100)");
    }

    [Theory]
    [InlineData(null, "missing")]
    [InlineData("", "empty")]
    [InlineData("0123456789abcdef0123456789abcdef", "a DCR client id")]
    [InlineData("http://claude.ai/client.json", "plain http")]
    [InlineData("https://claude.ai:8443/client.json", "explicit port other than 443")]
    [InlineData("https://claude.ai/", "empty path")]
    [InlineData("https://claude.ai", "no path")]
    [InlineData("https://claude.ai/client.json#frag", "fragment")]
    [InlineData("https://claude.ai/client.json#", "empty fragment")]
    [InlineData("https://user:pass@claude.ai/client.json", "user info")]
    [InlineData("https://user@claude.ai/client.json", "user name")]
    [InlineData("https://127.0.0.1/client.json", "IPv4 literal")]
    [InlineData("https://[::1]/client.json", "IPv6 literal")]
    [InlineData("https://169.254.169.254/latest/meta-data", "metadata service literal")]
    [InlineData("https://claude.ai/a/../client.json", "dot-dot segment")]
    [InlineData("https://claude.ai/a/./client.json", "dot segment")]
    [InlineData("https://claude.ai/a/%2e%2e/client.json", "encoded dot-dot segment")]
    [InlineData("https://claude.ai/client json", "white space")]
    [InlineData("https://claude.ai/client.json\n", "control character")]
    [InlineData("https://\u0441laude.ai/client.json", "Cyrillic letter imitating a Latin one")]
    [InlineData("https://claude.ai/caf\u00e9", "non-ASCII path")]
    [InlineData("https:claude.ai/client.json", "authority not spelled out")]
    [InlineData("ftp://claude.ai/client.json", "other scheme")]
    [InlineData("javascript://claude.ai/client.json", "script")]
    public void Refused(string? value, string reason) =>
        ClientIdMetadataUrl.TryParse(value, out _).Should().BeFalse(reason);

    [Theory]
    [InlineData("https://claude.ai/client.json", true)]
    [InlineData("http://claude.ai/client.json", true)]
    [InlineData("ftp://x/y", true)]
    [InlineData("0123456789abcdef0123456789abcdef", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeUrl_IsWhatLeavesTheDynamicRegistrationPath(string? clientId, bool expected) =>
        ClientIdMetadataUrl.LooksLikeUrl(clientId).Should().Be(expected);

    [Fact]
    public void Origin_IsTheHostTheConsentScreenShows()
    {
        ClientIdMetadataUrl.TryParse("https://Claude.AI/client.json", out var url).Should().BeTrue();
        ClientIdMetadataUrl.Origin(url!).Should().Be("claude.ai");
    }
}
