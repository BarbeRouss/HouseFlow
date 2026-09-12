using FluentAssertions;
using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Authentication;

/// <summary>
/// Acceptation des CGU à l'inscription (issue #135) et endpoint de ré-acceptation
/// <c>/api/v1/users/me/consent</c>.
/// </summary>
/// <remarks>
/// Rappel juridique : la base légale du compte est l'exécution du contrat (Art. 6(1)(b)).
/// Ce que l'on enregistre est une acceptation contractuelle des CGU + une preuve de prise de
/// connaissance de la politique (Art. 13), pas un consentement de l'Art. 6(1)(a). Le nommage
/// <c>consent</c> est historique.
/// </remarks>
[Collection("Integration")]
public class ConsentTests
{
    private readonly IntegrationTestFixture _fixture;

    public ConsentTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private HttpClient CreateClient() => _fixture.CreateApiClient();

    private static RegisterRequestDto RegisterRequest(bool consentAccepted = true, string? email = null) => new(
        email: email ?? $"consent-{Guid.NewGuid()}@example.com",
        firstName: "Test",
        lastName: "User",
        password: "Password123!",
        consentAccepted: consentAccepted
    );

    private static async Task<AuthResponseDto> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", RegisterRequest());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadAsJsonAsync<AuthResponseDto>())!;
    }

    [Fact]
    public async Task Register_WithoutAcceptingTerms_Returns400()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", RegisterRequest(consentAccepted: false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("terms of service");
    }

    [Fact]
    public async Task Register_WithoutAcceptingTerms_DoesNotCreateTheAccount()
    {
        var client = CreateClient();
        var email = $"refused-{Guid.NewGuid()}@example.com";

        var refused = await client.PostAsJsonAsync("/api/v1/auth/register", RegisterRequest(consentAccepted: false, email: email));
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Aucune écriture n'a eu lieu : la même adresse reste disponible (pas de 409 Conflict).
        var accepted = await client.PostAsJsonAsync("/api/v1/auth/register", RegisterRequest(consentAccepted: true, email: email));
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_WithAcceptedTerms_ReturnsConsentRequiredFalse()
    {
        var client = CreateClient();

        var auth = await RegisterAsync(client);

        auth.User.ConsentRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Login_AfterRegistration_ReturnsConsentRequiredFalse()
    {
        var client = CreateClient();
        var request = RegisterRequest();
        (await client.PostAsJsonAsync("/api/v1/auth/register", request)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequestDto(email: request.Email, password: request.Password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        auth!.User.ConsentRequired.Should().BeFalse();
    }

    [Fact]
    public async Task GetConsentStatus_WithoutAuthentication_Returns401()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/v1/users/me/consent");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetConsentStatus_AfterRegistration_ReturnsTheAcceptedVersion()
    {
        var client = CreateClient();
        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/users/me/consent");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await response.Content.ReadAsJsonAsync<ConsentStatusDto>();
        status!.ConsentRequired.Should().BeFalse();
        status.ConsentGivenAt.Should().NotBeNull();
        status.ConsentPolicyVersion.Should().Be(GdprPolicy.CurrentPolicyVersion);
        status.CurrentPolicyVersion.Should().Be(GdprPolicy.CurrentPolicyVersion);
    }

    [Fact]
    public async Task RecordConsent_WithCurrentPolicyVersion_Returns200AndStoresIt()
    {
        var client = CreateClient();
        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/users/me/consent",
            new { accepted = true, policyVersion = GdprPolicy.CurrentPolicyVersion });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await response.Content.ReadAsJsonAsync<ConsentStatusDto>();
        status!.ConsentRequired.Should().BeFalse();
        status.ConsentPolicyVersion.Should().Be(GdprPolicy.CurrentPolicyVersion);
        status.ConsentGivenAt.Should().NotBeNull();

        // L'acceptation est persistée : elle est relue à l'identique.
        var reread = await client.GetAsync("/api/v1/users/me/consent");
        var rereadStatus = await reread.Content.ReadAsJsonAsync<ConsentStatusDto>();
        rereadStatus!.ConsentGivenAt.Should().BeCloseTo(status.ConsentGivenAt!.Value, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RecordConsent_WhenNotAccepted_Returns400()
    {
        var client = CreateClient();
        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/users/me/consent",
            new { accepted = false, policyVersion = GdprPolicy.CurrentPolicyVersion });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RecordConsent_WithUnknownPolicyVersion_Returns400()
    {
        var client = CreateClient();
        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/users/me/consent",
            new { accepted = true, policyVersion = "1900-01-01" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RecordConsent_WithoutAuthentication_Returns401()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/users/me/consent",
            new { accepted = true, policyVersion = GdprPolicy.CurrentPolicyVersion });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
