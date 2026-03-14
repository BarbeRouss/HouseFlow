using FluentAssertions;
using HouseFlow.Application.DTOs;
using HouseFlow.Core.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Maintenance;

public class UpcomingTasksTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public UpcomingTasksTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

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

    #region Get Upcoming Tasks Tests

    [Fact]
    public async Task GetUpcomingTasks_NoMaintenanceTypes_ReturnsEmpty()
    {
        // Arrange
        var (client, _, _) = await CreateAuthenticatedClientWithDeviceAsync();

        // Act
        var response = await client.GetAsync("/api/v1/upcoming-tasks");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadAsJsonAsync<UpcomingTasksResponseDto>();
        result.Should().NotBeNull();
        result!.Tasks.Should().BeEmpty();
        result.OverdueCount.Should().Be(0);
        result.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task GetUpcomingTasks_WithPendingMaintenance_ReturnsPendingTask()
    {
        // Arrange
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();

        // Create a maintenance type (no instances = pending status)
        var mtRequest = new CreateMaintenanceTypeRequestDto("Revision Annuelle", Periodicity.Annual, null);
        await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", mtRequest);

        // Act
        var response = await client.GetAsync("/api/v1/upcoming-tasks");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadAsJsonAsync<UpcomingTasksResponseDto>();
        result.Should().NotBeNull();
        result!.Tasks.Should().HaveCount(1);
        result.PendingCount.Should().Be(1);
        result.OverdueCount.Should().Be(0);

        var task = result.Tasks.First();
        task.MaintenanceTypeName.Should().Be("Revision Annuelle");
        task.Status.Should().Be("pending");
        task.DeviceId.Should().Be(deviceId);
    }

    [Fact]
    public async Task GetUpcomingTasks_WithUpToDateMaintenance_ReturnsEmpty()
    {
        // Arrange
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();

        // Create a maintenance type
        var mtRequest = new CreateMaintenanceTypeRequestDto("Revision Annuelle", Periodicity.Annual, null);
        var mtResponse = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", mtRequest);
        var mt = await mtResponse.Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        // Log a recent maintenance (today - should be up_to_date for annual)
        var logRequest = new LogMaintenanceRequestDto(DateTime.UtcNow, null, null, null);
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{mt!.Id}/instances", logRequest);

        // Act
        var response = await client.GetAsync("/api/v1/upcoming-tasks");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadAsJsonAsync<UpcomingTasksResponseDto>();
        result.Should().NotBeNull();
        result!.Tasks.Should().BeEmpty();
        result.PendingCount.Should().Be(0);
        result.OverdueCount.Should().Be(0);
    }

    [Fact]
    public async Task GetUpcomingTasks_WithOverdueMaintenance_ReturnsOverdueTask()
    {
        // Arrange
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();

        // Create a monthly maintenance type
        var mtRequest = new CreateMaintenanceTypeRequestDto("Nettoyage Mensuel", Periodicity.Monthly, null);
        var mtResponse = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", mtRequest);
        var mt = await mtResponse.Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        // Log maintenance 2 months ago (should be overdue for monthly)
        var logRequest = new LogMaintenanceRequestDto(DateTime.UtcNow.AddMonths(-2), null, null, null);
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{mt!.Id}/instances", logRequest);

        // Act
        var response = await client.GetAsync("/api/v1/upcoming-tasks");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadAsJsonAsync<UpcomingTasksResponseDto>();
        result.Should().NotBeNull();
        result!.Tasks.Should().HaveCount(1);
        result.OverdueCount.Should().Be(1);

        var task = result.Tasks.First();
        task.Status.Should().Be("overdue");
    }

    [Fact]
    public async Task GetUpcomingTasks_Unauthenticated_Returns401()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/upcoming-tasks");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUpcomingTasks_IncludesHouseAndDeviceInfo()
    {
        // Arrange
        var (client, houseId, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();

        // Create a maintenance type
        var mtRequest = new CreateMaintenanceTypeRequestDto("Revision", Periodicity.Annual, null);
        await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", mtRequest);

        // Act
        var response = await client.GetAsync("/api/v1/upcoming-tasks");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadAsJsonAsync<UpcomingTasksResponseDto>();
        var task = result!.Tasks.First();

        task.HouseId.Should().Be(houseId);
        task.DeviceId.Should().Be(deviceId);
        task.DeviceName.Should().Be("Test Device");
        task.HouseName.Should().NotBeNullOrEmpty();
    }

    #endregion
}
