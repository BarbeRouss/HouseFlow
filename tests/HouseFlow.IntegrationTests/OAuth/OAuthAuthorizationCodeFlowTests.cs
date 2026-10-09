using System.Net;
using System.Text.Json;
using FluentAssertions;
using HouseFlow.Application.OAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.EntityFrameworkCore.Models;
using static HouseFlow.IntegrationTests.OAuth.OAuthTestClient;

namespace HouseFlow.IntegrationTests.OAuth;

/// <summary>
/// The authorization code flow with PKCE as Claude runs it: unauthenticated browser sent to the
/// front end, session cookie, consent, code, token exchange, refresh with rotation, and silent
/// re-authorization once consented.
/// </summary>
[Collection("Integration")]
public class OAuthAuthorizationCodeFlowTests
{
    private readonly IntegrationTestFixture _fixture;
    private readonly OAuthTestClient _oauth;

    public OAuthAuthorizationCodeFlowTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _oauth = new OAuthTestClient(fixture);
    }

    [Fact]
    public async Task FullFlow_LoginConsentCodeTokenRefresh_ThenSilentReauthorization()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync("Claude Test");
        var pkce = Pkce.Create();
        var state = Guid.NewGuid().ToString("N");
        var authorizeUrl = AuthorizeUrl(clientId, pkce, state);

        // (a) No session yet: the browser goes to the front end's login step, which comes back here.
        var anonymous = await _oauth.AuthorizeAsync(authorizeUrl);
        ReturnUrlOfFrontendRedirect(anonymous, "authorize").Should().Be(anonymous.RequestMessage!.RequestUri!.AbsoluteUri);

        // (b) The logged-in front end opens the OAuth session.
        var sessionResponse = await _oauth.SendAsBearer(HttpMethod.Post, "/api/v1/oauth/session", user.AccessToken);
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var setCookie = SessionCookieHeader(sessionResponse)!;
        setCookie.Should().ContainEquivalentOf("httponly")
            .And.ContainEquivalentOf("path=/connect");
        setCookie.Should().NotContainEquivalentOf("max-age", "a session cookie: the 10-minute token expiry bounds it")
            .And.NotContainEquivalentOf("expires");
        var session = SessionCookie(sessionResponse)!;

        // (c) Identified, but no consent yet: the consent screen, which comes back here too.
        var needsConsent = await _oauth.AuthorizeAsync(authorizeUrl, session);
        ReturnUrlOfFrontendRedirect(needsConsent, "consent").Should().Be(needsConsent.RequestMessage!.RequestUri!.AbsoluteUri);

        // (d) What the consent screen shows.
        var clientInfo = await _oauth.SendAsBearer(HttpMethod.Get, $"/api/v1/oauth/clients/{clientId}", user.AccessToken);
        clientInfo.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var json = JsonDocument.Parse(await clientInfo.Content.ReadAsStringAsync()))
        {
            json.RootElement.GetProperty("clientId").GetString().Should().Be(clientId);
            json.RootElement.GetProperty("clientName").GetString().Should().Be("Claude Test");
            json.RootElement.GetProperty("redirectHosts").EnumerateArray().Select(e => e.GetString()).Should().Equal("127.0.0.1:9");
            json.RootElement.GetProperty("scopes").EnumerateArray().Select(e => e.GetString()).Should().Equal("houses:read", "houses:write");
        }

        // (e) « Autoriser »: consent recorded, session cookie refreshed.
        var grant = await _oauth.GrantAsync(user, clientId, "houses:read", "houses:write");
        grant.StatusCode.Should().Be(HttpStatusCode.Created);
        SessionCookie(grant).Should().NotBeNullOrEmpty();
        using (var json = JsonDocument.Parse(await grant.Content.ReadAsStringAsync()))
        {
            json.RootElement.GetProperty("clientId").GetString().Should().Be(clientId);
            json.RootElement.GetProperty("clientName").GetString().Should().Be("Claude Test");
            json.RootElement.GetProperty("redirectHosts").EnumerateArray().Select(e => e.GetString()).Should().Equal("127.0.0.1:9");
            json.RootElement.GetProperty("scopes").EnumerateArray().Select(e => e.GetString()).Should().Equal("houses:read", "houses:write");
        }

        // (f) Back on the authorization endpoint: the code goes to the client, with its state.
        var code = CodeOfClientRedirect(await _oauth.AuthorizeAsync(authorizeUrl, session), state);

        // (g) Code + PKCE verifier → tokens.
        var tokens = await ReadTokensAsync(await _oauth.ExchangeCodeAsync(clientId, code, pkce.Verifier));
        tokens.TokenType.Should().Be("Bearer");
        tokens.ExpiresIn.Should().BeInRange(1, 900);
        tokens.Scope.Split(' ').Should().Contain(["houses:read", "houses:write"]);

        // (h) userinfo: the access token speaks for the user.
        var userInfo = await _oauth.SendAsBearer(HttpMethod.Get, "/connect/userinfo", tokens.AccessToken);
        userInfo.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var json = JsonDocument.Parse(await userInfo.Content.ReadAsStringAsync()))
        {
            json.RootElement.GetProperty("sub").GetString().Should().Be(user.Id.ToString());
            json.RootElement.GetProperty("scope").GetString().Should().Be("houses:read houses:write");
        }

        // (i) …but not for the REST API: its audience is the MCP server.
        (await _oauth.SendAsBearer(HttpMethod.Get, "/api/v1/houses", tokens.AccessToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        // (j) Refresh with rotation: a new pair; the old refresh token is dead.
        var refreshed = await ReadTokensAsync(await _oauth.RefreshAsync(clientId, tokens.RefreshToken));
        refreshed.AccessToken.Should().NotBe(tokens.AccessToken);
        refreshed.RefreshToken.Should().NotBe(tokens.RefreshToken);
        var replay = await _oauth.RefreshAsync(clientId, tokens.RefreshToken);
        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadOAuthErrorAsync(replay)).Should().Be("invalid_grant");

        // (k) Already consented: a new authorization is silent…
        var pkce2 = Pkce.Create();
        var state2 = Guid.NewGuid().ToString("N");
        var code2 = CodeOfClientRedirect(await _oauth.AuthorizeAsync(AuthorizeUrl(clientId, pkce2, state2), session), state2);
        (await ReadTokensAsync(await _oauth.ExchangeCodeAsync(clientId, code2, pkce2.Verifier))).AccessToken.Should().NotBeNullOrEmpty();

        // …unless the client asks for the consent screen again — which does not ask twice on return.
        var forced = await _oauth.AuthorizeAsync(AuthorizeUrl(clientId, Pkce.Create(), state2, extra: "prompt=consent"), session);
        var returnUrl = ReturnUrlOfFrontendRedirect(forced, "consent");
        returnUrl.Should().NotContain("prompt=consent");
        returnUrl.Should().StartWith(new Uri(_oauth.Http.BaseAddress!, "/connect/authorize?").AbsoluteUri);
        var afterConsent = await _oauth.AuthorizeAsync(new Uri(returnUrl).PathAndQuery, session);
        CodeOfClientRedirect(afterConsent, state2);
    }

    /// <summary>
    /// OpenIddict also holds an ephemeral RSA key (regenerated at every start), only to pass its
    /// startup check. Were it to sign anything, every token would die at each restart and on every
    /// other replica. Every token actually issued — access token, refresh token and the stored
    /// payload of the authorization code — must validate with the keys derived from Jwt:Key alone.
    /// </summary>
    [Fact]
    public async Task IssuedTokens_AreProtectedByTheKeysDerivedFromTheJwtKeyAlone()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();
        var tokens = await _oauth.ConnectAsync(user, clientId);

        // Opaque to the client: a JWE (A256KW), as OpenIddict encrypts access tokens by default.
        new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken).Alg.Should().Be(SecurityAlgorithms.Aes256KW);

        var access = await ValidateWithDerivedKeysAsync(tokens.AccessToken);
        access.IsValid.Should().BeTrue(access.Exception?.Message);
        var accessToken = (JsonWebToken)access.SecurityToken;
        accessToken.InnerToken.Alg.Should().Be(SecurityAlgorithms.HmacSha512);
        accessToken.Audiences.Should().Contain(_oauth.McpResource, "RFC 8707: bound to the MCP server");
        access.Claims["sub"].Should().Be(user.Id.ToString());
        access.Claims.Should().NotContainKey("email", "the access token carries no personal data but the subject");

        var refresh = await ValidateWithDerivedKeysAsync(tokens.RefreshToken);
        refresh.IsValid.Should().BeTrue(refresh.Exception?.Message);
        ((JsonWebToken)refresh.SecurityToken).InnerToken.Alg.Should().Be(SecurityAlgorithms.HmacSha512);

        // The code handed to the client is a reference; its payload, stored in the database, is a token too.
        await using var db = await _fixture.CreateDbContextAsync();
        var codePayloads = await db.Set<OpenIddictEntityFrameworkCoreToken>()
            .Where(t => t.Subject == user.Id.ToString() && t.Type == "urn:openiddict:params:oauth:token-type:authorization_code" && t.Payload != null)
            .Select(t => t.Payload!)
            .ToListAsync();
        codePayloads.Should().NotBeEmpty();
        foreach (var payload in codePayloads)
        {
            var code = await ValidateWithDerivedKeysAsync(payload);
            code.IsValid.Should().BeTrue(code.Exception?.Message);
            ((JsonWebToken)code.SecurityToken).InnerToken.Alg.Should().Be(SecurityAlgorithms.HmacSha512);
        }
    }

    [Fact]
    public async Task ExplicitMcpResource_IsAccepted()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();
        var session = await _oauth.OpenSessionAsync(user);
        (await _oauth.GrantAsync(user, clientId, "houses:read")).StatusCode.Should().Be(HttpStatusCode.Created);
        var pkce = Pkce.Create();
        var state = Guid.NewGuid().ToString("N");

        var response = await _oauth.AuthorizeAsync(
            AuthorizeUrl(clientId, pkce, state, extra: "resource=" + Uri.EscapeDataString(_oauth.McpResource)), session);

        var code = CodeOfClientRedirect(response, state);
        (await _oauth.ExchangeCodeAsync(clientId, code, pkce.Verifier)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PartialConsent_IssuesOnlyTheGrantedScopes()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();

        var tokens = await _oauth.ConnectAsync(user, clientId, BothScopes, "houses:read");

        tokens.Scope.Split(' ').Should().Contain("houses:read").And.NotContain("houses:write");
    }

    /// <summary>
    /// Unchecking a scope on a new consent withdraws it: the consent is replaced, not added to, and
    /// the tokens issued under it — which still carry the withdrawn scope — stop working at once.
    /// </summary>
    [Fact]
    public async Task NewConsent_ReplacesTheScopes_AndRevokesTokensWhenNarrowed()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();
        var tokens = await _oauth.ConnectAsync(user, clientId);

        var narrowed = await _oauth.GrantAsync(user, clientId, "houses:read");

        narrowed.StatusCode.Should().Be(HttpStatusCode.Created);
        var authorization = (await _oauth.ListAuthorizationsAsync(user)).Should().ContainSingle("a consent is replaced, never doubled").Subject;
        authorization.GetProperty("scopes").EnumerateArray().Select(e => e.GetString()).Should().Equal("houses:read");
        var refresh = await _oauth.RefreshAsync(clientId, tokens.RefreshToken);
        refresh.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadOAuthErrorAsync(refresh)).Should().Be("invalid_grant");
        (await _oauth.SendAsBearer(HttpMethod.Get, "/connect/userinfo", tokens.AccessToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "the access token still carries houses:write");
    }

    [Fact]
    public async Task NewConsent_WithTheSameOrWiderScopes_KeepsTheTokens()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();
        var tokens = await _oauth.ConnectAsync(user, clientId, BothScopes, "houses:read");

        (await _oauth.GrantAsync(user, clientId, "houses:read")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await _oauth.GrantAsync(user, clientId, "houses:read", "houses:write")).StatusCode.Should().Be(HttpStatusCode.Created);

        var authorization = (await _oauth.ListAuthorizationsAsync(user)).Should().ContainSingle().Subject;
        authorization.GetProperty("scopes").EnumerateArray().Select(e => e.GetString()).Should().Equal("houses:read", "houses:write");
        (await _oauth.SendAsBearer(HttpMethod.Get, "/connect/userinfo", tokens.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadTokensAsync(await _oauth.RefreshAsync(clientId, tokens.RefreshToken))).AccessToken.Should().NotBeNullOrEmpty();
    }

    /// <summary>Two consents racing could each create an authorization: the next consent leaves one.</summary>
    [Fact]
    public async Task NewConsent_RevokesADuplicateLeftByARace()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();
        (await _oauth.GrantAsync(user, clientId, "houses:read")).StatusCode.Should().Be(HttpStatusCode.Created);
        await using (var db = await _fixture.CreateDbContextAsync())
        {
            var application = await db.Set<OpenIddictEntityFrameworkCoreApplication>().SingleAsync(a => a.ClientId == clientId);
            db.Add(new OpenIddictEntityFrameworkCoreAuthorization
            {
                Application = application,
                CreationDate = DateTime.UtcNow.AddMinutes(-1),
                Scopes = "[\"houses:write\"]",
                Status = OpenIddict.Abstractions.OpenIddictConstants.Statuses.Valid,
                Subject = user.Id.ToString(),
                Type = OpenIddict.Abstractions.OpenIddictConstants.AuthorizationTypes.Permanent
            });
            await db.SaveChangesAsync();
        }
        (await _oauth.ListAuthorizationsAsync(user)).Should().HaveCount(2);

        (await _oauth.GrantAsync(user, clientId, "houses:read", "houses:write")).StatusCode.Should().Be(HttpStatusCode.Created);

        var authorization = (await _oauth.ListAuthorizationsAsync(user)).Should().ContainSingle().Subject;
        authorization.GetProperty("scopes").EnumerateArray().Select(e => e.GetString()).Should().Equal("houses:read", "houses:write");
    }

    [Fact]
    public async Task RequestWithoutScope_DefaultsToTheClientScopes_SpelledOutForTheConsentScreen()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync(scope: "houses:read");
        var session = await _oauth.OpenSessionAsync(user);
        var state = Guid.NewGuid().ToString("N");

        var returnUrl = ReturnUrlOfFrontendRedirect(
            await _oauth.AuthorizeAsync(AuthorizeUrl(clientId, Pkce.Create(), state, scope: null), session), "consent");

        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(returnUrl).Query)["scope"].ToString()
            .Should().Be("houses:read");
    }

    [Theory]
    [InlineData("houses:admin")]
    [InlineData("houses:write")] // not registered for this client
    public async Task Consent_WithScopesTheClientCannotHave_Is400ValidationFailed(string scope)
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync(scope: "houses:read");

        var response = await _oauth.GrantAsync(user, clientId, scope);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).Should().Be("validation_failed");
    }

    [Fact]
    public async Task Consent_WithNoScope_Is400ValidationFailed()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();

        var response = await _oauth.GrantAsync(user, clientId);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).Should().Be("validation_failed");
    }

    [Fact]
    public async Task UnknownClient_Is404NotFound()
    {
        var user = await _oauth.RegisterUserAsync();

        var info = await _oauth.SendAsBearer(HttpMethod.Get, "/api/v1/oauth/clients/0123456789abcdef0123456789abcdef", user.AccessToken);
        var grant = await _oauth.GrantAsync(user, "0123456789abcdef0123456789abcdef", "houses:read");

        info.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await info.ReadErrorCodeAsync()).Should().Be("not_found");
        grant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await grant.ReadErrorCodeAsync()).Should().Be("not_found");
    }

    /// <summary>Decrypts and verifies a token with the keys derived from the API's Jwt:Key, nothing else.</summary>
    private static Task<TokenValidationResult> ValidateWithDerivedKeysAsync(string token)
    {
        var jwtKey = ApiJwtKey();
        return new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            TokenDecryptionKey = new SymmetricSecurityKey(OAuthKeyDerivation.EncryptionKey(jwtKey)),
            IssuerSigningKey = new SymmetricSecurityKey(OAuthKeyDerivation.SigningKey(jwtKey)),
            // The list covers the JWE algorithms too: key wrap, content encryption — and HS512 for
            // the signature, never RS256.
            ValidAlgorithms = [SecurityAlgorithms.Aes256KW, SecurityAlgorithms.Aes256CbcHmacSha512, SecurityAlgorithms.HmacSha512],
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false
        });
    }

    /// <summary>
    /// <c>Jwt:Key</c> of the API under test, as Program.cs resolves it: <c>JWT__KEY</c>, else the
    /// API's appsettings.Development.json (copied next to the tests by the project reference).
    /// </summary>
    private static string ApiJwtKey() =>
        Environment.GetEnvironmentVariable("JWT__KEY")
        ?? new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Development.json")
            .Build()["Jwt:Key"]
        ?? throw new InvalidOperationException("No Jwt:Key for the API under test.");
}
