using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HouseFlow.Application.DTOs;
using static HouseFlow.IntegrationTests.OAuth.OAuthTestClient;

namespace HouseFlow.IntegrationTests.OAuth;

/// <summary>
/// The <c>oauthSession</c> cookie ends with the app session: on a shared browser, the next person
/// starting an OAuth authorization must not be taken for the one who logged out or left.
/// </summary>
[Collection("Integration")]
public class OAuthSessionCookieTests
{
    private readonly OAuthTestClient _oauth;

    public OAuthSessionCookieTests(IntegrationTestFixture fixture) => _oauth = new OAuthTestClient(fixture);

    private static void ShouldExpireTheSessionCookie(HttpResponseMessage response)
    {
        var cookie = SessionCookieHeader(response);
        cookie.Should().NotBeNull("the oauthSession cookie must be cleared");
        cookie.Should().StartWith($"{SessionCookieName}=;")
            .And.ContainEquivalentOf("expires=Thu, 01 Jan 1970 00:00:00 GMT")
            .And.ContainEquivalentOf("path=/connect");
        cookie.Should().NotContainEquivalentOf("max-age", "a Max-Age would outlive the expiry date");
    }

    /// <summary>A registered user's refresh cookie, as the browser would send it back.</summary>
    private async Task<(TestUser User, string RefreshCookie)> RegisterWithRefreshCookieAsync()
    {
        var response = await _oauth.Http.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequestDto(
            email: $"oauth-cookie-{Guid.NewGuid():N}@example.com", firstName: "Cookie", lastName: "User",
            password: "Password123!", consentAccepted: true));
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        var refreshCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("refreshToken=")).Split(';')[0];
        return (new TestUser(auth!.User.Id, auth.AccessToken), refreshCookie);
    }

    [Fact]
    public async Task Logout_ClearsTheOAuthSessionCookie()
    {
        var user = await _oauth.RegisterUserAsync();
        await _oauth.OpenSessionAsync(user);

        var response = await _oauth.SendAsBearer(HttpMethod.Post, "/api/v1/auth/logout", user.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldExpireTheSessionCookie(response);
    }

    [Fact]
    public async Task RevokingTheRefreshToken_ClearsTheOAuthSessionCookie()
    {
        var (user, refreshCookie) = await RegisterWithRefreshCookieAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/revoke");
        request.Headers.Authorization = new("Bearer", user.AccessToken);
        request.Headers.Add("Cookie", refreshCookie);

        var response = await _oauth.Http.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldExpireTheSessionCookie(response);
    }

    [Fact]
    public async Task AccountDeletion_ClearsTheOAuthSessionCookie()
    {
        var user = await _oauth.RegisterUserAsync();
        await _oauth.OpenSessionAsync(user);
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/me")
        {
            Content = JsonContent.Create(new DeleteAccountRequestDto(password: "Password123!"))
        };
        request.Headers.Authorization = new("Bearer", user.AccessToken);

        var response = await _oauth.Http.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        ShouldExpireTheSessionCookie(response);
    }

    [Fact]
    public async Task SessionOfADeletedAccount_CannotAuthorize()
    {
        var user = await _oauth.RegisterUserAsync();
        var clientId = await _oauth.RegisterClientAsync();
        (await _oauth.GrantAsync(user, clientId, "houses:read")).StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await _oauth.OpenSessionAsync(user);
        var delete = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/me")
        {
            Content = JsonContent.Create(new DeleteAccountRequestDto(password: "Password123!"))
        };
        delete.Headers.Authorization = new("Bearer", user.AccessToken);
        (await _oauth.Http.SendAsync(delete)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // A browser that kept the cookie anyway (it was cleared, see above) still gets no code.
        var state = Guid.NewGuid().ToString("N");
        ErrorOfClientRedirect(await _oauth.AuthorizeAsync(AuthorizeUrl(clientId, Pkce.Create(), state), session), state)
            .Should().Be("access_denied");
    }
}
