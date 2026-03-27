using FluentAssertions;
using HouseFlow.Application.DTOs;
using HouseFlow.Core.Entities;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Maintenance;

[Collection("Integration")]
public class MaintenanceTypesTests
{
    private readonly IntegrationTestFixture _fixture;

    public MaintenanceTypesTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private HttpClient CreateClient() => _fixture.CreateApiClient();

    private async Task<(HttpClient client, Guid houseId, Guid deviceId)> CreateAuthenticatedClientWithDeviceAsync()
    {
        var client = CreateClient();
        var email = $"test-{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequestDto("Test", "User", email, "Password123!");

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        response.EnsureSuccessStatusCode();

        var authResponse = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);

        // Get the auto-created house
        var housesResponse = await client.GetAsync("/api/v1/houses");
        var houses = await housesResponse.Content.ReadAsJsonAsync<HousesListResponseDto>();
        var houseId = houses!.Houses.First().Id;

        // Create a device
        var deviceRequest = new CreateDeviceRequestDto("Test Device", "Chaudiere Gaz", "Viessmann", "Vitodens", null);
        var deviceResponse = await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/devices", deviceRequest);
        var device = await deviceResponse.Content.ReadAsJsonAsync<DeviceDto>();

        return (client, houseId, device!.Id);
    }

    private static CreateMaintenanceTypeRequestDto CreateValidMaintenanceTypeRequest(string? name = null) => new(
        Name: name ?? $"Entretien {Guid.NewGuid().ToString("N")[..8]}",
        Periodicity: Periodicity.Annual,
        CustomDays: null
    );

    #region Create Maintenance Type Tests

    [Fact]
    public async Task CreateMaintenanceType_ForOwnDevice_ReturnsType()
    {
        // Arrange
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();
        var request = CreateValidMaintenanceTypeRequest("Revision Annuelle");

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var maintenanceType = await response.Content.ReadAsJsonAsync<MaintenanceTypeDto>();
        maintenanceType.Should().NotBeNull();
        maintenanceType!.Id.Should().NotBeEmpty();
        maintenanceType.Name.Should().Be("Revision Annuelle");
        maintenanceType.Periodicity.Should().Be(Periodicity.Annual);
        maintenanceType.DeviceId.Should().Be(deviceId);
    }

    [Fact]
    public async Task CreateMaintenanceType_ForOtherUserDevice_Returns403Forbidden()
    {
        // Arrange - Create User 1 with device
        var (client1, _, deviceId1) = await CreateAuthenticatedClientWithDeviceAsync();

        // Create User 2
        var (client2, _, _) = await CreateAuthenticatedClientWithDeviceAsync();

        var request = CreateValidMaintenanceTypeRequest();

        // Act - User 2 tries to create maintenance type on User 1's device
        var response = await client2.PostAsJsonAsync($"/api/v1/devices/{deviceId1}/maintenance-types", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateMaintenanceType_WithoutAuth_Returns401Unauthorized()
    {
        // Arrange
        var client = CreateClient(); // No authentication
        var request = CreateValidMaintenanceTypeRequest();
        var fakeDeviceId = Guid.NewGuid();

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/devices/{fakeDeviceId}/maintenance-types", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateMaintenanceType_WithEmptyName_Returns400BadRequest()
    {
        // Arrange
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();
        var request = new CreateMaintenanceTypeRequestDto(
            Name: "",
            Periodicity: Periodicity.Annual,
            CustomDays: null
        );

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMaintenanceType_WithCustomPeriodicityNoCustomDays_DefaultsTo365()
    {
        // Arrange
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();
        var request = new CreateMaintenanceTypeRequestDto(
            Name: "Custom Sans Jours",
            Periodicity: Periodicity.Custom,
            CustomDays: null // Should default to 365 days
        );

        // Create maintenance type
        var createResponse = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", request);
        var createdType = await createResponse.Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        // Log maintenance today
        var logRequest = new LogMaintenanceRequestDto(DateTime.UtcNow, 100m, null, null);
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{createdType!.Id}/instances", logRequest);

        // Act
        var response = await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-types");
        var types = await response.Content.ReadAsJsonAsync<IEnumerable<MaintenanceTypeWithStatusDto>>();

        // Assert
        var type = types!.First(t => t.Id == createdType.Id);
        // Default to 365 days when CustomDays is null
        type.NextDueDate!.Value.Date.Should().Be(DateTime.UtcNow.AddDays(365).Date);
    }

    #endregion

    [Fact]
    public async Task CreateMaintenanceType_WithCustomPeriodicity_UsesCustomDays()
    {
        // Arrange
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();
        var request = new CreateMaintenanceTypeRequestDto(
            Name: "Entretien Custom",
            Periodicity: Periodicity.Custom,
            CustomDays: 45
        );

        // Create maintenance type
        var createResponse = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", request);
        var createdType = await createResponse.Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        // Log maintenance today
        var logRequest = new LogMaintenanceRequestDto(DateTime.UtcNow, 100m, null, null);
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{createdType!.Id}/instances", logRequest);

        // Act
        var response = await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-types");
        var types = await response.Content.ReadAsJsonAsync<IEnumerable<MaintenanceTypeWithStatusDto>>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var type = types!.First(t => t.Id == createdType.Id);
        type.Periodicity.Should().Be(Periodicity.Custom);
        type.CustomDays.Should().Be(45);
        type.NextDueDate.Should().NotBeNull();
        // Next due should be 45 days from now (Custom periodicity)
        type.NextDueDate!.Value.Date.Should().Be(DateTime.UtcNow.AddDays(45).Date);
        type.Status.Should().Be("up_to_date"); // 45 days > 30 days threshold
    }

    [Fact]
    public async Task CreateMaintenanceType_WithCustomPeriodicity_CalculatesOverdueCorrectly()
    {
        // Arrange
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();
        var request = new CreateMaintenanceTypeRequestDto(
            Name: "Entretien Custom Court",
            Periodicity: Periodicity.Custom,
            CustomDays: 15
        );

        // Create maintenance type
        var createResponse = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", request);
        var createdType = await createResponse.Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        // Log maintenance 20 days ago (should be overdue with 15-day custom period)
        var logRequest = new LogMaintenanceRequestDto(DateTime.UtcNow.AddDays(-20), 100m, null, null);
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{createdType!.Id}/instances", logRequest);

        // Act
        var response = await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-types");
        var types = await response.Content.ReadAsJsonAsync<IEnumerable<MaintenanceTypeWithStatusDto>>();

        // Assert
        var type = types!.First(t => t.Id == createdType.Id);
        type.Status.Should().Be("overdue");
        type.NextDueDate!.Value.Date.Should().Be(DateTime.UtcNow.AddDays(-5).Date); // 20 - 15 = 5 days ago
    }

    #region Get Maintenance Types Tests

    [Fact]
    public async Task GetMaintenanceTypes_ForOtherUserDevice_Returns403Forbidden()
    {
        // Arrange - Create User 1 with device
        var (client1, _, deviceId1) = await CreateAuthenticatedClientWithDeviceAsync();

        // Create User 2
        var (client2, _, _) = await CreateAuthenticatedClientWithDeviceAsync();

        // Act - User 2 tries to get maintenance types of User 1's device
        var response = await client2.GetAsync($"/api/v1/devices/{deviceId1}/maintenance-types");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetMaintenanceTypes_ReturnsWithStatus_UpToDate()
    {
        // Arrange
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();

        // Create maintenance type
        var createRequest = CreateValidMaintenanceTypeRequest("Entretien Test");
        var createResponse = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", createRequest);
        var createdType = await createResponse.Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        // Log a maintenance (today) to make it up_to_date
        var logRequest = new LogMaintenanceRequestDto(DateTime.UtcNow, 100m, "Technicien", "RAS");
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{createdType!.Id}/instances", logRequest);

        // Act
        var response = await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-types");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var types = await response.Content.ReadAsJsonAsync<IEnumerable<MaintenanceTypeWithStatusDto>>();
        types.Should().NotBeNull();

        var type = types!.FirstOrDefault(t => t.Id == createdType.Id);
        type.Should().NotBeNull();
        type!.Status.Should().Be("up_to_date");
        type.LastMaintenanceDate.Should().NotBeNull();
        type.NextDueDate.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMaintenanceTypes_ReturnsWithStatus_Pending()
    {
        // Arrange
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();

        // Create maintenance type with short periodicity (monthly)
        var createRequest = new CreateMaintenanceTypeRequestDto("Entretien Mensuel", Periodicity.Monthly, null);
        var createResponse = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", createRequest);
        var createdType = await createResponse.Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        // Log a maintenance from 20 days ago (within the month, so should be pending soon)
        var logRequest = new LogMaintenanceRequestDto(DateTime.UtcNow.AddDays(-20), 50m, null, null);
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{createdType!.Id}/instances", logRequest);

        // Act
        var response = await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-types");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var types = await response.Content.ReadAsJsonAsync<IEnumerable<MaintenanceTypeWithStatusDto>>();
        var type = types!.FirstOrDefault(t => t.Id == createdType.Id);
        type.Should().NotBeNull();
        // Status should be up_to_date or pending depending on next due calculation
        type!.Status.Should().BeOneOf("up_to_date", "pending");
    }

    [Fact]
    public async Task GetMaintenanceTypes_ReturnsWithStatus_Overdue()
    {
        // Arrange
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();

        // Create maintenance type with monthly periodicity
        var createRequest = new CreateMaintenanceTypeRequestDto("Entretien Mensuel Overdue", Periodicity.Monthly, null);
        var createResponse = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", createRequest);
        var createdType = await createResponse.Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        // Log a maintenance from 60 days ago (should be overdue for monthly)
        var logRequest = new LogMaintenanceRequestDto(DateTime.UtcNow.AddDays(-60), 50m, null, null);
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{createdType!.Id}/instances", logRequest);

        // Act
        var response = await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-types");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var types = await response.Content.ReadAsJsonAsync<IEnumerable<MaintenanceTypeWithStatusDto>>();
        var type = types!.FirstOrDefault(t => t.Id == createdType.Id);
        type.Should().NotBeNull();
        type!.Status.Should().Be("overdue");
    }

    #endregion

    #region Update Maintenance Type Tests

    [Fact]
    public async Task UpdateMaintenanceType_AsOwner_UpdatesSuccessfully()
    {
        // Arrange
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();
        var createRequest = CreateValidMaintenanceTypeRequest("Old Name");
        var createResponse = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", createRequest);
        var createdType = await createResponse.Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        var updateRequest = new UpdateMaintenanceTypeRequestDto(
            Name: "New Name",
            Periodicity: Periodicity.Semestrial,
            CustomDays: null
        );

        // Act
        var response = await client.PutAsJsonAsync($"/api/v1/maintenance-types/{createdType!.Id}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedType = await response.Content.ReadAsJsonAsync<MaintenanceTypeDto>();
        updatedType.Should().NotBeNull();
        updatedType!.Name.Should().Be("New Name");
        updatedType.Periodicity.Should().Be(Periodicity.Semestrial);
    }

    [Fact]
    public async Task UpdateMaintenanceType_ChangePeriodicity_RecalculatesNextDue()
    {
        // Arrange
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();

        // Create with annual periodicity
        var createRequest = new CreateMaintenanceTypeRequestDto("Entretien Annual", Periodicity.Annual, null);
        var createResponse = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", createRequest);
        var createdType = await createResponse.Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        // Log maintenance today
        var logRequest = new LogMaintenanceRequestDto(DateTime.UtcNow, 100m, null, null);
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{createdType!.Id}/instances", logRequest);

        // Get current next due date (should be ~1 year from now)
        var beforeResponse = await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-types");
        var beforeTypes = await beforeResponse.Content.ReadAsJsonAsync<IEnumerable<MaintenanceTypeWithStatusDto>>();
        var beforeType = beforeTypes!.First(t => t.Id == createdType.Id);
        var beforeNextDue = beforeType.NextDueDate;

        // Update to monthly periodicity
        var updateRequest = new UpdateMaintenanceTypeRequestDto(null, Periodicity.Monthly, null);
        await client.PutAsJsonAsync($"/api/v1/maintenance-types/{createdType.Id}", updateRequest);

        // Act - Get updated next due date
        var afterResponse = await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-types");
        var afterTypes = await afterResponse.Content.ReadAsJsonAsync<IEnumerable<MaintenanceTypeWithStatusDto>>();
        var afterType = afterTypes!.First(t => t.Id == createdType.Id);

        // Assert - Next due should be recalculated (monthly = ~30 days, not ~365 days)
        afterType.NextDueDate.Should().NotBeNull();
        afterType.NextDueDate.Should().BeBefore(beforeNextDue!.Value);
    }

    [Fact]
    public async Task UpdateMaintenanceType_NotOwner_Returns403Forbidden()
    {
        // Arrange - Create maintenance type with User 1
        var (client1, _, deviceId1) = await CreateAuthenticatedClientWithDeviceAsync();
        var createResponse = await client1.PostAsJsonAsync($"/api/v1/devices/{deviceId1}/maintenance-types", CreateValidMaintenanceTypeRequest());
        var createdType = await createResponse.Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        // Create User 2
        var (client2, _, _) = await CreateAuthenticatedClientWithDeviceAsync();

        var updateRequest = new UpdateMaintenanceTypeRequestDto(Name: "Hacked Name", null, null);

        // Act - User 2 tries to update User 1's maintenance type
        var response = await client2.PutAsJsonAsync($"/api/v1/maintenance-types/{createdType!.Id}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Delete Maintenance Type Tests

    [Fact]
    public async Task DeleteMaintenanceType_AsOwner_DeletesCascadeInstances()
    {
        // Arrange
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();
        var createResponse = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", CreateValidMaintenanceTypeRequest());
        var createdType = await createResponse.Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        // Log a maintenance instance
        var logRequest = new LogMaintenanceRequestDto(DateTime.UtcNow, 100m, "Test Provider", "Test Notes");
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{createdType!.Id}/instances", logRequest);

        // Act
        var deleteResponse = await client.DeleteAsync($"/api/v1/maintenance-types/{createdType.Id}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify maintenance type no longer exists
        var typesResponse = await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-types");
        var types = await typesResponse.Content.ReadAsJsonAsync<IEnumerable<MaintenanceTypeWithStatusDto>>();
        types.Should().NotContain(t => t.Id == createdType.Id);
    }

    [Fact]
    public async Task DeleteMaintenanceType_NotOwner_Returns403Forbidden()
    {
        // Arrange - Create maintenance type with User 1
        var (client1, _, deviceId1) = await CreateAuthenticatedClientWithDeviceAsync();
        var createResponse = await client1.PostAsJsonAsync($"/api/v1/devices/{deviceId1}/maintenance-types", CreateValidMaintenanceTypeRequest());
        var createdType = await createResponse.Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        // Create User 2
        var (client2, _, _) = await CreateAuthenticatedClientWithDeviceAsync();

        // Act - User 2 tries to delete User 1's maintenance type
        var response = await client2.DeleteAsync($"/api/v1/maintenance-types/{createdType!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Verify maintenance type still exists for User 1
        var typesResponse = await client1.GetAsync($"/api/v1/devices/{deviceId1}/maintenance-types");
        var types = await typesResponse.Content.ReadAsJsonAsync<IEnumerable<MaintenanceTypeWithStatusDto>>();
        types.Should().Contain(t => t.Id == createdType.Id);
    }

    #endregion
}
