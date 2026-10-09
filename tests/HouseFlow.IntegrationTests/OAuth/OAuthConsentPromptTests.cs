using System.Net;
using FluentAssertions;
using static HouseFlow.IntegrationTests.OAuth.OAuthTestClient;

namespace HouseFlow.IntegrationTests.OAuth;

/// <summary>
/// When /connect/authorize shows the consent screen again rather than issuing a code on the
/// strength of an earlier consent — and the one way back from that screen without a loop: the
/// session cookie « Autoriser » sets, which names the client just consented to and is good for
/// one code.
/// </summary>
[Collection("Integration")]
public class OAuthConsentPromptTests
{
    private readonly OAuthTestClient _oauth;

    public OAuthConsentPromptTests(IntegrationTestFixture fixture) => _oauth = new OAuthTestClient(fixture);

    private static (string Url, string State) NewRequest(
        string clientId, string scope = BothScopes, string redirectUri = RedirectUri, string? extra = null)
    {
        var state = Guid.NewGuid().ToString("N");
        return (AuthorizeUrl(clientId, Pkce.Create(), state, scope, redirectUri, extra), state);
    }

    /// <summary>
    /// RFC 8252 §8.6: any process of the machine can present a loopback client's identity and listen
    /// for its code. A native client never gets one without the user, whatever it was granted before.
    /// </summary>
    [Fact]
    public async Task NativeClient_IsPromptedAtEveryAuthorization()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync(); // loopback redirect URI: native
        var session = await _oauth.OpenSessionAsync(user);

        var (url, state) = NewRequest(clientId);
        ReturnUrlOfFrontendRedirect(await _oauth.AuthorizeAsync(url, session), "consent");
        var consented = await _oauth.ConsentAsync(user, clientId, "houses:read", "houses:write");
        var first = await _oauth.AuthorizeAsync(url, consented);
        CodeOfClientRedirect(first, state);

        // That code spent the consent just given: the cookie the browser now holds no longer names it.
        var browser = SessionCookie(first);
        browser.Should().NotBeNullOrEmpty("the session cookie is re-issued without the consent");
        var (again, againState) = NewRequest(clientId);
        ReturnUrlOfFrontendRedirect(await _oauth.AuthorizeAsync(again, browser), "consent");
        ReturnUrlOfFrontendRedirect(await _oauth.AuthorizeAsync(again, session), "consent");
        ErrorOfClientRedirect(await _oauth.AuthorizeAsync(again + "&prompt=none", browser), againState)
            .Should().Be("consent_required");
        // Nothing in the URL — which the client writes — stands for the consent.
        ReturnUrlOfFrontendRedirect(await _oauth.AuthorizeAsync(again + "&consent_client=" + clientId, browser), "consent");

        // « Autoriser » again: the code.
        var consentedAgain = await _oauth.ConsentAsync(user, clientId, "houses:read", "houses:write");
        CodeOfClientRedirect(await _oauth.AuthorizeAsync(again, consentedAgain), againState);
    }

    [Fact]
    public async Task WebClient_WithCoveringConsent_IsNotPrompted()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync("Web app", WebRedirectUri);
        var session = await _oauth.OpenSessionAsync(user);
        (await _oauth.GrantAsync(user, clientId, "houses:read", "houses:write")).StatusCode.Should().Be(HttpStatusCode.Created);

        // A session cookie naming no consent just given: the earlier consent covers both requests.
        var (both, bothState) = NewRequest(clientId, redirectUri: WebRedirectUri);
        CodeOfClientRedirect(await _oauth.AuthorizeAsync(both, session), bothState, WebRedirectUri);
        var (readOnly, readOnlyState) = NewRequest(clientId, "houses:read", WebRedirectUri);
        CodeOfClientRedirect(await _oauth.AuthorizeAsync(readOnly, session), readOnlyState, WebRedirectUri);
    }

    /// <summary>
    /// A client asking for more than it was granted gets the consent screen — not a code silently
    /// reduced to the granted scopes — and a user who grants the same again gets that code, no loop.
    /// </summary>
    [Fact]
    public async Task RequestedScopeNotGranted_PromptsAgain()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync("Web app", WebRedirectUri);
        var session = await _oauth.OpenSessionAsync(user);
        (await _oauth.GrantAsync(user, clientId, "houses:read")).StatusCode.Should().Be(HttpStatusCode.Created);
        var pkce = Pkce.Create();
        var state = Guid.NewGuid().ToString("N");

        var returnUrl = ReturnUrlOfFrontendRedirect(
            await _oauth.AuthorizeAsync(AuthorizeUrl(clientId, pkce, state, BothScopes, WebRedirectUri), session), "consent");

        var consented = await _oauth.ConsentAsync(user, clientId, "houses:read");
        var code = CodeOfClientRedirect(await _oauth.AuthorizeAsync(new Uri(returnUrl).PathAndQuery, consented), state, WebRedirectUri);
        var tokens = await ReadTokensAsync(await _oauth.ExchangeCodeAsync(clientId, code, pkce.Verifier, WebRedirectUri));
        tokens.Scope.Split(' ').Should().Contain("houses:read").And.NotContain("houses:write");
    }

    [Fact]
    public async Task JustGivenConsent_NeverReplacesAMissingAuthorization()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();
        var consented = await _oauth.ConsentAsync(user, clientId, "houses:read", "houses:write");
        var id = (await _oauth.ListAuthorizationsAsync(user)).Single().GetProperty("id").GetString();
        (await _oauth.SendAsBearer(HttpMethod.Delete, $"/api/v1/oauth/authorizations/{id}", user.AccessToken)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        // Revoked in the meantime (another tab, « Applications connectées »): the cookie is not a consent.
        var (url, _) = NewRequest(clientId);
        ReturnUrlOfFrontendRedirect(await _oauth.AuthorizeAsync(url, consented), "consent");
    }

    [Fact]
    public async Task JustGivenConsent_CountsForThatClientOnly()
    {
        var user = await _oauth.RegisterUserAsync();
        var consentedClient = await _oauth.RegisterClientAsync("Consented");
        var otherClient = await _oauth.RegisterClientAsync("Other");
        (await _oauth.GrantAsync(user, otherClient, "houses:read", "houses:write")).StatusCode.Should().Be(HttpStatusCode.Created);
        var consented = await _oauth.ConsentAsync(user, consentedClient, "houses:read", "houses:write");

        var (url, _) = NewRequest(otherClient);

        ReturnUrlOfFrontendRedirect(await _oauth.AuthorizeAsync(url, consented), "consent");
    }
}
