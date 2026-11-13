using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;
using static HouseFlow.IntegrationTests.OAuth.OAuthTestClient;

namespace HouseFlow.IntegrationTests.OAuth;

/// <summary>
/// Client ID Metadata Documents (#308) through the real API. The API runs in its own process (Aspire)
/// and rightly refuses to download from loopback or a private network, so a document cannot be
/// served to it from here: the happy path (document → application → consent origin, cache,
/// refresh, races) is covered by the unit tests, with the downloader stubbed. What this checks is
/// the wiring only the running server shows: the discovery flag, the handler placed before
/// OpenIddict's own client lookup, and the SSRF refusals of the real <c>ConnectCallback</c> — which
/// never answer by redirecting to the client.
/// </summary>
[Collection("Integration")]
public class OAuthClientIdMetadataTests
{
    private readonly IntegrationTestFixture _fixture;
    private readonly OAuthTestClient _oauth;

    public OAuthClientIdMetadataTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _oauth = new OAuthTestClient(fixture);
    }

    [Fact]
    public async Task Metadata_AdvertisesClientIdMetadataDocuments()
    {
        var response = await _fixture.CreateApiClient().GetAsync("/.well-known/oauth-authorization-server");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("client_id_metadata_document_supported").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// None of these can become a client: not a valid URL for a client_id, or a host the downloader
    /// refuses to connect to. Each is answered on the error page — 400 <c>invalid_client</c> — with
    /// the description of the metadata-document handler (not OpenIddict's own « unknown client »),
    /// which proves the handler ran first; and never a redirect to the request's redirect_uri.
    /// </summary>
    [Theory]
    [InlineData("http://claude.ai/.well-known/oauth-client-metadata")]
    [InlineData("https://claude.ai:8443/client.json")]
    [InlineData("https://claude.ai/")]
    [InlineData("https://claude.ai/a/../client.json")]
    [InlineData("https://user:pass@claude.ai/client.json")]
    [InlineData("https://127.0.0.1/client.json")]
    [InlineData("https://localhost/client.json")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://168.63.129.16/machine")]
    [InlineData("https://[::1]/client.json")]
    public async Task ARefusedClientIdUrl_IsInvalidClient_NeverARedirect(string clientId)
    {
        var response = await _oauth.AuthorizeAsync(
            AuthorizeUrl(clientId, Pkce.Create(), "state-1", redirectUri: WebRedirectUri));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull("the redirect_uri of a client nobody validated is never trusted");

        // OpenIddict answers an authorization request it cannot redirect in plain text
        // (« error:invalid_client », « error_description:… »), not in JSON as the token endpoint does.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("invalid_client");
        body.Should().ContainAny("client metadata document", "client_id");
    }

    [Fact]
    public async Task ADynamicallyRegisteredClient_IsUntouchedByTheHandler()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();
        var session = await _oauth.ConsentAsync(user, clientId, "houses:read", "houses:write");
        var state = Guid.NewGuid().ToString("N");

        var response = await _oauth.AuthorizeAsync(AuthorizeUrl(clientId, Pkce.Create(), state), session);

        CodeOfClientRedirect(response, state).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ADynamicallyRegisteredClient_IsNeverShownAsVerified_WhateverTheRedirectUri()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync("Claude Test", WebRedirectUri);

        var response = await _oauth.SendAsBearer(
            HttpMethod.Get,
            $"/api/v1/oauth/clients/{clientId}?redirectUri={Uri.EscapeDataString(WebRedirectUri)}",
            user.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (json.RootElement.TryGetProperty("verifiedClientOrigin", out var origin))
            origin.ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// The consent screen asks for <c>GET /api/v1/oauth/clients/{client_id}</c> with the URL encoded in
    /// the path (<c>https%3A%2F%2F…</c>, as the Blazor client does). The application cannot come from a
    /// download here (loopback is refused), so it is written as the provisioner writes it: a registered
    /// client re-keyed under the URL, flagged as a metadata-document client.
    /// </summary>
    [Fact]
    public async Task AUrlClientId_EncodedInThePath_IsFoundAndShownAsVerified()
    {
        const string callback = "https://claude.ai/api/mcp/auth_callback";
        var user = await _oauth.RegisterUserAsync();
        var registered = await _oauth.RegisterClientAsync("Claude", callback);
        var clientId = $"https://claude.ai/oauth/mcp-oauth-client-metadata-{Guid.NewGuid():N}";
        await using (var db = await _fixture.CreateDbContextAsync())
        {
            var application = await db.Set<OpenIddictEntityFrameworkCoreApplication>().SingleAsync(a => a.ClientId == registered);
            application.ClientId = clientId;
            application.Properties = "{\"cimd\":true}";
            await db.SaveChangesAsync();
        }

        var response = await _oauth.SendAsBearer(
            HttpMethod.Get,
            $"/api/v1/oauth/clients/{Uri.EscapeDataString(clientId)}?redirectUri={Uri.EscapeDataString(callback)}",
            user.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("clientId").GetString().Should().Be(clientId);
        json.RootElement.GetProperty("verifiedClientOrigin").GetString().Should().Be("claude.ai");

        // Never a verified origin for a request that leaves the client's host.
        var elsewhere = await _oauth.SendAsBearer(
            HttpMethod.Get,
            $"/api/v1/oauth/clients/{Uri.EscapeDataString(clientId)}?redirectUri={Uri.EscapeDataString("https://other.example/cb")}",
            user.AccessToken);
        using var elsewhereJson = JsonDocument.Parse(await elsewhere.Content.ReadAsStringAsync());
        elsewhereJson.RootElement.GetProperty("verifiedClientOrigin").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task TheTokenEndpoint_NeverCreatesAClientFromAUrl()
    {
        var response = await _oauth.ExchangeCodeAsync("https://claude.ai/.well-known/oauth-client-metadata", "not-a-code", Pkce.Create().Verifier, WebRedirectUri);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
        (await ReadOAuthErrorAsync(response)).Should().Be("invalid_client");
    }
}
