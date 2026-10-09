using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace HouseFlow.IntegrationTests.OAuth;

/// <summary>RFC 7591 dynamic client registration: anonymous, public clients only, safe redirect URIs.</summary>
[Collection("Integration")]
public class OAuthClientRegistrationTests
{
    private readonly OAuthTestClient _oauth;

    public OAuthClientRegistrationTests(IntegrationTestFixture fixture) => _oauth = new OAuthTestClient(fixture);

    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback")]
    [InlineData("http://127.0.0.1:9/cb")]
    [InlineData("http://localhost:3000/cb")]
    [InlineData("http://[::1]:8080/cb")]
    public async Task ValidRegistration_Returns201_APublicClientWithoutSecret(string redirectUri)
    {
        var response = await _oauth.RegisterClientRawAsync(new
        {
            client_name = "Claude",
            redirect_uris = new[] { redirectUri },
            client_uri = "https://claude.ai"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        root.TryGetProperty("client_secret", out _).Should().BeFalse();
        root.GetProperty("client_id").GetString().Should().MatchRegex("^[0-9a-f]{32}$", "128 random bits, no hf_ prefix");
        root.GetProperty("client_id_issued_at").GetInt64().Should()
            .BeCloseTo(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 60);
        root.GetProperty("client_name").GetString().Should().Be("Claude");
        root.GetProperty("redirect_uris").EnumerateArray().Select(e => e.GetString()).Should().Equal(redirectUri);
        root.GetProperty("token_endpoint_auth_method").GetString().Should().Be("none");
        root.GetProperty("grant_types").EnumerateArray().Select(e => e.GetString()).Should().Equal("authorization_code", "refresh_token");
        root.GetProperty("response_types").EnumerateArray().Select(e => e.GetString()).Should().Equal("code");
        root.GetProperty("scope").GetString().Should().Be("houses:read houses:write");
        root.GetProperty("client_uri").GetString().Should().Be("https://claude.ai");
    }

    [Fact]
    public async Task RegisteredClient_IsDescribedForTheConsentScreen_WithItsRegisteredScopesOnly()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync("Read-only tool", "https://tool.example/callback", scope: "houses:read");

        var response = await _oauth.SendAsBearer(HttpMethod.Get, $"/api/v1/oauth/clients/{clientId}", user.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("clientName").GetString().Should().Be("Read-only tool");
        json.RootElement.GetProperty("redirectHosts").EnumerateArray().Select(e => e.GetString()).Should().Equal("tool.example");
        json.RootElement.GetProperty("scopes").EnumerateArray().Select(e => e.GetString()).Should().Equal("houses:read");
    }

    /// <summary>
    /// A native client (Claude Code, the E2E suite) listens on an ephemeral loopback port: any port
    /// registers, the consent screen shows it as host:port, and the code is delivered there.
    /// </summary>
    [Fact]
    public async Task LoopbackRedirectUri_OnAnEphemeralPort_WorksEndToEnd()
    {
        var port = Random.Shared.Next(49152, 65536);
        var redirectUri = $"http://127.0.0.1:{port}/callback";
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync("Ephemeral", redirectUri);

        var info = await _oauth.SendAsBearer(HttpMethod.Get, $"/api/v1/oauth/clients/{clientId}", user.AccessToken);
        using (var json = JsonDocument.Parse(await info.Content.ReadAsStringAsync()))
            json.RootElement.GetProperty("redirectHosts").EnumerateArray().Select(e => e.GetString()).Should().Equal($"127.0.0.1:{port}");

        var session = await _oauth.OpenSessionAsync(user);
        var pkce = Pkce.Create();
        var state = Guid.NewGuid().ToString("N");
        var url = OAuthTestClient.AuthorizeUrl(clientId, pkce, state, redirectUri: redirectUri);
        OAuthTestClient.ReturnUrlOfFrontendRedirect(await _oauth.AuthorizeAsync(url, session), "consent");
        var consented = await _oauth.ConsentAsync(user, clientId, "houses:read");
        var first = await _oauth.AuthorizeAsync(url, consented);
        var code = OAuthTestClient.CodeOfClientRedirect(first, state, redirectUri);
        (await _oauth.ExchangeCodeAsync(clientId, code, pkce.Verifier, redirectUri)).StatusCode.Should().Be(HttpStatusCode.OK);

        // The second round goes through the consent screen again, as every round of a native client.
        OAuthTestClient.ReturnUrlOfFrontendRedirect(await _oauth.AuthorizeAsync(
            OAuthTestClient.AuthorizeUrl(clientId, Pkce.Create(), state, redirectUri: redirectUri),
            OAuthTestClient.SessionCookie(first) ?? consented), "consent");
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { """{ "client_name": "X", "redirect_uris": ["http://example.com/cb"] }""", "invalid_redirect_uri" },
        { """{ "client_name": "X", "redirect_uris": ["myapp://cb"] }""", "invalid_redirect_uri" },
        { """{ "client_name": "X", "redirect_uris": ["https://claude.ai/cb#fragment"] }""", "invalid_redirect_uri" },
        { """{ "client_name": "X" }""", "invalid_redirect_uri" },
        { """{ "client_name": "X", "redirect_uris": [] }""", "invalid_redirect_uri" },
        { """{ "client_name": "X", "redirect_uris": ["https://claude.ai/cb"], "token_endpoint_auth_method": "client_secret_basic" }""", "invalid_client_metadata" },
        { """{ "client_name": "X", "redirect_uris": ["https://claude.ai/cb"], "grant_types": ["authorization_code", "implicit"] }""", "invalid_client_metadata" },
        { """{ "client_name": "X", "redirect_uris": ["https://claude.ai/cb"], "response_types": ["token"] }""", "invalid_client_metadata" },
        { """{ "client_name": "X", "redirect_uris": ["https://claude.ai/cb"], "scope": "houses:read admin" }""", "invalid_client_metadata" },
        { """{ "redirect_uris": ["https://claude.ai/cb"] }""", "invalid_client_metadata" },
        { """{ "client_name": "X", "redirect_uris": "https://claude.ai/cb" }""", "invalid_client_metadata" },
        { """not json""", "invalid_client_metadata" }
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task InvalidRegistration_Returns400_WithTheRfc7591Error(string body, string expectedError)
    {
        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await _oauth.Http.PostAsync("/connect/register", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("error").GetString().Should().Be(expectedError);
        json.RootElement.GetProperty("error_description").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
