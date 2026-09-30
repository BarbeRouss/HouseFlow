using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Contract;

/// <summary>
/// The error contract (ProblemDetails, <c>application/problem+json</c> and a <c>code</c> on every
/// 4xx the API or the framework produces) and a few response-shape rules of specs/openapi.yaml.
/// </summary>
[Collection("Integration")]
public class ApiConsistencyTests
{
    private const string Password = "Password123!";
    private readonly IntegrationTestFixture _fixture;

    public ApiConsistencyTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<HttpClient> RegisterAsync()
    {
        var client = _fixture.CreateApiClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequestDto(email: $"contract-{Guid.NewGuid():N}@example.com", firstName: "Con", lastName: "Tract",
                password: Password, consentAccepted: true));
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return client;
    }

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetInt32().Should().Be((int)status);
        body.RootElement.GetProperty("code").GetString().Should().Be(code);
    }

    // ---------------------------------------------------------------- ProblemDetails

    [Fact]
    public async Task UnknownHouse_Is404ProblemJson_WithCode()
    {
        var client = await RegisterAsync();

        await ShouldBeProblemAsync(await client.GetAsync($"/api/v1/houses/{Guid.NewGuid()}"),
            HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task MissingAccessToken_Is401ProblemJson_WithCode()
    {
        var anonymous = _fixture.CreateApiClient();

        await ShouldBeProblemAsync(await anonymous.GetAsync("/api/v1/houses"),
            HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task ModelValidationError_Is400ProblemJson_WithCodeAndErrors()
    {
        var anonymous = _fixture.CreateApiClient();

        var response = await anonymous.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequestDto(email: "not-an-email", firstName: "A", lastName: "B", password: Password, consentAccepted: true));

        await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task BusinessRule400WithoutDedicatedCode_CarriesValidationFailed()
    {
        var client = await RegisterAsync();
        var houseId = await client.CreateHouseAsync();
        var device = await (await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/devices",
                new CreateDeviceRequestDto(maintenanceType: null, name: "Chaudière", type: "Chaudière Gaz", brand: null, model: null, installDate: null)))
            .Content.ReadAsJsonAsync<DeviceDto>();

        // A custom periodicity without its interval is refused by the service (InvalidOperationException).
        var response = await client.PostAsJsonAsync($"/api/v1/devices/{device!.Id}/maintenance-types",
            new CreateMaintenanceTypeRequestDto("Sur mesure", Core.Entities.Periodicity.Custom, null));

        await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task RouteConstraintMismatch_Is404ProblemJson_WithCode()
    {
        var client = await RegisterAsync();

        await ShouldBeProblemAsync(await client.PostAsync("/api/v1/invitations/not-a-guid/resend", null),
            HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task DeletedAccount_ItsStillValidAccessToken_Gets401()
    {
        var client = await RegisterAsync();
        var delete = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/me")
        {
            Content = JsonContent.Create(new DeleteAccountRequestDto(password: Password))
        };
        (await client.SendAsync(delete)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The JWT is stateless and stays valid for up to 15 minutes: the account is gone, so this
        // is an authentication failure (the client drops the session), not a missing resource.
        await ShouldBeProblemAsync(await client.GetAsync("/api/v1/users/me"),
            HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task DeleteAccount_WithWrongPassword_Is400WrongPassword()
    {
        var client = await RegisterAsync();
        var delete = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/me")
        {
            Content = JsonContent.Create(new DeleteAccountRequestDto(password: "NotMyPassword123!"))
        };

        await ShouldBeProblemAsync(await client.SendAsync(delete), HttpStatusCode.BadRequest, ErrorCodes.WrongPassword);
    }

    // ---------------------------------------------------------------- response shapes

    [Fact]
    public async Task EmptiedAddress_IsStoredAndReturnedAsNull()
    {
        var client = await RegisterAsync();
        var created = await client.PostAsJsonAsync("/api/v1/houses",
            new CreateHouseRequestDto(colorKey: null, address: "12 rue des Lilas", city: "Lyon", name: "Maison", zipCode: "69001"));
        var house = await created.Content.ReadAsJsonAsync<HouseDto>();

        var updated = await client.PutAsJsonAsync($"/api/v1/houses/{house!.Id}",
            new UpdateHouseRequestDto(address: "", city: "  ", colorKey: null, name: null, zipCode: null));

        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await updated.Content.ReadAsJsonAsync<HouseDto>();
        result!.Address.Should().BeNull();
        result.City.Should().BeNull();
        result.ZipCode.Should().Be("69001", "an omitted field is kept");
    }

    [Fact]
    public async Task Members_ReportEffectiveRights_ForTheOwner()
    {
        var client = await RegisterAsync();
        var houseId = await client.CreateHouseAsync();

        var members = await (await client.GetAsync($"/api/v1/houses/{houseId}/members"))
            .Content.ReadAsJsonAsync<List<HouseMemberDto>>();

        var owner = members!.Single();
        owner.CanViewCosts.Should().BeTrue("the owner always sees costs (R5)");
        owner.CanLogMaintenance.Should().BeTrue();
    }

    [Fact]
    public async Task ExportFileName_CarriesTheParisCalendarDay()
    {
        var client = await RegisterAsync();
        var before = ParisClock.Today();

        var response = await client.GetAsync("/api/v1/users/me/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var fileName = response.Content.Headers.ContentDisposition!.FileName!.Trim('"');
        var after = ParisClock.Today();
        fileName.Should().BeOneOf(
            $"houseflow-data-export-{before:yyyy-MM-dd}.json",
            $"houseflow-data-export-{after:yyyy-MM-dd}.json");
    }
}
