using System.Net;
using FluentAssertions;
using static HouseFlow.IntegrationTests.OAuth.OAuthTestClient;

namespace HouseFlow.IntegrationTests.OAuth;

/// <summary>Everything the authorization server must refuse: no code, no token, no confusion of tokens.</summary>
[Collection("Integration")]
public class OAuthRefusalTests
{
    private readonly OAuthTestClient _oauth;

    public OAuthRefusalTests(IntegrationTestFixture fixture) => _oauth = new OAuthTestClient(fixture);

    /// <summary>A consented user, a client and a fresh session: the authorization endpoint would issue a code.</summary>
    private async Task<(TestUser User, string ClientId, string Session)> ConsentedAsync()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();
        var session = await _oauth.OpenSessionAsync(user);
        (await _oauth.GrantAsync(user, clientId, "houses:read", "houses:write")).StatusCode.Should().Be(HttpStatusCode.Created);
        return (user, clientId, session);
    }

    private static void ShouldIssueNoCode(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Redirect)
            LocationQuery(response).Should().ContainKey("error").And.NotContainKey("code");
        else
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WrongCodeVerifier_IsInvalidGrant()
    {
        var (_, clientId, session) = await ConsentedAsync();
        var state = Guid.NewGuid().ToString("N");
        var code = CodeOfClientRedirect(await _oauth.AuthorizeAsync(AuthorizeUrl(clientId, Pkce.Create(), state), session), state);

        var response = await _oauth.ExchangeCodeAsync(clientId, code, Pkce.Create().Verifier);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadOAuthErrorAsync(response)).Should().Be("invalid_grant");
    }

    [Fact]
    public async Task ReplayedCode_IsInvalidGrant()
    {
        var (_, clientId, session) = await ConsentedAsync();
        var pkce = Pkce.Create();
        var state = Guid.NewGuid().ToString("N");
        var code = CodeOfClientRedirect(await _oauth.AuthorizeAsync(AuthorizeUrl(clientId, pkce, state), session), state);
        (await _oauth.ExchangeCodeAsync(clientId, code, pkce.Verifier)).StatusCode.Should().Be(HttpStatusCode.OK);

        var replay = await _oauth.ExchangeCodeAsync(clientId, code, pkce.Verifier);

        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadOAuthErrorAsync(replay)).Should().Be("invalid_grant");
    }

    [Fact]
    public async Task CodeRedeemedByAnotherClient_IsInvalidGrant()
    {
        var (_, clientId, session) = await ConsentedAsync();
        var otherClient = await _oauth.RegisterClientAsync("Other");
        var pkce = Pkce.Create();
        var state = Guid.NewGuid().ToString("N");
        var code = CodeOfClientRedirect(await _oauth.AuthorizeAsync(AuthorizeUrl(clientId, pkce, state), session), state);

        var response = await _oauth.ExchangeCodeAsync(otherClient, code, pkce.Verifier);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadOAuthErrorAsync(response)).Should().Be("invalid_grant");
    }

    [Fact]
    public async Task UnregisteredRedirectUri_GetsNoCode()
    {
        var (_, clientId, session) = await ConsentedAsync();

        var response = await _oauth.AuthorizeAsync(
            AuthorizeUrl(clientId, Pkce.Create(), "s", redirectUri: "http://127.0.0.1:9/elsewhere"), session);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the user agent is never sent to an unregistered URI");
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task MissingCodeChallenge_GetsNoCode()
    {
        var (_, clientId, session) = await ConsentedAsync();

        ShouldIssueNoCode(await _oauth.AuthorizeAsync(AuthorizeUrl(clientId, pkce: null, "s"), session));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData(null)] // RFC 7636 §4.3: no method means plain
    public async Task NonS256CodeChallenge_GetsNoCode(string? method)
    {
        var (_, clientId, session) = await ConsentedAsync();
        var verifier = Pkce.Create().Verifier;
        var url = AuthorizeUrl(clientId, pkce: null, "s", extra: $"code_challenge={verifier}"
            + (method is null ? "" : $"&code_challenge_method={method}"));

        ShouldIssueNoCode(await _oauth.AuthorizeAsync(url, session));
    }

    [Fact]
    public async Task ForeignResource_IsInvalidTarget()
    {
        var (_, clientId, session) = await ConsentedAsync();
        var state = Guid.NewGuid().ToString("N");

        var response = await _oauth.AuthorizeAsync(
            AuthorizeUrl(clientId, Pkce.Create(), state, extra: "resource=" + Uri.EscapeDataString("https://evil.example/mcp")), session);

        ErrorOfClientRedirect(response, state).Should().Be("invalid_target");
    }

    [Fact]
    public async Task ForeignResource_IsInvalidTarget_EvenBeforeTheUserIsKnown()
    {
        var clientId = await _oauth.RegisterClientAsync();
        var state = Guid.NewGuid().ToString("N");

        var response = await _oauth.AuthorizeAsync(
            AuthorizeUrl(clientId, Pkce.Create(), state, extra: "resource=" + Uri.EscapeDataString("https://evil.example/mcp")));

        ErrorOfClientRedirect(response, state).Should().Be("invalid_target");
    }

    [Fact]
    public async Task ForeignResource_AtTheTokenEndpoint_IsInvalidTarget()
    {
        var (_, clientId, session) = await ConsentedAsync();
        var pkce = Pkce.Create();
        var state = Guid.NewGuid().ToString("N");
        var code = CodeOfClientRedirect(await _oauth.AuthorizeAsync(AuthorizeUrl(clientId, pkce, state), session), state);

        var response = await _oauth.Http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = pkce.Verifier,
            ["resource"] = "https://evil.example/mcp"
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadOAuthErrorAsync(response)).Should().Be("invalid_target");
    }

    [Fact]
    public async Task Denial_OnTheConsentScreen_IsAccessDeniedForTheClient()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();
        var session = await _oauth.OpenSessionAsync(user);
        var state = Guid.NewGuid().ToString("N");

        var response = await _oauth.AuthorizeAsync(
            AuthorizeUrl(clientId, Pkce.Create(), state, extra: "houseflow_consent=denied"), session);

        ErrorOfClientRedirect(response, state).Should().Be("access_denied");
    }

    [Fact]
    public async Task PromptNone_WithoutSessionOrConsent_ReturnsAnErrorInsteadOfAPage()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();
        var state = Guid.NewGuid().ToString("N");
        var url = AuthorizeUrl(clientId, Pkce.Create(), state, extra: "prompt=none");

        ErrorOfClientRedirect(await _oauth.AuthorizeAsync(url), state).Should().Be("login_required");
        ErrorOfClientRedirect(await _oauth.AuthorizeAsync(url, await _oauth.OpenSessionAsync(user)), state).Should().Be("consent_required");
    }

    [Fact]
    public async Task ApiAccessToken_InTheSessionCookie_IsTreatedAsNoSession()
    {
        var (user, clientId, _) = await ConsentedAsync();

        var response = await _oauth.AuthorizeAsync(AuthorizeUrl(clientId, Pkce.Create(), "s"), sessionCookie: user.AccessToken);

        ReturnUrlOfFrontendRedirect(response, "authorize");
    }

    [Fact]
    public async Task ForgedSessionCookie_IsTreatedAsNoSession()
    {
        var (_, clientId, session) = await ConsentedAsync();
        var parts = session.Split('.');
        var forged = $"{parts[0]}.{parts[1]}.{Pkce.Base64Url(new byte[32])}";

        var response = await _oauth.AuthorizeAsync(AuthorizeUrl(clientId, Pkce.Create(), "s"), forged);

        ReturnUrlOfFrontendRedirect(response, "authorize");
    }

    [Fact]
    public async Task SessionCookie_AsBearerOnTheApi_IsUnauthorized()
    {
        var user = await _oauth.RegisterUserAsync();
        var session = await _oauth.OpenSessionAsync(user);

        (await _oauth.SendAsBearer(HttpMethod.Get, "/api/v1/houses", session)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _oauth.SendAsBearer(HttpMethod.Post, "/api/v1/oauth/session", session)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OAuthEndpoints_RefuseApiKeys()
    {
        var user = await _oauth.RegisterUserAsync();
        var created = await _oauth.Http.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/api-keys")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { name = "integration", scope = "ReadWrite" }),
            Headers = { Authorization = new("Bearer", user.AccessToken) }
        });
        created.EnsureSuccessStatusCode();
        using var json = System.Text.Json.JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var apiKey = json.RootElement.GetProperty("key").GetString()!;

        (await _oauth.SendAsBearer(HttpMethod.Post, "/api/v1/oauth/session", apiKey)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "granting a third party access is a personal act, never an API key's");
        (await _oauth.SendAsBearer(HttpMethod.Get, "/api/v1/oauth/authorizations", apiKey)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OAuthAccessToken_IsRefusedByTheApiAndTheConsentEndpoints()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();
        var tokens = await _oauth.ConnectAsync(user, clientId);

        (await _oauth.SendAsBearer(HttpMethod.Get, "/api/v1/users/me", tokens.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _oauth.SendAsBearer(HttpMethod.Get, "/api/v1/oauth/authorizations", tokens.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
