using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HouseFlow.Application.DTOs;
using Microsoft.AspNetCore.WebUtilities;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.OAuth;

public sealed record TestUser(Guid Id, string AccessToken);

public sealed record Pkce(string Verifier, string Challenge)
{
    public static Pkce Create()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return new Pkce(verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record TokenSet(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn, string Scope);

/// <summary>
/// Drives the OAuth 2.1 flow like an MCP client (Claude) and its user's browser would: dynamic
/// registration, authorization request with PKCE, front-end session and consent, code exchange.
/// The API client follows no redirect and keeps no cookie: every 302 and cookie is asserted by hand.
/// </summary>
public sealed class OAuthTestClient
{
    /// <summary>Front end the API redirects to: no OAuth:WebBaseUrl nor CORS__ORIGINS in the tests.</summary>
    public const string WebBaseUrl = "http://localhost:3000";

    public const string RedirectUri = "http://127.0.0.1:9/callback";
    public const string BothScopes = "houses:read houses:write";
    public const string SessionCookieName = "oauthSession";

    private readonly IntegrationTestFixture _fixture;

    public OAuthTestClient(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        Http = fixture.CreateApiClient();
    }

    public HttpClient Http { get; }

    /// <summary>The MCP resource the tokens are issued for by default: <c>{scheme}://{host}/mcp</c> of the API.</summary>
    public string McpResource => new Uri(Http.BaseAddress!, "/mcp").AbsoluteUri;

    public async Task<TestUser> RegisterUserAsync()
    {
        var response = await Http.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequestDto(
            email: $"oauth-{Guid.NewGuid():N}@example.com", firstName: "OAuth", lastName: "User",
            password: "Password123!", consentAccepted: true));
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        return new TestUser(auth!.User.Id, auth.AccessToken);
    }

    public Task<HttpResponseMessage> RegisterClientRawAsync(object metadata) =>
        Http.PostAsJsonAsync("/connect/register", metadata);

    public async Task<string> RegisterClientAsync(string name = "Claude Test", string redirectUri = RedirectUri, string? scope = null)
    {
        var response = await RegisterClientRawAsync(scope is null
            ? new { client_name = name, redirect_uris = new[] { redirectUri } }
            : new { client_name = name, redirect_uris = new[] { redirectUri }, scope });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("client_id").GetString()!;
    }

    /// <summary>Relative authorization URL; <paramref name="extra"/> is appended verbatim (already encoded).</summary>
    public static string AuthorizeUrl(string clientId, Pkce? pkce, string state,
        string? scope = BothScopes, string redirectUri = RedirectUri, string? extra = null)
    {
        var parameters = new List<string>
        {
            $"client_id={Uri.EscapeDataString(clientId)}",
            "response_type=code",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            $"state={Uri.EscapeDataString(state)}"
        };
        if (scope is not null) parameters.Add($"scope={Uri.EscapeDataString(scope)}");
        if (pkce is not null)
        {
            parameters.Add($"code_challenge={pkce.Challenge}");
            parameters.Add("code_challenge_method=S256");
        }
        if (extra is not null) parameters.Add(extra);
        return "/connect/authorize?" + string.Join('&', parameters);
    }

    /// <summary>POST /api/v1/oauth/session: the value of the oauthSession cookie it sets.</summary>
    public async Task<string> OpenSessionAsync(TestUser user)
    {
        var response = await SendAsBearer(HttpMethod.Post, "/api/v1/oauth/session", user.AccessToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        return SessionCookie(response) ?? throw new InvalidOperationException("No oauthSession cookie was set.");
    }

    /// <summary>The <c>oauthSession=…</c> Set-Cookie header of a response, if any.</summary>
    public static string? SessionCookieHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(c => c.StartsWith(SessionCookieName + "=", StringComparison.Ordinal))
            : null;

    public static string? SessionCookie(HttpResponseMessage response) =>
        SessionCookieHeader(response)?.Split(';')[0][(SessionCookieName.Length + 1)..];

    public async Task<HttpResponseMessage> AuthorizeAsync(string url, string? sessionCookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (sessionCookie is not null) request.Headers.Add("Cookie", $"{SessionCookieName}={sessionCookie}");
        return await Http.SendAsync(request);
    }

    public async Task<HttpResponseMessage> GrantAsync(TestUser user, string clientId, params string[] scopes)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/oauth/authorizations")
        {
            Content = JsonContent.Create(new GrantOAuthAuthorizationRequestDto(clientId, scopes))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return await Http.SendAsync(request);
    }

    public Task<HttpResponseMessage> SendAsBearer(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Http.SendAsync(request);
    }

    public Task<HttpResponseMessage> ExchangeCodeAsync(string clientId, string code, string verifier, string redirectUri = RedirectUri) =>
        Http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier
        }));

    public Task<HttpResponseMessage> RefreshAsync(string clientId, string refreshToken) =>
        Http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId
        }));

    /// <summary>Query parameters of a redirect's Location.</summary>
    public static Dictionary<string, string> LocationQuery(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        return QueryHelpers.ParseQuery(location.Query).ToDictionary(p => p.Key, p => p.Value.ToString());
    }

    /// <summary>Asserts a redirect to a front-end OAuth page and returns its decoded <c>returnUrl</c>.</summary>
    public static string ReturnUrlOfFrontendRedirect(HttpResponseMessage response, string page)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).Should().Be($"{WebBaseUrl}/oauth/{page}");
        return LocationQuery(response)["returnUrl"];
    }

    /// <summary>Asserts a redirect back to the client with a code (and the state), and returns the code.</summary>
    public static string CodeOfClientRedirect(HttpResponseMessage response, string state, string redirectUri = RedirectUri)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be(redirectUri);
        var query = LocationQuery(response);
        query.Should().NotContainKey("error", query.GetValueOrDefault("error_description"));
        query["state"].Should().Be(state);
        return query["code"];
    }

    /// <summary>Asserts a redirect back to the client with an error (never a code) and returns the error.</summary>
    public static string ErrorOfClientRedirect(HttpResponseMessage response, string? state = null, string redirectUri = RedirectUri)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be(redirectUri);
        var query = LocationQuery(response);
        query.Should().NotContainKey("code");
        if (state is not null) query["state"].Should().Be(state);
        return query["error"];
    }

    public static async Task<TokenSet> ReadTokensAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        return new TokenSet(
            root.GetProperty("access_token").GetString()!,
            root.GetProperty("refresh_token").GetString()!,
            root.GetProperty("token_type").GetString()!,
            root.GetProperty("expires_in").GetInt32(),
            root.GetProperty("scope").GetString()!);
    }

    /// <summary>The OAuth <c>error</c> of a token endpoint failure.</summary>
    public static async Task<string?> ReadOAuthErrorAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
    }

    /// <summary>The whole flow for a user who consents to <paramref name="grantedScopes"/>: the client's tokens.</summary>
    public async Task<TokenSet> ConnectAsync(TestUser user, string clientId, string scope = BothScopes, params string[] grantedScopes)
    {
        var session = await OpenSessionAsync(user);
        (await GrantAsync(user, clientId, grantedScopes.Length > 0 ? grantedScopes : scope.Split(' '))).StatusCode
            .Should().Be(HttpStatusCode.Created);

        var pkce = Pkce.Create();
        var state = Guid.NewGuid().ToString("N");
        var code = CodeOfClientRedirect(await AuthorizeAsync(AuthorizeUrl(clientId, pkce, state, scope), session), state);
        return await ReadTokensAsync(await ExchangeCodeAsync(clientId, code, pkce.Verifier));
    }
}
