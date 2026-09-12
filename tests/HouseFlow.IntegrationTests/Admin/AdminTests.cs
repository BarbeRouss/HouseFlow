using FluentAssertions;
using HouseFlow.Application.DTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Admin;

/// <summary>
/// Admin interface API: only users flagged IsAdmin (JWT role claim) can reach /api/v1/admin/*.
/// The first admin comes from the Admin:BootstrapEmails configuration (src/HouseFlow.API/appsettings.json).
/// </summary>
[Collection("Integration")]
public class AdminTests
{
    /// <summary>Must match an entry of Admin:BootstrapEmails in src/HouseFlow.API/appsettings.json.</summary>
    private const string BootstrapAdminEmail = "julienrousselle@outlook.be";
    private const string Password = "Password123!";

    private readonly IntegrationTestFixture _fixture;

    public AdminTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private HttpClient CreateClient() => _fixture.CreateApiClient();

    private async Task<(HttpClient client, AuthResponseDto auth)> RegisterUserAsync(string? email = null)
    {
        var client = CreateClient();
        var request = new RegisterRequestDto(
            email: email ?? $"test-{Guid.NewGuid()}@example.com",
            firstName: "Regular",
            lastName: "User",
            password: Password);

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();

        var auth = (await response.Content.ReadAsJsonAsync<AuthResponseDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    /// <summary>
    /// Returns a client authenticated as the bootstrap administrator. The account is registered on
    /// first use (the test database is reset once per run) and logged into afterwards.
    /// </summary>
    private async Task<(HttpClient client, AuthResponseDto auth)> GetBootstrapAdminAsync()
    {
        var client = CreateClient();
        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequestDto(email: BootstrapAdminEmail, firstName: "Bootstrap", lastName: "Admin", password: Password));

        HttpResponseMessage response = register;
        if (register.StatusCode == HttpStatusCode.Conflict)
        {
            response = await client.PostAsJsonAsync("/api/v1/auth/login",
                new LoginRequestDto(email: BootstrapAdminEmail, password: Password, rememberMe: false));
        }
        response.EnsureSuccessStatusCode();

        var auth = (await response.Content.ReadAsJsonAsync<AuthResponseDto>())!;
        auth.User.IsAdmin.Should().BeTrue("the bootstrap e-mail must be flagged admin at registration");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    [Fact]
    public async Task AdminEndpoints_WithoutToken_Return401()
    {
        var client = CreateClient();

        (await client.GetAsync("/api/v1/admin/stats")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/admin/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PutAsJsonAsync($"/api/v1/admin/users/{Guid.NewGuid()}/admin", new { isAdmin = true }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminEndpoints_AsRegularUser_Return403()
    {
        var (client, auth) = await RegisterUserAsync();
        auth.User.IsAdmin.Should().BeFalse();

        (await client.GetAsync("/api/v1/admin/stats")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/v1/admin/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync($"/api/v1/admin/users/{auth.User.Id}/admin", new { isAdmin = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminEndpoints_WithApiKeyOfAnAdmin_Return403()
    {
        // The admin role only travels in JWTs: an API key never grants admin access.
        var (admin, _) = await GetBootstrapAdminAsync();
        var keyResponse = await admin.PostAsJsonAsync("/api/v1/users/api-keys",
            new CreateApiKeyRequestDto("Admin integration key", "ReadWrite"));
        keyResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var key = (await keyResponse.Content.ReadAsJsonAsync<CreateApiKeyResponseDto>())!;

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key.Key);

        (await client.GetAsync("/api/v1/houses")).StatusCode.Should().Be(HttpStatusCode.OK, "the key itself is valid");
        (await client.GetAsync("/api/v1/admin/stats")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BootstrapAdmin_LoginResponse_FlagsUserAsAdmin()
    {
        await GetBootstrapAdminAsync();

        var client = CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequestDto(email: BootstrapAdminEmail, password: Password, rememberMe: false));
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var auth = await login.Content.ReadAsJsonAsync<AuthResponseDto>();
        auth!.User.IsAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task GetStats_AsAdmin_ReturnsPlatformCounters()
    {
        var (admin, _) = await GetBootstrapAdminAsync();
        await RegisterUserAsync(); // at least one regular user with its auto-created house

        var response = await admin.GetAsync("/api/v1/admin/stats");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stats = await response.Content.ReadAsJsonAsync<AdminStatsDto>();
        stats!.Users.Should().BeGreaterThanOrEqualTo(2);
        stats.Admins.Should().BeGreaterThanOrEqualTo(1);
        stats.Admins.Should().BeLessThanOrEqualTo(stats.Users);
        stats.Houses.Should().BeGreaterThanOrEqualTo(1);
        stats.Devices.Should().BeGreaterThanOrEqualTo(0);
        stats.MaintenanceInstances.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetUsers_AsAdmin_SearchesAndPaginates()
    {
        var (admin, adminAuth) = await GetBootstrapAdminAsync();
        var (_, regular) = await RegisterUserAsync();

        // Search by e-mail returns exactly that user, with its auto-created house counted.
        var search = await admin.GetAsync($"/api/v1/admin/users?search={Uri.EscapeDataString(regular.User.Email)}");
        search.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await search.Content.ReadAsJsonAsync<AdminUsersPageDto>();
        page!.Total.Should().Be(1);
        page.Users.Should().ContainSingle();
        var found = page.Users[0];
        found.Id.Should().Be(regular.User.Id);
        found.Email.Should().Be(regular.User.Email);
        found.IsAdmin.Should().BeFalse();
        found.HousesCount.Should().Be(1);

        // Search is case-insensitive and matches names too.
        var byName = await admin.GetAsync("/api/v1/admin/users?search=BOOTSTRAP");
        var byNamePage = await byName.Content.ReadAsJsonAsync<AdminUsersPageDto>();
        byNamePage!.Users.Should().Contain(u => u.Id == adminAuth.User.Id && u.IsAdmin);

        // Pagination: pageSize is honoured and clamped to 100; total spans all pages.
        var paged = await admin.GetAsync("/api/v1/admin/users?page=1&pageSize=1");
        var pagedPage = await paged.Content.ReadAsJsonAsync<AdminUsersPageDto>();
        pagedPage!.Users.Should().HaveCount(1);
        pagedPage.PageSize.Should().Be(1);
        pagedPage.Total.Should().BeGreaterThanOrEqualTo(2);

        var clamped = await admin.GetAsync("/api/v1/admin/users?pageSize=1000");
        (await clamped.Content.ReadAsJsonAsync<AdminUsersPageDto>())!.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task SetAdmin_GrantThenRevoke_UpdatesRightsOnNextLogin()
    {
        var (admin, _) = await GetBootstrapAdminAsync();
        var (_, regular) = await RegisterUserAsync();

        // Grant
        var grant = await admin.PutAsJsonAsync($"/api/v1/admin/users/{regular.User.Id}/admin", new { isAdmin = true });
        grant.StatusCode.Should().Be(HttpStatusCode.OK);
        (await grant.Content.ReadAsJsonAsync<AdminUserDto>())!.IsAdmin.Should().BeTrue();

        // The promoted user gets the admin role on their next login…
        var promotedClient = CreateClient();
        var login = await promotedClient.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequestDto(email: regular.User.Email, password: Password, rememberMe: false));
        var promotedAuth = (await login.Content.ReadAsJsonAsync<AuthResponseDto>())!;
        promotedAuth.User.IsAdmin.Should().BeTrue();
        promotedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", promotedAuth.AccessToken);
        (await promotedClient.GetAsync("/api/v1/admin/stats")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Revoke
        var revoke = await admin.PutAsJsonAsync($"/api/v1/admin/users/{regular.User.Id}/admin", new { isAdmin = false });
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);
        (await revoke.Content.ReadAsJsonAsync<AdminUserDto>())!.IsAdmin.Should().BeFalse();

        var relogin = await CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequestDto(email: regular.User.Email, password: Password, rememberMe: false));
        (await relogin.Content.ReadAsJsonAsync<AuthResponseDto>())!.User.IsAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task SetAdmin_RevokingOwnRights_Returns400()
    {
        var (admin, adminAuth) = await GetBootstrapAdminAsync();

        var response = await admin.PutAsJsonAsync($"/api/v1/admin/users/{adminAuth.User.Id}/admin", new { isAdmin = false });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Still an admin afterwards.
        (await admin.GetAsync("/api/v1/admin/stats")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SetAdmin_UnknownUser_Returns404()
    {
        var (admin, _) = await GetBootstrapAdminAsync();

        var response = await admin.PutAsJsonAsync($"/api/v1/admin/users/{Guid.NewGuid()}/admin", new { isAdmin = true });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
