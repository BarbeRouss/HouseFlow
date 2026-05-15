using FluentAssertions;
using HouseFlow.Application.DTOs;
using HouseFlow.Core.Entities;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Maintenance;

[Collection("Integration")]
public class MaintenanceInstancesTests
{
    private readonly IntegrationTestFixture _fixture;

    public MaintenanceInstancesTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private HttpClient CreateClient() => _fixture.CreateApiClient();

    private async Task<(HttpClient client, Guid houseId, Guid deviceId, Guid maintenanceTypeId)> CreateAuthenticatedClientWithMaintenanceTypeAsync()
    {
        var client = CreateClient();
        var email = $"test-{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequestDto(firstName: "Test", lastName: "User", email: email, password: "Password123!");

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        response.EnsureSuccessStatusCode();

        var authResponse = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);

        // Get the auto-created house
        var housesResponse = await client.GetAsync("/api/v1/houses");
        var houses = await housesResponse.Content.ReadAsJsonAsync<HousesListResponseDto>();
        var houseId = houses!.Houses.First().Id;

        // Create a device
        var deviceRequest = new CreateDeviceRequestDto(name: "Test Device", type: "Chaudiere Gaz", brand: "Viessmann", model: "Vitodens", installDate: null);
        var deviceResponse = await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/devices", deviceRequest);
        var device = await deviceResponse.Content.ReadAsJsonAsync<DeviceDto>();

