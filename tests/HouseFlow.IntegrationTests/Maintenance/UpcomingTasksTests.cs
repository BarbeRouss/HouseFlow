using FluentAssertions;
using HouseFlow.Application.DTOs;
using HouseFlow.Core.Entities;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Maintenance;

[Collection("Integration")]
public class UpcomingTasksTests
{
    private readonly IntegrationTestFixture _fixture;

    public UpcomingTasksTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private HttpClient CreateClient() => _fixture.CreateApiClient();

    private async Task<(HttpClient client, Guid houseId, Guid deviceId)> CreateAuthenticatedClientWithDeviceAsync()
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
        var logRequest = new LogMaintenanceRequestDto(date: DateTime.UtcNow, cost: null, provider: null, notes: null);
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
        var logRequest = new LogMaintenanceRequestDto(date: DateTime.UtcNow.AddMonths(-2), cost: null, provider: null, notes: null);
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

    [Fact]
    public async Task GetUpcomingTasks_ReturnsTasksSortedByDueDate()
    {
        // Arrange
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();

        // Create a monthly maintenance type with old maintenance (overdue)
        var mt1Request = new CreateMaintenanceTypeRequestDto("Nettoyage Mensuel", Periodicity.Monthly, null);
        var mt1Response = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", mt1Request);
        var mt1 = await mt1Response.Content.ReadAsJsonAsync<MaintenanceTypeDto>();
        var log1 = new LogMaintenanceRequestDto(date: DateTime.UtcNow.AddMonths(-3), cost: null, provider: null, notes: null);
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{mt1!.Id}/instances", log1);

        // Create an annual maintenance type (never done = null date, should be first)
        var mt2Request = new CreateMaintenanceTypeRequestDto("Revision Annuelle", Periodicity.Annual, null);
        await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", mt2Request);

        // Create a quarterly maintenance type with recent-ish maintenance (pending soon)
        var mt3Request = new CreateMaintenanceTypeRequestDto("Nettoyage Trimestriel", Periodicity.Quarterly, null);
        var mt3Response = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", mt3Request);
        var mt3 = await mt3Response.Content.ReadAsJsonAsync<MaintenanceTypeDto>();
        var log3 = new LogMaintenanceRequestDto(date: DateTime.UtcNow.AddMonths(-2), cost: null, provider: null, notes: null);
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{mt3!.Id}/instances", log3);

        // Act
        var response = await client.GetAsync("/api/v1/upcoming-tasks");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadAsJsonAsync<UpcomingTasksResponseDto>();
        result.Should().NotBeNull();
        result!.Tasks.Should().HaveCountGreaterThanOrEqualTo(2);

        // Tasks never done (null NextDueDate) should come first
        var tasksList = result.Tasks.ToList();
        var neverDoneTask = tasksList.FirstOrDefault(t => t.MaintenanceTypeName == "Revision Annuelle");
        neverDoneTask.Should().NotBeNull();
        tasksList.IndexOf(neverDoneTask!).Should().Be(0, "tasks never done should be sorted first");

        // Overdue tasks should come before pending tasks (excluding never-done tasks which are sorted first)
        var overdueIndices = tasksList.Where(t => t.Status == "overdue").Select(t => tasksList.IndexOf(t));
        var pendingWithDateIndices = tasksList
            .Where(t => t.Status == "pending" && t.NextDueDate != null)
            .Select(t => tasksList.IndexOf(t));
        if (overdueIndices.Any() && pendingWithDateIndices.Any())
        {
            overdueIndices.Max().Should().BeLessThan(pendingWithDateIndices.Min(),
                "overdue tasks should appear before pending tasks with due dates");
        }
    }

    [Fact]
    public async Task GetUpcomingTasks_RespectsLimit()
    {
        // Arrange
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();

        // Create 3 maintenance types (all pending since never done)
        var mt1Request = new CreateMaintenanceTypeRequestDto("Type A", Periodicity.Annual, null);
        await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", mt1Request);

        var mt2Request = new CreateMaintenanceTypeRequestDto("Type B", Periodicity.Semestrial, null);
        await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", mt2Request);

        var mt3Request = new CreateMaintenanceTypeRequestDto("Type C", Periodicity.Monthly, null);
        await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", mt3Request);

        // Act - request with limit=2
        var response = await client.GetAsync("/api/v1/upcoming-tasks?limit=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadAsJsonAsync<UpcomingTasksResponseDto>();
        result.Should().NotBeNull();
        result!.Tasks.Should().HaveCount(2);

        // Counts should reflect ALL tasks, not just the limited ones
        result.PendingCount.Should().Be(3);
    }

    [Fact]
    public async Task GetUpcomingTasks_OnlyReturnsUserTasks()
    {
        // Arrange - User 1 creates a device with maintenance type
        var (client1, _, deviceId1) = await CreateAuthenticatedClientWithDeviceAsync();
        var mt1Request = new CreateMaintenanceTypeRequestDto("User1 Task", Periodicity.Annual, null);
        await client1.PostAsJsonAsync($"/api/v1/devices/{deviceId1}/maintenance-types", mt1Request);

        // Arrange - User 2 creates their own device with maintenance type
        var (client2, _, deviceId2) = await CreateAuthenticatedClientWithDeviceAsync();
        var mt2Request = new CreateMaintenanceTypeRequestDto("User2 Task", Periodicity.Annual, null);
        await client2.PostAsJsonAsync($"/api/v1/devices/{deviceId2}/maintenance-types", mt2Request);

        // Act - User 1 fetches upcoming tasks
        var response1 = await client1.GetAsync("/api/v1/upcoming-tasks");
        var result1 = await response1.Content.ReadAsJsonAsync<UpcomingTasksResponseDto>();

        // Act - User 2 fetches upcoming tasks
        var response2 = await client2.GetAsync("/api/v1/upcoming-tasks");
        var result2 = await response2.Content.ReadAsJsonAsync<UpcomingTasksResponseDto>();

        // Assert - Each user only sees their own tasks
        result1!.Tasks.Should().HaveCount(1);
        result1.Tasks.First().MaintenanceTypeName.Should().Be("User1 Task");

        result2!.Tasks.Should().HaveCount(1);
        result2.Tasks.First().MaintenanceTypeName.Should().Be("User2 Task");
    }

    #endregion
}
