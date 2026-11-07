using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace HouseFlow.IntegrationTests.OAuth;

/// <summary>RFC 8414 metadata: what an MCP client discovers before registering.</summary>
[Collection("Integration")]
public class OAuthMetadataTests
{
    private readonly IntegrationTestFixture _fixture;

    public OAuthMetadataTests(IntegrationTestFixture fixture) => _fixture = fixture;

    private static string[] Strings(JsonElement root, string name) =>
        [.. root.GetProperty(name).EnumerateArray().Select(e => e.GetString()!)];

    [Theory]
    [InlineData("/.well-known/oauth-authorization-server")]
    [InlineData("/.well-known/openid-configuration")]
    public async Task Metadata_DescribesTheAuthorizationCodeFlowWithPkceAndRegistration(string path)
    {
        var client = _fixture.CreateApiClient();
        var origin = client.BaseAddress!.GetLeftPart(UriPartial.Authority);

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        root.GetProperty("issuer").GetString().Should().Be(origin + "/");
        root.GetProperty("authorization_endpoint").GetString().Should().Be(origin + "/connect/authorize");
        root.GetProperty("token_endpoint").GetString().Should().Be(origin + "/connect/token");
        root.GetProperty("registration_endpoint").GetString().Should().Be(origin + "/connect/register");
        root.GetProperty("revocation_endpoint").GetString().Should().Be(origin + "/connect/revocation");
        root.GetProperty("userinfo_endpoint").GetString().Should().Be(origin + "/connect/userinfo");

        Strings(root, "code_challenge_methods_supported").Should().Equal("S256");
        Strings(root, "grant_types_supported").Should().BeEquivalentTo("authorization_code", "refresh_token");
        Strings(root, "grant_types_supported").Should().NotContain(["implicit", "password", "client_credentials"]);
        Strings(root, "response_types_supported").Should().Equal("code");
        Strings(root, "scopes_supported").Should().Contain(["houses:read", "houses:write"]);
        Strings(root, "token_endpoint_auth_methods_supported").Should().Equal("none");
    }

    [Fact]
    public async Task BothWellKnownNames_ServeTheSameDocument()
    {
        var client = _fixture.CreateApiClient();

        var rfc8414 = await client.GetStringAsync("/.well-known/oauth-authorization-server");
        var oidc = await client.GetStringAsync("/.well-known/openid-configuration");

        oidc.Should().Be(rfc8414);
    }
}
