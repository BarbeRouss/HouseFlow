using FluentAssertions;
using HouseFlow.Application.DTOs;
using System.Net;
using System.Net.Http.Json;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Authentication;

[Collection("Integration")]
public class AuthenticationTests
{
    private readonly IntegrationTestFixture _fixture;

    public AuthenticationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private HttpClient CreateClient() => _fixture.CreateApiClient();

    private static RegisterRequestDto CreateValidRegisterRequest(string? email = null) => new(
        email: email ?? $"test-{Guid.NewGuid()}@example.com",
        firstName: "Test",
        lastName: "User",
        password: "Password123!"
    );

    #region Register Tests

    [Fact]
    public async Task Register_WithValidData_ReturnsTokenAndCreatesUser()
    {
        // Arrange
        var client = CreateClient();
        var request = CreateValidRegisterRequest();

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var authResponse = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        authResponse.Should().NotBeNull();
        authResponse!.AccessToken.Should().NotBeNullOrEmpty();
        authResponse.User.Should().NotBeNull();
        authResponse.User.Email.Should().Be(request.Email);
        authResponse.User.FirstName.Should().Be(request.FirstName);
        authResponse.User.LastName.Should().Be(request.LastName);

        // Verify refresh token is set in cookie (not in response body for security)
        response.Headers.Should().ContainKey("Set-Cookie");
        var setCookieHeader = response.Headers.GetValues("Set-Cookie").FirstOrDefault();
        setCookieHeader.Should().Contain("refreshToken=");
    }

    [Fact]
    public async Task Register_WithExistingEmail_Returns409Conflict()
    {
        // Arrange
        var client = CreateClient();
        var email = $"duplicate-{Guid.NewGuid()}@example.com";
        var request = CreateValidRegisterRequest(email);

        // First registration
        await client.PostAsJsonAsync("/api/v1/auth/register", request);

        // Act - Second registration with same email
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);

        // Assert - The API returns 409 Conflict for duplicate email
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_WithInvalidEmail_Returns400BadRequest()
    {
        // Arrange
        var client = CreateClient();
        var request = new RegisterRequestDto(
            email: "invalid-email",
            firstName: "Test",
            lastName: "User",
            password: "Password123!"
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("Sh1!aaa", "less than 8 characters")]
    [InlineData("alllowercase123!", "no uppercase letter")]
    [InlineData("ALLUPPERCASE123!", "no lowercase letter")]
    [InlineData("NoDigitsHere!!", "no digit")]
    [InlineData("NoSpecialChar123", "no special character")]
    public async Task Register_WithWeakPassword_Returns400BadRequest(string password, string reason)
    {
        // Arrange
        var client = CreateClient();
        var request = new RegisterRequestDto(
            email: $"test-{Guid.NewGuid()}@example.com",
            firstName: "Test",
            lastName: "User",
            password: password
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, because: reason);
    }

    #endregion

    #region Login Tests

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsToken()
    {
        // Arrange
        var client = CreateClient();
        var email = $"login-test-{Guid.NewGuid()}@example.com";
        var password = "Password123!";
        var registerRequest = new RegisterRequestDto(email: email, firstName: "Test", lastName: "User", password: password);

        // First register the user
        await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);

        var loginRequest = new LoginRequestDto(email: email, password: password, rememberMe: false);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var authResponse = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        authResponse.Should().NotBeNull();
        authResponse!.AccessToken.Should().NotBeNullOrEmpty();
        authResponse.User.Should().NotBeNull();
        authResponse.User.Email.Should().Be(email);

        // Verify refresh token is set in cookie
        response.Headers.Should().ContainKey("Set-Cookie");
    }

    [Fact]
    public async Task Login_WithInvalidPassword_Returns401Unauthorized()
    {
        // Arrange
        var client = CreateClient();
        var email = $"login-invalid-{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequestDto(email: email, firstName: "Test", lastName: "User", password: "Password123!");

        // First register the user
        await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);