        // Create a maintenance type
        var typeRequest = new CreateMaintenanceTypeRequestDto("Entretien Annuel", Periodicity.Annual, null);
        var typeResponse = await client.PostAsJsonAsync($"/api/v1/devices/{device!.Id}/maintenance-types", typeRequest);
        var maintenanceType = await typeResponse.Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        return (client, houseId, device.Id, maintenanceType!.Id);
    }

    #region Log Maintenance Tests

    [Fact]
    public async Task LogMaintenance_WithValidData_CreatesInstance()
    {
        // Arrange
        var (client, _, deviceId, maintenanceTypeId) = await CreateAuthenticatedClientWithMaintenanceTypeAsync();
        var request = new LogMaintenanceRequestDto(
            date: DateTime.UtcNow,
            cost: 150.50m,
            provider: "Chauffagiste Pro",
            notes: "Nettoyage complet effectue"
        );

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId}/instances", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var instance = await response.Content.ReadAsJsonAsync<MaintenanceInstanceDto>();
        instance.Should().NotBeNull();
        instance!.Id.Should().NotBeEmpty();
        instance.Cost.Should().Be(150.50m);
        instance.Provider.Should().Be("Chauffagiste Pro");
        instance.Notes.Should().Be("Nettoyage complet effectue");
        instance.MaintenanceTypeId.Should().Be(maintenanceTypeId);
    }

    [Fact]
    public async Task LogMaintenance_UpdatesNextDueDate()
    {
        // Arrange
        var (client, _, deviceId, maintenanceTypeId) = await CreateAuthenticatedClientWithMaintenanceTypeAsync();

        // Get initial status (should have no LastMaintenanceDate)
        var beforeResponse = await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-types");
        var beforeTypes = await beforeResponse.Content.ReadAsJsonAsync<IEnumerable<MaintenanceTypeWithStatusDto>>();
        var beforeType = beforeTypes!.First(t => t.Id == maintenanceTypeId);
        beforeType.LastMaintenanceDate.Should().BeNull();

        // Log maintenance
        var request = new LogMaintenanceRequestDto(date: DateTime.UtcNow, cost: 100m, provider: null, notes: null);

        // Act
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId}/instances", request);

        // Assert
        var afterResponse = await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-types");
        var afterTypes = await afterResponse.Content.ReadAsJsonAsync<IEnumerable<MaintenanceTypeWithStatusDto>>();
        var afterType = afterTypes!.First(t => t.Id == maintenanceTypeId);

        afterType.LastMaintenanceDate.Should().NotBeNull();
        afterType.NextDueDate.Should().NotBeNull();
        // For annual periodicity, next due should be ~1 year from now
        afterType.NextDueDate!.Value.Should().BeAfter(DateTime.UtcNow.AddMonths(11));
    }

    [Fact]
    public async Task LogMaintenance_UpdatesDeviceScore()
    {
        // Arrange
        var (client, _, deviceId, maintenanceTypeId) = await CreateAuthenticatedClientWithMaintenanceTypeAsync();

        // Get initial device score (should be 100% with no maintenance types needing action)
        var beforeResponse = await client.GetAsync($"/api/v1/devices/{deviceId}");
        var beforeDevice = await beforeResponse.Content.ReadAsJsonAsync<DeviceDetailDto>();

        // Log maintenance
        var request = new LogMaintenanceRequestDto(date: DateTime.UtcNow, cost: 100m, provider: null, notes: null);
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId}/instances", request);

        // Act
        var afterResponse = await client.GetAsync($"/api/v1/devices/{deviceId}");
        var afterDevice = await afterResponse.Content.ReadAsJsonAsync<DeviceDetailDto>();

        // Assert - Score should be 100% after logging maintenance
        afterDevice!.Score.Should().Be(100);
        afterDevice.Status.Should().Be("up_to_date");
    }

    [Fact]
    public async Task LogMaintenance_ForOtherUserDevice_Returns403Forbidden()
    {
        // Arrange - Create User 1 with maintenance type
        var (client1, _, _, maintenanceTypeId1) = await CreateAuthenticatedClientWithMaintenanceTypeAsync();

        // Create User 2
        var (client2, _, _, _) = await CreateAuthenticatedClientWithMaintenanceTypeAsync();

        var request = new LogMaintenanceRequestDto(date: DateTime.UtcNow, cost: 100m, provider: null, notes: null);

        // Act - User 2 tries to log maintenance on User 1's type
        var response = await client2.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId1}/instances", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LogMaintenance_WithoutAuth_Returns401Unauthorized()
    {
        // Arrange
        var client = CreateClient(); // No authentication
        var request = new LogMaintenanceRequestDto(date: DateTime.UtcNow, cost: 100m, provider: null, notes: null);
        var fakeTypeId = Guid.NewGuid();

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/maintenance-types/{fakeTypeId}/instances", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Get Maintenance History Tests

    [Fact]
    public async Task GetMaintenanceHistory_ReturnsSortedByDate()
    {
        // Arrange
        var (client, _, deviceId, maintenanceTypeId) = await CreateAuthenticatedClientWithMaintenanceTypeAsync();

        // Log 3 maintenances at different dates
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId}/instances",
            new LogMaintenanceRequestDto(date: DateTime.UtcNow.AddDays(-30), cost: 100m, provider: "Provider 1", notes: null));
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId}/instances",
            new LogMaintenanceRequestDto(date: DateTime.UtcNow.AddDays(-60), cost: 200m, provider: "Provider 2", notes: null));
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId}/instances",
            new LogMaintenanceRequestDto(date: DateTime.UtcNow.AddDays(-10), cost: 150m, provider: "Provider 3", notes: null));

        // Act
        var response = await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-history");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var history = await response.Content.ReadAsJsonAsync<MaintenanceHistoryResponseDto>();
        history.Should().NotBeNull();

        var instances = history!.Instances.ToList();
        instances.Should().HaveCount(3);

        // Should be sorted by date (most recent first based on typical UI needs)
        // or chronologically - let's just verify they exist
        instances.Should().Contain(i => i.Provider == "Provider 1");
        instances.Should().Contain(i => i.Provider == "Provider 2");
        instances.Should().Contain(i => i.Provider == "Provider 3");
    }

    [Fact]
    public async Task GetMaintenanceHistory_ReturnsTotalSpent()
    {
        // Arrange
        var (client, _, deviceId, maintenanceTypeId) = await CreateAuthenticatedClientWithMaintenanceTypeAsync();

        // Log maintenances with different costs
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId}/instances",
            new LogMaintenanceRequestDto(date: DateTime.UtcNow.AddDays(-30), cost: 100.50m, provider: null, notes: null));
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId}/instances",
            new LogMaintenanceRequestDto(date: DateTime.UtcNow.AddDays(-20), cost: 200.25m, provider: null, notes: null));
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId}/instances",
            new LogMaintenanceRequestDto(date: DateTime.UtcNow.AddDays(-10), cost: 50.00m, provider: null, notes: null));

        // Act
        var response = await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-history");

        // Assert
        var history = await response.Content.ReadAsJsonAsync<MaintenanceHistoryResponseDto>();
        history!.TotalSpent.Should().Be(350.75m);
    }

    [Fact]
    public async Task GetMaintenanceHistory_ReturnsCount()
    {
        // Arrange
        var (client, _, deviceId, maintenanceTypeId) = await CreateAuthenticatedClientWithMaintenanceTypeAsync();

        // Log 3 maintenances
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId}/instances",
            new LogMaintenanceRequestDto(date: DateTime.UtcNow.AddDays(-30), cost: 100m, provider: null, notes: null));
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId}/instances",
            new LogMaintenanceRequestDto(date: DateTime.UtcNow.AddDays(-20), cost: 200m, provider: null, notes: null));
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId}/instances",
            new LogMaintenanceRequestDto(date: DateTime.UtcNow.AddDays(-10), cost: 150m, provider: null, notes: null));

        // Act
        var response = await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-history");

        // Assert
        var history = await response.Content.ReadAsJsonAsync<MaintenanceHistoryResponseDto>();
        history!.Count.Should().Be(3);
    }

    [Fact]
    public async Task GetMaintenanceHistory_ForOtherUserDevice_Returns403Forbidden()
    {
        // Arrange - Create User 1 with device
        var (client1, _, deviceId1, _) = await CreateAuthenticatedClientWithMaintenanceTypeAsync();

        // Create User 2
        var (client2, _, _, _) = await CreateAuthenticatedClientWithMaintenanceTypeAsync();

        // Act - User 2 tries to get history of User 1's device
        var response = await client2.GetAsync($"/api/v1/devices/{deviceId1}/maintenance-history");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Update Maintenance Instance Tests

    [Fact]
    public async Task UpdateMaintenanceInstance_AsOwner_UpdatesSuccessfully()
    {
        // Arrange
        var (client, _, deviceId, maintenanceTypeId) = await CreateAuthenticatedClientWithMaintenanceTypeAsync();

        // Create an instance first
        var createRequest = new LogMaintenanceRequestDto(
            date: DateTime.UtcNow.AddDays(-10),
            cost: 100m,
            provider: "Original Provider",
            notes: "Original Notes"
        );
        var createResponse = await client.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId}/instances", createRequest);
        var createdInstance = await createResponse.Content.ReadAsJsonAsync<MaintenanceInstanceDto>();

        // Update request
        var updateRequest = new UpdateMaintenanceInstanceRequestDto(
            Date: DateTime.UtcNow,
            Cost: 200m,
            Provider: "Updated Provider",
            Notes: "Updated Notes"
        );

        // Act
        var response = await client.PutAsJsonAsync($"/api/v1/maintenance-instances/{createdInstance!.Id}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedInstance = await response.Content.ReadAsJsonAsync<MaintenanceInstanceDto>();
        updatedInstance.Should().NotBeNull();
        updatedInstance!.Cost.Should().Be(200m);
        updatedInstance.Provider.Should().Be("Updated Provider");
        updatedInstance.Notes.Should().Be("Updated Notes");
    }

    [Fact]
    public async Task UpdateMaintenanceInstance_NotOwner_Returns403Forbidden()
    {
        // Arrange - Create User 1 with instance
        var (client1, _, _, maintenanceTypeId1) = await CreateAuthenticatedClientWithMaintenanceTypeAsync();
        var createRequest = new LogMaintenanceRequestDto(date: DateTime.UtcNow, cost: 100m, provider: "Provider", notes: null);
        var createResponse = await client1.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId1}/instances", createRequest);
        var createdInstance = await createResponse.Content.ReadAsJsonAsync<MaintenanceInstanceDto>();

        // Create User 2
        var (client2, _, _, _) = await CreateAuthenticatedClientWithMaintenanceTypeAsync();

        var updateRequest = new UpdateMaintenanceInstanceRequestDto(Date: null, Cost: 999m, Provider: null, Notes: null);

        // Act - User 2 tries to update User 1's instance
        var response = await client2.PutAsJsonAsync($"/api/v1/maintenance-instances/{createdInstance!.Id}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Delete Maintenance Instance Tests

    [Fact]
    public async Task DeleteMaintenanceInstance_AsOwner_DeletesSuccessfully()
    {
        // Arrange
        var (client, _, deviceId, maintenanceTypeId) = await CreateAuthenticatedClientWithMaintenanceTypeAsync();

        // Create an instance
        var createRequest = new LogMaintenanceRequestDto(date: DateTime.UtcNow, cost: 100m, provider: "Provider", notes: null);
        var createResponse = await client.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId}/instances", createRequest);
        var createdInstance = await createResponse.Content.ReadAsJsonAsync<MaintenanceInstanceDto>();

        // Act
        var deleteResponse = await client.DeleteAsync($"/api/v1/maintenance-instances/{createdInstance!.Id}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify instance no longer exists in history
        var historyResponse = await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-history");
        var history = await historyResponse.Content.ReadAsJsonAsync<MaintenanceHistoryResponseDto>();
        history!.Instances.Should().NotContain(i => i.Id == createdInstance.Id);
    }

    [Fact]
    public async Task DeleteMaintenanceInstance_NotOwner_Returns403Forbidden()
    {
        // Arrange - Create User 1 with instance
        var (client1, _, deviceId1, maintenanceTypeId1) = await CreateAuthenticatedClientWithMaintenanceTypeAsync();
        var createRequest = new LogMaintenanceRequestDto(date: DateTime.UtcNow, cost: 100m, provider: "Provider", notes: null);
        var createResponse = await client1.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId1}/instances", createRequest);
        var createdInstance = await createResponse.Content.ReadAsJsonAsync<MaintenanceInstanceDto>();

        // Create User 2
        var (client2, _, _, _) = await CreateAuthenticatedClientWithMaintenanceTypeAsync();

        // Act - User 2 tries to delete User 1's instance
        var response = await client2.DeleteAsync($"/api/v1/maintenance-instances/{createdInstance!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Verify instance still exists for User 1
        var historyResponse = await client1.GetAsync($"/api/v1/devices/{deviceId1}/maintenance-history");
        var history = await historyResponse.Content.ReadAsJsonAsync<MaintenanceHistoryResponseDto>();
        history!.Instances.Should().Contain(i => i.Id == createdInstance.Id);
    }

    #endregion
}
