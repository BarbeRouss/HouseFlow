using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using HouseFlow.Application.DTOs;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Users;

/// <summary>
/// Droits RGPD en self-service : accès (Art. 15), rectification (Art. 16),
/// effacement (Art. 17), portabilité (Art. 20).
/// </summary>
[Collection("Integration")]
public class UserAccountTests
{
    private const string Password = "Password123!";

    private readonly IntegrationTestFixture _fixture;

    public UserAccountTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(HttpClient Client, string Email, string? RefreshToken)> RegisterAsync()
    {
        var client = _fixture.CreateApiClient();
        var email = $"gdpr-{Guid.NewGuid()}@example.com";
        var request = new RegisterRequestDto(
            email: email, firstName: "Test", lastName: "User", password: Password, consentAccepted: true);

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        return (client, email, ExtractRefreshToken(response));
    }

    /// <summary>Le client de test n'a pas de cookie container (UseCookies = false).</summary>
    private static string? ExtractRefreshToken(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;

        return cookies
            .Select(c => c.Split(';')[0].Trim())
            .Where(c => c.StartsWith("refreshToken=", StringComparison.Ordinal))
            .Select(c => c["refreshToken=".Length..])
            .FirstOrDefault(v => !string.IsNullOrEmpty(v));
    }

    // ====================================================================
    // Art. 15 — profil
    // ====================================================================

