using FluentAssertions;
using HouseFlow.API.OAuth;

namespace HouseFlow.IntegrationTests.OAuth;

/// <summary>Pure rules of the OAuth configuration: where the front end is, which resources tokens may target.</summary>
public class OAuthConfigurationTests
{
    [Theory]
    [InlineData("https://app.houseflow.test", "https://other.test", "https://app.houseflow.test")]
    [InlineData("https://app.houseflow.test/", null, "https://app.houseflow.test")]
    [InlineData(null, "http://localhost:3000,http://localhost:3000,http://127.0.0.1:3000", "http://localhost:3000")]
    [InlineData(null, " https://www.houseflow.app , https://preview.houseflow.app", "https://www.houseflow.app")]
    [InlineData("", "*,https://www.houseflow.app", "https://www.houseflow.app")]
    [InlineData(null, "*", OAuthOptions.DefaultWebBaseUrl)]
    [InlineData(null, null, OAuthOptions.DefaultWebBaseUrl)]
    public void WebBaseUrl_IsTheSettingElseTheFirstCorsOriginElseLocalhost(string? configured, string? corsOrigins, string expected) =>
        OAuthOptions.ResolveWebBaseUrl(configured, corsOrigins).Should().Be(expected);

    [Theory]
    [InlineData("https://app.houseflow.test", true)]
    [InlineData("http://localhost:3000", true)]
    [InlineData("app.houseflow.test", false)]
    [InlineData("javascript:alert(1)", false)]
    public void WebBaseUrl_MustBeAnAbsoluteHttpUri(string webBaseUrl, bool valid) =>
        new OAuthOptions { WebBaseUrl = webBaseUrl }.HasValidUris().Should().Be(valid);

    [Fact]
    public void Resources_MustBeAbsoluteHttpUrisWithoutFragment()
    {
        new OAuthOptions { WebBaseUrl = "https://a.test", Resources = ["https://api.test/mcp"] }.HasValidUris().Should().BeTrue();
        new OAuthOptions { WebBaseUrl = "https://a.test", Resources = ["https://api.test/mcp#x"] }.HasValidUris().Should().BeFalse();
        new OAuthOptions { WebBaseUrl = "https://a.test", Resources = ["urn:mcp"] }.HasValidUris().Should().BeFalse();
    }

    private static readonly string[] Allowed = ["https://api.houseflow.app/mcp"];

    [Fact]
    public void NoResourceRequested_IsEveryAllowedResource() =>
        OAuthResources.Resolve([], Allowed).Should().Equal(Allowed);

    [Theory]
    [InlineData("https://api.houseflow.app/mcp")]
    [InlineData("HTTPS://API.HOUSEFLOW.APP/mcp")]
    [InlineData("https://api.houseflow.app:443/mcp")]
    public void AllowedResource_IsAccepted_InItsCanonicalForm(string requested) =>
        OAuthResources.Resolve([requested], Allowed).Should().Equal(Allowed);

    [Theory]
    [InlineData("https://evil.example/mcp")]
    [InlineData("https://api.houseflow.app/mcp/")]
    [InlineData("https://api.houseflow.app/api/v1")]
    [InlineData("not a uri")]
    public void OtherResource_IsInvalidTarget(string requested) =>
        OAuthResources.Resolve([requested], Allowed).Should().BeNull();

    [Fact]
    public void TokenEndpoint_NeverWidensWhatTheGrantCovered()
    {
        string[] allowed = ["https://a.test/mcp", "https://b.test/mcp"];

        OAuthResources.Resolve([], allowed, granted: ["https://a.test/mcp"]).Should().Equal("https://a.test/mcp");
        OAuthResources.Resolve(["https://b.test/mcp"], allowed, granted: ["https://a.test/mcp"]).Should().BeNull();
        OAuthResources.Resolve([], allowed, granted: ["https://gone.test/mcp"]).Should().BeNull("no longer served");
    }
}
