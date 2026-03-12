using FluentAssertions;
using HouseFlow.Application.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Authentication;

public class AuthenticationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthenticationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private static RegisterRequestDto CreateValidRegisterRequest(string? email = null) => new(
        FirstName: "Test",
        LastName: "User",
        Email: email ?? $"test-{Guid.NewGuid()}@example.com",
        Password: "Password123!"
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
            FirstName: "Test",
            LastName: "User",
            Email: "invalid-email",
            Password: "Password123!"
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_WithWeakPassword_Returns400BadRequest()
    {
        // Arrange
        var client = CreateClient();
        var request = new RegisterRequestDto(
            FirstName: "Test",
            LastName: "User",
            Email: $"test-{Guid.NewGuid()}@example.com",
            Password: "weak" // Less than 8 characters
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
        var registerRequest = new RegisterRequestDto("Test", "User", email, password);

        // First register the user
        await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);

        var loginRequest = new LoginRequestDto(email, password);

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
        var registerRequest = new RegisterRequestDto("Test", "User", email, "Password123!");

        // First register the user
        await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);

        var loginRequest = new LoginRequestDto(email, "WrongPassword!");

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
        var loginRequest = new LoginRequestDto($"nonexistent-{Guid.NewGuid()}@example.com", "Password123!");

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
        var registerRequest = new RegisterRequestDto("Test", "User", email, "Password123!");

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

    #region Revoke Token Tests

    [Fact]
    public async Task RevokeToken_WithValidToken_RevokesSuccessfully()
    {
        // Arrange
        var client = CreateClient();
        var email = $"revoke-test-{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequestDto("Test", "User", email, "Password123!");

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
        var registerRequest = new RegisterRequestDto("Test", "User", email, "Password123!");

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
        var registerRequest = new RegisterRequestDto("Test", "User", email, "Password123!");

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
        var registerRequest = new RegisterRequestDto("Test", "User", email, "Password123!");

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
