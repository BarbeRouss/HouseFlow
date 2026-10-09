using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using static HouseFlow.IntegrationTests.OAuth.OAuthTestClient;

namespace HouseFlow.IntegrationTests.OAuth;

/// <summary>Connected applications: listing, revocation (immediate), and accounts that lost the right to authorize.</summary>
[Collection("Integration")]
public class OAuthRevocationTests
{
    private readonly IntegrationTestFixture _fixture;
    private readonly OAuthTestClient _oauth;

    public OAuthRevocationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _oauth = new OAuthTestClient(fixture);
    }

    private Task<List<JsonElement>> ListAsync(TestUser user) => _oauth.ListAuthorizationsAsync(user);

    [Fact]
    public async Task Revocation_ListsThenRevokes_AndEveryTokenStopsWorkingAtOnce()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync("Revoked app");
        var tokens = await _oauth.ConnectAsync(user, clientId);

        var listed = (await ListAsync(user)).Should().ContainSingle().Subject;
        listed.GetProperty("clientId").GetString().Should().Be(clientId);
        listed.GetProperty("clientName").GetString().Should().Be("Revoked app");
        listed.GetProperty("scopes").EnumerateArray().Select(e => e.GetString()).Should().Equal("houses:read", "houses:write");
        listed.GetProperty("createdAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
        var id = listed.GetProperty("id").GetString()!;
        (await _oauth.SendAsBearer(HttpMethod.Get, "/connect/userinfo", tokens.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _oauth.SendAsBearer(HttpMethod.Delete, $"/api/v1/oauth/authorizations/{id}", user.AccessToken)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var refresh = await _oauth.RefreshAsync(clientId, tokens.RefreshToken);
        refresh.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadOAuthErrorAsync(refresh)).Should().Be("invalid_grant");
        (await _oauth.SendAsBearer(HttpMethod.Get, "/connect/userinfo", tokens.AccessToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "the access token is still unexpired, but its authorization is revoked");
        (await ListAsync(user)).Should().BeEmpty();
    }

    [Fact]
    public async Task RevokingSomeoneElsesAuthorization_Is404()
    {
        var owner = await _oauth.RegisterUserAsync();
        var intruder = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();
        (await _oauth.GrantAsync(owner, clientId, "houses:read")).StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await ListAsync(owner)).Single().GetProperty("id").GetString()!;

        var response = await _oauth.SendAsBearer(HttpMethod.Delete, $"/api/v1/oauth/authorizations/{id}", intruder.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadErrorCodeAsync()).Should().Be("not_found");
        (await ListAsync(owner)).Should().ContainSingle("the owner's consent is untouched");
        (await ListAsync(intruder)).Should().BeEmpty();
    }

    [Fact]
    public async Task RevokingAnUnknownAuthorization_Is404()
    {
        var user = await _oauth.RegisterUserAsync();

        var response = await _oauth.SendAsBearer(HttpMethod.Delete, $"/api/v1/oauth/authorizations/{Guid.NewGuid()}", user.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadErrorCodeAsync()).Should().Be("not_found");
    }

    [Fact]
    public async Task RestrictedAccount_CannotAuthorize_NorRefresh()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();
        var tokens = await _oauth.ConnectAsync(user, clientId);
        var session = await _oauth.OpenSessionAsync(user);

        // RGPD Art. 18 — set by the privacy officer, as in the account restriction tests.
        await using (var db = await _fixture.CreateDbContextAsync())
        {
            await db.Users.Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ProcessingRestrictedAt, DateTime.UtcNow));
        }

        var state = Guid.NewGuid().ToString("N");
        ErrorOfClientRedirect(await _oauth.AuthorizeAsync(AuthorizeUrl(clientId, Pkce.Create(), state), session), state)
            .Should().Be("access_denied");

        var refresh = await _oauth.RefreshAsync(clientId, tokens.RefreshToken);
        refresh.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadOAuthErrorAsync(refresh)).Should().Be("invalid_grant");
    }

    [Fact]
    public async Task DeletedAccount_CannotRefresh()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();
        var tokens = await _oauth.ConnectAsync(user, clientId);

        var delete = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/me")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new DeleteAccountRequestDto(password: "Password123!")),
            Headers = { Authorization = new("Bearer", user.AccessToken) }
        };
        (await _oauth.Http.SendAsync(delete)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refresh = await _oauth.RefreshAsync(clientId, tokens.RefreshToken);
        refresh.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadOAuthErrorAsync(refresh)).Should().Be("invalid_grant");
    }
}