        var loginRequest = new LoginRequestDto(email: email, password: "WrongPassword!", rememberMe: false);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithNonExistentEmail_Returns401Unauthorized()
    {
        // Arrange
        var client = CreateClient();
        var loginRequest = new LoginRequestDto(email: $"nonexistent-{Guid.NewGuid()}@example.com", password: "Password123!", rememberMe: false);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Refresh Token Tests

    [Fact]
    public async Task RefreshToken_WithValidToken_ReturnsNewToken()
    {
        // Arrange
        var client = CreateClient();
        var email = $"refresh-test-{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequestDto(email: email, firstName: "Test", lastName: "User", password: "Password123!");

        // Register and get refresh token cookie
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();

        // Extract refresh token from Set-Cookie header
        var setCookieHeader = registerResponse.Headers.GetValues("Set-Cookie").FirstOrDefault();
        setCookieHeader.Should().NotBeNull();

        // Parse the cookie value
        var cookieValue = setCookieHeader!.Split(';')[0].Replace("refreshToken=", "");

        // Create request with cookie header
        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        refreshRequest.Headers.Add("Cookie", $"refreshToken={cookieValue}");

        // Act
        var response = await client.SendAsync(refreshRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var authResponse = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        authResponse.Should().NotBeNull();
        authResponse!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RefreshToken_WithExpiredToken_Returns401Unauthorized()
    {
        // Arrange
        var client = CreateClient();

        // Create request with invalid/expired refresh token
        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        refreshRequest.Headers.Add("Cookie", "refreshToken=invalid-expired-token");

        // Act
        var response = await client.SendAsync(refreshRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Session persistence / reuse detection (#164)

    private static string RefreshCookieOf(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("refreshToken="));

    private static string CookieValue(string setCookie) =>
        setCookie.Split(';')[0].Replace("refreshToken=", "");

    private static DateTime? CookieExpires(string setCookie)
    {
        var attr = setCookie.Split(';').Select(p => p.Trim())
            .FirstOrDefault(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase));
        return attr is null ? null : DateTime.Parse(attr["expires=".Length..], null, System.Globalization.DateTimeStyles.AdjustToUniversal);
    }

    private static Task<HttpResponseMessage> RefreshWithAsync(HttpClient client, string cookieValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", $"refreshToken={cookieValue}");
        return client.SendAsync(request);
    }

    private async Task<(HttpClient Client, string Email)> RegisterAsync()
    {
        var client = CreateClient();
        var request = CreateValidRegisterRequest();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (client, request.Email);
    }

    private static async Task<string> LoginCookieAsync(HttpClient client, string email, bool rememberMe)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequestDto(email: email, password: "Password123!", rememberMe: rememberMe));
        response.EnsureSuccessStatusCode();
        return RefreshCookieOf(response);
    }

    [Fact]
    public async Task Login_WithRememberMe_SetsPersistentCookieForAYear()
    {
        var (client, email) = await RegisterAsync();

        var setCookie = await LoginCookieAsync(client, email, rememberMe: true);

        setCookie.Should().Contain("httponly");
        setCookie.ToLowerInvariant().Should().Contain("samesite=lax", "Lax is the default (CSRF protection); None is opt-in per environment");
        var expires = CookieExpires(setCookie);
        expires.Should().NotBeNull();
        expires!.Value.Should().BeCloseTo(DateTime.UtcNow.AddDays(365), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Login_WithoutRememberMe_SetsSessionCookie()
    {
        var (client, email) = await RegisterAsync();

        var setCookie = await LoginCookieAsync(client, email, rememberMe: false);

        setCookie.Should().Contain("httponly");
        setCookie.ToLowerInvariant().Should().NotContain("expires=").And.NotContain("max-age=");
    }

    [Fact]
    public async Task Refresh_KeepsTheLifetimeChosenAtLogin()
    {
        var (client, email) = await RegisterAsync();
        var persistent = await LoginCookieAsync(client, email, rememberMe: true);
        var session = await LoginCookieAsync(client, email, rememberMe: false);

        var refreshedPersistent = await RefreshWithAsync(client, CookieValue(persistent));
        var refreshedSession = await RefreshWithAsync(client, CookieValue(session));

        refreshedPersistent.StatusCode.Should().Be(HttpStatusCode.OK);
        CookieExpires(RefreshCookieOf(refreshedPersistent))!.Value
            .Should().BeCloseTo(DateTime.UtcNow.AddDays(365), TimeSpan.FromMinutes(5));
        refreshedSession.StatusCode.Should().Be(HttpStatusCode.OK);
        CookieExpires(RefreshCookieOf(refreshedSession)).Should().BeNull();
    }

    [Fact]
    public async Task Refresh_RotatedTokenReusedWithinGrace_ReturnsCurrentToken()
    {
        var (client, email) = await RegisterAsync();
        var a1 = CookieValue(await LoginCookieAsync(client, email, rememberMe: true));
        var a2 = CookieValue(RefreshCookieOf(await RefreshWithAsync(client, a1)));

        // Two tabs booting at once both send a1; the loser must not be treated as a thief.
        var replay = await RefreshWithAsync(client, a1);

        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        CookieValue(RefreshCookieOf(replay)).Should().Be(a2);
        (await RefreshWithAsync(client, a2)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_RotatedTokenReusedOutsideGrace_RevokesItsFamilyOnly()
    {
        var (client, email) = await RegisterAsync();
        var deviceA1 = CookieValue(await LoginCookieAsync(client, email, rememberMe: true));
        var deviceB1 = CookieValue(await LoginCookieAsync(client, email, rememberMe: true));
        var deviceA2 = CookieValue(RefreshCookieOf(await RefreshWithAsync(client, deviceA1)));
        var deviceA3 = CookieValue(RefreshCookieOf(await RefreshWithAsync(client, deviceA2)));

        // a1 is two rotations old: its replacement is no longer active, so this cannot be a race.
        var replay = await RefreshWithAsync(client, deviceA1);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshWithAsync(client, deviceA3)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the whole family is revoked");
        (await RefreshWithAsync(client, deviceB1)).StatusCode.Should().Be(HttpStatusCode.OK, "other devices are untouched");
    }

    [Fact]
    public async Task Login_BeyondTenSessions_EvictsTheOldestOne()
    {
        var (client, email) = await RegisterAsync();
        var sessions = new List<string>();
        for (var i = 0; i < 10; i++)
            sessions.Add(CookieValue(await LoginCookieAsync(client, email, rememberMe: false)));

        // Registration opened a session too: this is the 12th, evicting registration's and the first login's.
        sessions.Add(CookieValue(await LoginCookieAsync(client, email, rememberMe: false)));

        (await RefreshWithAsync(client, sessions[0])).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshWithAsync(client, sessions[1])).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RefreshWithAsync(client, sessions[10])).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Revoke Token Tests

    [Fact]
    public async Task RevokeToken_WithValidToken_RevokesSuccessfully()
    {
        // Arrange
        var client = CreateClient();
        var email = $"revoke-test-{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequestDto(email: email, firstName: "Test", lastName: "User", password: "Password123!");

        // Register and get tokens
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();

        var authResponse = await registerResponse.Content.ReadAsJsonAsync<AuthResponseDto>();
        var setCookieHeader = registerResponse.Headers.GetValues("Set-Cookie").FirstOrDefault();
        var cookieValue = setCookieHeader!.Split(';')[0].Replace("refreshToken=", "");

        // Create revoke request with auth token and cookie
        var revokeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/revoke");
        revokeRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);
        revokeRequest.Headers.Add("Cookie", $"refreshToken={cookieValue}");

        // Act
        var response = await client.SendAsync(revokeRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify the refresh token is now invalid - try to use it
        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        refreshRequest.Headers.Add("Cookie", $"refreshToken={cookieValue}");
        var refreshResponse = await client.SendAsync(refreshRequest);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokeToken_WithoutAuth_Returns401Unauthorized()
    {
        // Arrange
        var client = CreateClient();

        // Create revoke request without authorization
        var revokeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/revoke");
        revokeRequest.Headers.Add("Cookie", "refreshToken=some-token");

        // Act
        var response = await client.SendAsync(revokeRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokeToken_WithInvalidRefreshToken_Returns400BadRequest()
    {
        // Arrange - Register user with one client
        var client1 = CreateClient();
        var email = $"revoke-invalid-{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequestDto(email: email, firstName: "Test", lastName: "User", password: "Password123!");

        var registerResponse = await client1.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        var authResponse = await registerResponse.Content.ReadAsJsonAsync<AuthResponseDto>();

        // Use a fresh client to avoid cookie storage, send invalid token
        var client2 = CreateClient();
        var revokeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/revoke");
        revokeRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);
        revokeRequest.Headers.Add("Cookie", "refreshToken=invalid-token-that-does-not-exist");

        // Act
        var response = await client2.SendAsync(revokeRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Logout Tests

    [Fact]
    public async Task Logout_WithValidSession_LogsOutSuccessfully()
    {
        // Arrange
        var client = CreateClient();
        var email = $"logout-test-{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequestDto(email: email, firstName: "Test", lastName: "User", password: "Password123!");

        // Register and get tokens
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();

        var authResponse = await registerResponse.Content.ReadAsJsonAsync<AuthResponseDto>();
        var setCookieHeader = registerResponse.Headers.GetValues("Set-Cookie").FirstOrDefault();
        var cookieValue = setCookieHeader!.Split(';')[0].Replace("refreshToken=", "");

        // Create logout request
        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logoutRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);
        logoutRequest.Headers.Add("Cookie", $"refreshToken={cookieValue}");

        // Act
        var response = await client.SendAsync(logoutRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify the refresh token is now invalid
        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        refreshRequest.Headers.Add("Cookie", $"refreshToken={cookieValue}");
        var refreshResponse = await client.SendAsync(refreshRequest);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithoutAuth_Returns401Unauthorized()
    {
        // Arrange
        var client = CreateClient();

        // Create logout request without authorization
        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");

        // Act
        var response = await client.SendAsync(logoutRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithoutRefreshToken_StillSucceeds()
    {
        // Arrange
        var client = CreateClient();
        var email = $"logout-nocookie-{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequestDto(email: email, firstName: "Test", lastName: "User", password: "Password123!");

        // Register and get access token
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        var authResponse = await registerResponse.Content.ReadAsJsonAsync<AuthResponseDto>();

        // Create logout request without cookie (but with valid auth)
        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logoutRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);

        // Act
        var response = await client.SendAsync(logoutRequest);

        // Assert - Should still succeed (graceful handling)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion
}