    [Fact]
    public async Task GetMyProfile_WithoutAuth_Returns401()
    {
        var client = _fixture.CreateApiClient();

        var response = await client.GetAsync("/api/v1/users/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMyProfile_ReturnsProfileWithConsentState()
    {
        var (client, email, _) = await RegisterAsync();

        var response = await client.GetAsync("/api/v1/users/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.Content.ReadAsJsonAsync<UserProfileDto>();
        profile!.Email.Should().Be(email);
        profile.FirstName.Should().Be("Test");
        profile.Id.Should().NotBeEmpty();
        profile.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));

        // L'état d'acceptation de la politique est exposé au frontend (bannière de
        // re-consentement) ; la valeur dépend de l'enregistrement du consentement à
        // l'inscription, qui relève de l'endpoint /users/me/consent.
        profile.ConsentRequired.Should().Be(
            profile.ConsentGivenAt is null || profile.ConsentPolicyVersion != "2026-09-11");
    }

    // ====================================================================
    // Art. 16 — rectification
    // ====================================================================

    [Fact]
    public async Task UpdateMyProfile_WithValidData_Returns200AndPersists()
    {
        var (client, _, _) = await RegisterAsync();
        var newEmail = $"renamed-{Guid.NewGuid()}@example.com";

        var response = await client.PutAsJsonAsync("/api/v1/users/me",
            new UpdateProfileRequestDto(email: newEmail, firstName: "Renamed", lastName: "Person"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.Content.ReadAsJsonAsync<UserProfileDto>();
        profile!.FirstName.Should().Be("Renamed");
        profile.Email.Should().Be(newEmail);

        // Le JWT courant reste valable : l'identité repose sur le claim `sub`.
        var reread = await client.GetAsync("/api/v1/users/me");
        var persisted = await reread.Content.ReadAsJsonAsync<UserProfileDto>();
        persisted!.Email.Should().Be(newEmail);
    }

    [Fact]
    public async Task UpdateMyProfile_WithEmailOfAnotherAccount_Returns409()
    {
        var (_, takenEmail, _) = await RegisterAsync();
        var (client, _, _) = await RegisterAsync();

        var response = await client.PutAsJsonAsync("/api/v1/users/me",
            new UpdateProfileRequestDto(email: takenEmail, firstName: "Test", lastName: "User"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ====================================================================
    // Art. 17 — effacement
    // ====================================================================

    [Fact]
    public async Task DeleteMyAccount_WithoutAuth_Returns401()
    {
        var client = _fixture.CreateApiClient();

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/me")
        {
            Content = JsonContent.Create(new DeleteAccountRequestDto(password: Password))
        };
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteMyAccount_WithWrongPassword_Returns400()
    {
        var (client, _, _) = await RegisterAsync();

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/me")
        {
            Content = JsonContent.Create(new DeleteAccountRequestDto(password: "NotMyPassword123!"))
        };
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Le compte est intact.
        (await client.GetAsync("/api/v1/users/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteMyAccount_Returns204ThenLoginAndRefreshAreRejected()
    {
        var (client, email, refreshToken) = await RegisterAsync();
        refreshToken.Should().NotBeNullOrEmpty();

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/me")
        {
            Content = JsonContent.Create(new DeleteAccountRequestDto(password: Password))
        };
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());

        var anonymous = _fixture.CreateApiClient();

        var login = await anonymous.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequestDto(email: email, password: Password));
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        refresh.Headers.Add("Cookie", $"refreshToken={refreshToken}");
        (await anonymous.SendAsync(refresh)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Art. 17 / considérant 26 : plus aucune trace identifiante — ni l'email (y compris dans
        // les entrées écrites à l'inscription : maison par défaut, adhésion, premier jeton),
        // ni l'UUID du compte, ni de valeurs avant/après.
        await using var db = await _fixture.CreateDbContextAsync();
        var residual = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Username == email
                     || (a.OldValues != null && a.OldValues.Contains(email))
                     || (a.NewValues != null && a.NewValues.Contains(email)))
            .CountAsync();
        residual.Should().Be(0, "no audit log may still carry the deleted user's email");

        var deletedTrace = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "AccountDeleted" && a.EntityId == "deleted" && a.Username == "deleted-user")
            .CountAsync();
        deletedTrace.Should().BeGreaterThan(0);
    }

    // ====================================================================
    // Art. 15 + 20 — export
    // ====================================================================

    [Fact]
    public async Task ExportMyData_ReturnsJsonAttachment_ThenRateLimits()
    {
        var (client, email, _) = await RegisterAsync();

        var response = await client.GetAsync("/api/v1/users/me/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName.Should().MatchRegex(
            @"^""?houseflow-data-export-\d{4}-\d{2}-\d{2}\.json""?$");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("formatVersion").GetString().Should().Be("1.0");
        root.GetProperty("profile").GetProperty("email").GetString().Should().Be(email);
        root.GetProperty("houses").GetArrayLength().Should().Be(1); // la maison créée à l'inscription
        foreach (var section in new[] { "preferences", "consent", "memberships", "invitationsSent", "invitationsReceived", "apiKeys", "sessions", "auditLogs", "information" })
        {
            root.TryGetProperty(section, out _).Should().BeTrue($"la section '{section}' doit être présente");
        }
        root.GetProperty("information").GetProperty("contactEmail").GetString().Should().Be("privacy@houseflow.app");

        // Un export par heure et par utilisateur (Art. 12(5)).
        var second = await client.GetAsync("/api/v1/users/me/export");
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        second.Headers.Should().ContainKey("Retry-After");
    }

    [Fact]
    public async Task ExportMyData_WithCsvFormat_ReturnsZipArchive()
    {
        var (client, _, _) = await RegisterAsync();

        var response = await client.GetAsync("/api/v1/users/me/export?format=csv");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        response.Content.Headers.ContentDisposition!.FileName.Should().MatchRegex(
            @"^""?houseflow-data-export-\d{4}-\d{2}-\d{2}\.zip""?$");

        using var archive = new ZipArchive(await response.Content.ReadAsStreamAsync());
        archive.Entries.Select(e => e.FullName).Should().Contain(["profile.csv", "houses.csv", "README.txt"]);
    }

    [Fact]
    public async Task ExportMyData_WithUnknownFormat_Returns400()
    {
        var (client, _, _) = await RegisterAsync();

        var response = await client.GetAsync("/api/v1/users/me/export?format=pdf");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExportMyData_WithoutAuth_Returns401()
    {
        var client = _fixture.CreateApiClient();

        var response = await client.GetAsync("/api/v1/users/me/export");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
