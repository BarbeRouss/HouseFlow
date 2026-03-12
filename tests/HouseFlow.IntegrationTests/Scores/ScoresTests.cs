using FluentAssertions;
using HouseFlow.Application.DTOs;
using HouseFlow.Core.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Scores;

public class ScoresTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ScoresTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private async Task<(HttpClient client, Guid houseId)> CreateAuthenticatedClientWithHouseAsync()
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

        return (client, houseId);
    }

    private async Task<Guid> CreateDeviceAsync(HttpClient client, Guid houseId, string name = "Test Device")
    {
        var request = new CreateDeviceRequestDto(name, "Chaudiere Gaz", "Viessmann", "Vitodens", null);
        var response = await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/devices", request);
        var device = await response.Content.ReadAsJsonAsync<DeviceDto>();
        return device!.Id;
    }

    private async Task<Guid> CreateMaintenanceTypeAsync(HttpClient client, Guid deviceId, Periodicity periodicity = Periodicity.Annual)
    {
        var request = new CreateMaintenanceTypeRequestDto($"Entretien {Guid.NewGuid().ToString("N")[..8]}", periodicity, null);
        var response = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", request);
        var type = await response.Content.ReadAsJsonAsync<MaintenanceTypeDto>();
        return type!.Id;
    }

    private async Task LogMaintenanceAsync(HttpClient client, Guid maintenanceTypeId, DateTime date)
    {
        var request = new LogMaintenanceRequestDto(date, 100m, null, null);
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{maintenanceTypeId}/instances", request);
    }

    #region House Score Tests

    [Fact]
    public async Task HouseScore_CalculatesFromDeviceScores()
    {
        // Arrange
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();

        // Create 2 devices
        var device1Id = await CreateDeviceAsync(client, houseId, "Device 1");
        var device2Id = await CreateDeviceAsync(client, houseId, "Device 2");

        // Device 1: 1 maintenance type, up to date (100%)
        var type1Id = await CreateMaintenanceTypeAsync(client, device1Id, Periodicity.Annual);
        await LogMaintenanceAsync(client, type1Id, DateTime.UtcNow); // logged today = up_to_date

        // Device 2: 1 maintenance type, overdue (0%)
        var type2Id = await CreateMaintenanceTypeAsync(client, device2Id, Periodicity.Monthly);
        await LogMaintenanceAsync(client, type2Id, DateTime.UtcNow.AddDays(-60)); // 60 days ago with monthly = overdue

        // Act
        var response = await client.GetAsync($"/api/v1/houses/{houseId}");
        var houseDetail = await response.Content.ReadAsJsonAsync<HouseDetailDto>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        houseDetail.Should().NotBeNull();
        // Device 1 = 100%, Device 2 = 0%, House = 50% (1 up_to_date out of 2 types)
        houseDetail!.Score.Should().Be(50);
    }

    [Fact]
    public async Task HouseScore_NoDevices_Returns100()
    {
        // Arrange
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();

        // Delete any auto-created devices (if any)
        var devicesResponse = await client.GetAsync($"/api/v1/houses/{houseId}");
        var houseDetail = await devicesResponse.Content.ReadAsJsonAsync<HouseDetailDto>();

        foreach (var device in houseDetail!.Devices)
        {
            await client.DeleteAsync($"/api/v1/devices/{device.Id}");
        }

        // Act
        var response = await client.GetAsync($"/api/v1/houses/{houseId}");
        var house = await response.Content.ReadAsJsonAsync<HouseDetailDto>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        house!.Score.Should().Be(100);
        house.DevicesCount.Should().Be(0);
    }

    #endregion

    #region Device Score Tests

    [Fact]
    public async Task DeviceScore_AllMaintenanceUpToDate_Returns100()
    {
        // Arrange
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var deviceId = await CreateDeviceAsync(client, houseId);

        // Create 2 maintenance types, both up to date
        var type1Id = await CreateMaintenanceTypeAsync(client, deviceId, Periodicity.Annual);
        var type2Id = await CreateMaintenanceTypeAsync(client, deviceId, Periodicity.Semestrial);

        // Log maintenance today for both
        await LogMaintenanceAsync(client, type1Id, DateTime.UtcNow);
        await LogMaintenanceAsync(client, type2Id, DateTime.UtcNow);

        // Act
        var response = await client.GetAsync($"/api/v1/devices/{deviceId}");
        var device = await response.Content.ReadAsJsonAsync<DeviceDetailDto>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        device!.Score.Should().Be(100);
        device.Status.Should().Be("up_to_date");
    }

    [Fact]
    public async Task DeviceScore_WithOverdue_ReturnsLowerScore()
    {
        // Arrange
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var deviceId = await CreateDeviceAsync(client, houseId);

        // Create 2 maintenance types
        var type1Id = await CreateMaintenanceTypeAsync(client, deviceId, Periodicity.Annual);
        var type2Id = await CreateMaintenanceTypeAsync(client, deviceId, Periodicity.Monthly);

        // Type 1: up to date (logged today)
        await LogMaintenanceAsync(client, type1Id, DateTime.UtcNow);

        // Type 2: overdue (logged 60 days ago with monthly periodicity)
        await LogMaintenanceAsync(client, type2Id, DateTime.UtcNow.AddDays(-60));

        // Act
        var response = await client.GetAsync($"/api/v1/devices/{deviceId}");
        var device = await response.Content.ReadAsJsonAsync<DeviceDetailDto>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        device!.Score.Should().Be(50); // 1 out of 2 up_to_date
        device.Status.Should().Be("overdue");
    }

    [Fact]
    public async Task DeviceScore_WithPending_ReturnsLowerScore()
    {
        // Arrange
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var deviceId = await CreateDeviceAsync(client, houseId);

        // Create 2 maintenance types
        var type1Id = await CreateMaintenanceTypeAsync(client, deviceId, Periodicity.Annual);
        var type2Id = await CreateMaintenanceTypeAsync(client, deviceId, Periodicity.Monthly);

        // Type 1: up to date (logged today)
        await LogMaintenanceAsync(client, type1Id, DateTime.UtcNow);

        // Type 2: pending (logged 20 days ago with monthly periodicity - due in ~10 days)
        await LogMaintenanceAsync(client, type2Id, DateTime.UtcNow.AddDays(-20));

        // Act
        var response = await client.GetAsync($"/api/v1/devices/{deviceId}");
        var device = await response.Content.ReadAsJsonAsync<DeviceDetailDto>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        device!.Score.Should().Be(50); // 1 out of 2 up_to_date
        device.Status.Should().Be("pending");
    }

    [Fact]
    public async Task DeviceScore_NoMaintenanceTypes_Returns100()
    {
        // Arrange
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var deviceId = await CreateDeviceAsync(client, houseId);

        // No maintenance types added

        // Act
        var response = await client.GetAsync($"/api/v1/devices/{deviceId}");
        var device = await response.Content.ReadAsJsonAsync<DeviceDetailDto>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        device!.Score.Should().Be(100);
        device.Status.Should().Be("up_to_date");
        device.MaintenanceTypesCount.Should().Be(0);
    }

    #endregion

    #region Global Score Tests

    [Fact]
    public async Task GlobalScore_CalculatesFromAllHouses()
    {
        // Arrange
        var (client, houseId1) = await CreateAuthenticatedClientWithHouseAsync();

        // Create a second house
        var createHouseRequest = new CreateHouseRequestDto("Second House", null, null, null);
        var createHouseResponse = await client.PostAsJsonAsync("/api/v1/houses", createHouseRequest);
        var secondHouse = await createHouseResponse.Content.ReadAsJsonAsync<HouseDto>();
        var houseId2 = secondHouse!.Id;

        // House 1: 1 device with 1 type, up to date (100%)
        var device1Id = await CreateDeviceAsync(client, houseId1);
        var type1Id = await CreateMaintenanceTypeAsync(client, device1Id, Periodicity.Annual);
        await LogMaintenanceAsync(client, type1Id, DateTime.UtcNow);

        // House 2: 1 device with 1 type, overdue (0%)
        var device2Id = await CreateDeviceAsync(client, houseId2);
        var type2Id = await CreateMaintenanceTypeAsync(client, device2Id, Periodicity.Monthly);
        await LogMaintenanceAsync(client, type2Id, DateTime.UtcNow.AddDays(-60));

        // Act
        var response = await client.GetAsync("/api/v1/houses");
        var houses = await response.Content.ReadAsJsonAsync<HousesListResponseDto>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        houses!.GlobalScore.Should().Be(50); // Average of 100% and 0%
    }

    [Fact]
    public async Task GlobalScore_NoHouses_Returns100()
    {
        // Arrange
        var client = CreateClient();
        var email = $"test-{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequestDto("Test", "User", email, "Password123!");

        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        var authResponse = await registerResponse.Content.ReadAsJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);

        // Delete the auto-created house
        var housesResponse = await client.GetAsync("/api/v1/houses");
        var houses = await housesResponse.Content.ReadAsJsonAsync<HousesListResponseDto>();

        foreach (var house in houses!.Houses)
        {
            await client.DeleteAsync($"/api/v1/houses/{house.Id}");
        }

        // Act
        var response = await client.GetAsync("/api/v1/houses");
        var result = await response.Content.ReadAsJsonAsync<HousesListResponseDto>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result!.GlobalScore.Should().Be(100);
        result.Houses.Should().BeEmpty();
    }

    #endregion

    #region Count Tests

    [Fact]
    public async Task PendingCount_CountsCorrectly()
    {
        // Arrange
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var deviceId = await CreateDeviceAsync(client, houseId);

        // Create 3 maintenance types
        var type1Id = await CreateMaintenanceTypeAsync(client, deviceId, Periodicity.Annual);
        var type2Id = await CreateMaintenanceTypeAsync(client, deviceId, Periodicity.Monthly);
        var type3Id = await CreateMaintenanceTypeAsync(client, deviceId, Periodicity.Quarterly);

        // Type 1: up to date
        await LogMaintenanceAsync(client, type1Id, DateTime.UtcNow);

        // Type 2: pending (due in ~10 days)
        await LogMaintenanceAsync(client, type2Id, DateTime.UtcNow.AddDays(-20));

        // Type 3: never maintained = pending

        // Act
        var response = await client.GetAsync($"/api/v1/devices/{deviceId}");
        var device = await response.Content.ReadAsJsonAsync<DeviceDetailDto>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // 2 pending (type2 + type3)
        device!.PendingCount.Should().Be(2);
    }

    [Fact]
    public async Task OverdueCount_CountsCorrectly()
    {
        // Arrange
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var deviceId = await CreateDeviceAsync(client, houseId);

        // Create 3 maintenance types
        var type1Id = await CreateMaintenanceTypeAsync(client, deviceId, Periodicity.Annual);
        var type2Id = await CreateMaintenanceTypeAsync(client, deviceId, Periodicity.Monthly);
        var type3Id = await CreateMaintenanceTypeAsync(client, deviceId, Periodicity.Monthly);

        // Type 1: up to date
        await LogMaintenanceAsync(client, type1Id, DateTime.UtcNow);

        // Type 2: overdue (60 days ago for monthly)
        await LogMaintenanceAsync(client, type2Id, DateTime.UtcNow.AddDays(-60));

        // Type 3: overdue (45 days ago for monthly)
        await LogMaintenanceAsync(client, type3Id, DateTime.UtcNow.AddDays(-45));

        // Act - Check house level for overdue count
        var response = await client.GetAsync($"/api/v1/houses/{houseId}");
        var house = await response.Content.ReadAsJsonAsync<HouseDetailDto>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        house!.OverdueCount.Should().Be(2);
    }

    [Fact]
    public async Task House_PendingCount_AggregatesFromDevices()
    {
        // Arrange
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();

        // Create 2 devices
        var device1Id = await CreateDeviceAsync(client, houseId, "Device 1");
        var device2Id = await CreateDeviceAsync(client, houseId, "Device 2");

        // Device 1: 1 pending type
        var type1Id = await CreateMaintenanceTypeAsync(client, device1Id, Periodicity.Monthly);
        await LogMaintenanceAsync(client, type1Id, DateTime.UtcNow.AddDays(-20)); // pending

        // Device 2: 2 pending types (never maintained = pending)
        await CreateMaintenanceTypeAsync(client, device2Id, Periodicity.Annual);
        await CreateMaintenanceTypeAsync(client, device2Id, Periodicity.Quarterly);

        // Act
        var response = await client.GetAsync($"/api/v1/houses/{houseId}");
        var house = await response.Content.ReadAsJsonAsync<HouseDetailDto>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // 3 pending total (1 from device1 + 2 from device2)
        house!.PendingCount.Should().Be(3);
    }

    [Fact]
    public async Task House_OverdueCount_AggregatesFromDevices()
    {
        // Arrange
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();

        // Create 2 devices
        var device1Id = await CreateDeviceAsync(client, houseId, "Device 1");
        var device2Id = await CreateDeviceAsync(client, houseId, "Device 2");

        // Device 1: 1 overdue type
        var type1Id = await CreateMaintenanceTypeAsync(client, device1Id, Periodicity.Monthly);
        await LogMaintenanceAsync(client, type1Id, DateTime.UtcNow.AddDays(-60)); // overdue

        // Device 2: 2 overdue types
        var type2Id = await CreateMaintenanceTypeAsync(client, device2Id, Periodicity.Monthly);
        var type3Id = await CreateMaintenanceTypeAsync(client, device2Id, Periodicity.Monthly);
        await LogMaintenanceAsync(client, type2Id, DateTime.UtcNow.AddDays(-45)); // overdue
        await LogMaintenanceAsync(client, type3Id, DateTime.UtcNow.AddDays(-50)); // overdue

        // Act
        var response = await client.GetAsync($"/api/v1/houses/{houseId}");
        var house = await response.Content.ReadAsJsonAsync<HouseDetailDto>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // 3 overdue total (1 from device1 + 2 from device2)
        house!.OverdueCount.Should().Be(3);
    }

    #endregion
}
