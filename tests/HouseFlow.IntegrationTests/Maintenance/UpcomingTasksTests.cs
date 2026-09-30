using HouseFlow.Application.Common;
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
        var registerRequest = new RegisterRequestDto(firstName: "Test", lastName: "User", email: email, password: "Password123!", consentAccepted: true);

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        response.EnsureSuccessStatusCode();

        var authResponse = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);

        // Registration creates no house any more (onboarding P05 does): create one
        var houseId = await client.CreateHouseAsync();

        // Create a device
        var deviceRequest = new CreateDeviceRequestDto(maintenanceType: null, name: "Test Device", type: "Chaudiere Gaz", brand: "Viessmann", model: "Vitodens", installDate: null);
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

        // Create an annual maintenance type (never done: due 30 days after creation)
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

        // R2: a never-done type is due 30 days after its creation (never null) and R1 sorts every
        // task by due date — overdue ones naturally come first.
        var tasksList = result.Tasks.ToList();
        tasksList.Select(t => t.NextDueDate).Should().BeInAscendingOrder();
        tasksList[0].Status.Should().Be("overdue");
        tasksList[0].MaintenanceTypeName.Should().Be("Nettoyage Mensuel");

        var neverDoneTask = tasksList.Single(t => t.MaintenanceTypeName == "Revision Annuelle");
        neverDoneTask.Status.Should().Be("pending");
        neverDoneTask.LastMaintenanceDate.Should().BeNull();
        (neverDoneTask.NextDueDate.Date - DateTime.UtcNow.Date).TotalDays.Should().BeInRange(29, 31);
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

    #region Redesign: dashboard (P07), R1/R2 rules, « Dernier entretien »

    private static CreateMaintenanceTypeRequestDto TypeRequest(string name, Periodicity periodicity, LastMaintenanceDto? last, int? customMonths = null) =>
        new(name, periodicity, null, customMonths, last);

    private static LastMaintenanceDto Month(int year, int month) => new(LastMaintenanceKind.Month, month, year);

    [Fact]
    public async Task Dashboard_ReturnsEveryTaskToHandle_WithoutLimit_AndCounters()
    {
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();
        // 6 never-done types (due in 30 days → « À faire ») + 1 overdue + 1 up to date.
        for (var i = 0; i < 6; i++)
            await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", TypeRequest($"Due {i}", Periodicity.Annual, null));
        var overdue = await (await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types",
            TypeRequest("Overdue", Periodicity.Monthly, null))).Content.ReadAsJsonAsync<MaintenanceTypeDto>();
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{overdue!.Id}/instances",
            new LogMaintenanceRequestDto(date: DateTime.UtcNow.AddMonths(-3), cost: null, provider: null, notes: null));
        var upToDate = await (await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types",
            TypeRequest("Fresh", Periodicity.Annual, null))).Content.ReadAsJsonAsync<MaintenanceTypeDto>();
        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{upToDate!.Id}/instances",
            new LogMaintenanceRequestDto(date: DateTime.UtcNow.AddDays(-1), cost: null, provider: null, notes: null));

        var response = await client.GetAsync("/api/v1/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dashboard = await response.Content.ReadAsJsonAsync<DashboardDto>();
        dashboard!.Tasks.Should().HaveCount(7, "no limit of 5 any more");
        dashboard.ToHandleCount.Should().Be(7);
        dashboard.OverdueCount.Should().Be(1);
        dashboard.PendingCount.Should().Be(6);
        dashboard.UpToDateCount.Should().Be(1);
        dashboard.TotalCount.Should().Be(8);
        dashboard.Tasks.First().MaintenanceTypeName.Should().Be("Overdue");
        dashboard.Tasks.Should().OnlyContain(t => t.CanLogMaintenance);
        dashboard.NextTask!.MaintenanceTypeName.Should().Be("Fresh");
        dashboard.NextTask.Status.Should().Be("up_to_date");
    }

    [Fact]
    public async Task Dashboard_AllUpToDate_HasNoTaskButANextOne()
    {
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();
        var today = ParisClock.Today();
        await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types",
            TypeRequest("Annual", Periodicity.Annual, Month(today.Year, today.Month)));

        var dashboard = await (await client.GetAsync("/api/v1/dashboard")).Content.ReadAsJsonAsync<DashboardDto>();

        dashboard!.Tasks.Should().BeEmpty();
        dashboard.ToHandleCount.Should().Be(0);
        dashboard.TotalCount.Should().Be(1);
        dashboard.UpToDateCount.Should().Be(1);
        dashboard.NextTask.Should().NotBeNull();
        dashboard.NextTask!.NextDueDate.Should().Be(new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddYears(1));
    }

    [Fact]
    public async Task Dashboard_NoHouse_IsEmpty()
    {
        var client = CreateClient();
        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequestDto(email: $"test-{Guid.NewGuid()}@example.com", firstName: "No", lastName: "House", password: "Password123!", consentAccepted: true));
        var auth = await register.Content.ReadAsJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var dashboard = await (await client.GetAsync("/api/v1/dashboard")).Content.ReadAsJsonAsync<DashboardDto>();

        dashboard!.Tasks.Should().BeEmpty();
        dashboard.TotalCount.Should().Be(0);
        dashboard.NextTask.Should().BeNull();
    }

    [Fact]
    public async Task CreateType_WithLastMaintenanceMonth_CreatesApproximateRecordOnFirstOfMonth()
    {
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();

        var created = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types",
            TypeRequest("Entretien annuel", Periodicity.Annual, Month(2025, 3)));
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var history = await (await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-history")).Content.ReadAsJsonAsync<MaintenanceHistoryResponseDto>();
        var record = history!.Instances.Should().ContainSingle().Subject;
        record.Date.Should().Be(new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        record.Notes.Should().Be("Date approximative (mois)");

        var types = await (await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-types")).Content.ReadAsJsonAsync<MaintenanceTypeWithStatusDto[]>();
        types!.Single().NextDueDate.Should().Be(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task CreateType_WithFutureMonth_Returns400()
    {
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();
        var nextMonth = ParisClock.Today().AddMonths(1);

        var response = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types",
            TypeRequest("Futur", Periodicity.Annual, Month(nextMonth.Year, nextMonth.Month)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateType_Unknown_IsDueInThirtyDays_Older_IsDueToday()
    {
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();
        var today = ParisClock.Today();

        await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types",
            TypeRequest("Inconnu", Periodicity.Annual, new LastMaintenanceDto(LastMaintenanceKind.Unknown, null, null)));
        await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types",
            TypeRequest("Ancien", Periodicity.Annual, new LastMaintenanceDto(LastMaintenanceKind.Older, null, null)));

        var types = await (await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-types")).Content.ReadAsJsonAsync<MaintenanceTypeWithStatusDto[]>();

        var unknown = types!.Single(t => t.Name == "Inconnu");
        unknown.NextDueDate.Should().Be(today.AddDays(30));
        unknown.Status.Should().Be("pending");
        unknown.LastMaintenanceDate.Should().BeNull();

        var older = types.Single(t => t.Name == "Ancien");
        older.NextDueDate.Should().Be(today);
        older.Status.Should().Be("pending", "due today is « À faire », it becomes overdue tomorrow");

        var history = await (await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-history")).Content.ReadAsJsonAsync<MaintenanceHistoryResponseDto>();
        history!.Instances.Should().BeEmpty("neither choice creates a record");
    }

    [Fact]
    public async Task DeleteLastRecord_RecalculatesFromPreviousThenFromNoHistoryRule()
    {
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();
        var type = await (await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types",
            TypeRequest("Ramonage", Periodicity.Annual, new LastMaintenanceDto(LastMaintenanceKind.Older, null, null))))
            .Content.ReadAsJsonAsync<MaintenanceTypeDto>();
        var older = await (await client.PostAsJsonAsync($"/api/v1/maintenance-types/{type!.Id}/instances",
            new LogMaintenanceRequestDto(date: new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc), cost: null, provider: null, notes: null)))
            .Content.ReadAsJsonAsync<MaintenanceInstanceDto>();
        var latest = await (await client.PostAsJsonAsync($"/api/v1/maintenance-types/{type.Id}/instances",
            new LogMaintenanceRequestDto(date: new DateTime(2025, 6, 10, 0, 0, 0, DateTimeKind.Utc), cost: null, provider: null, notes: null)))
            .Content.ReadAsJsonAsync<MaintenanceInstanceDto>();

        async Task<DateTime> NextDue() =>
            (await (await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-types")).Content.ReadAsJsonAsync<MaintenanceTypeWithStatusDto[]>())!
            .Single().NextDueDate;

        (await NextDue()).Should().Be(new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc));

        (await client.DeleteAsync($"/api/v1/maintenance-instances/{latest!.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await NextDue()).Should().Be(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), "recomputed from the previous record");

        (await client.DeleteAsync($"/api/v1/maintenance-instances/{older!.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await NextDue()).Should().Be(ParisClock.Today(), "no record left: the « Plus ancien » rule applies again");
    }

    [Fact]
    public async Task UpdateRecord_ChangesTheDueDate()
    {
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();
        var type = await (await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types",
            TypeRequest("Filtre", Periodicity.Semestrial, Month(2025, 1)))).Content.ReadAsJsonAsync<MaintenanceTypeDto>();
        var history = await (await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-history")).Content.ReadAsJsonAsync<MaintenanceHistoryResponseDto>();
        var record = history!.Instances.Single();

        var update = await client.PutAsJsonAsync($"/api/v1/maintenance-instances/{record.Id}",
            new UpdateMaintenanceInstanceRequestDto(new DateTime(2025, 4, 15, 0, 0, 0, DateTimeKind.Utc), 80m, "Pro", "Précisé"));
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var types = await (await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-types")).Content.ReadAsJsonAsync<MaintenanceTypeWithStatusDto[]>();
        types!.Single(t => t.Id == type!.Id).NextDueDate.Should().Be(new DateTime(2025, 10, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Periodicity_BiennialAndCustomMonths_AndUpdateRecalculates()
    {
        var (client, _, deviceId) = await CreateAuthenticatedClientWithDeviceAsync();
        var biennial = await (await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types",
            TypeRequest("Détartrage", Periodicity.Biennial, Month(2025, 2)))).Content.ReadAsJsonAsync<MaintenanceTypeDto>();
        var custom = await (await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types",
            TypeRequest("Tous les 18 mois", Periodicity.Custom, Month(2025, 2), customMonths: 18))).Content.ReadAsJsonAsync<MaintenanceTypeDto>();
        custom!.CustomMonths.Should().Be(18);
        custom.CustomDays.Should().BeNull();

        var customWithoutInterval = await client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types",
            TypeRequest("Invalide", Periodicity.Custom, null));
        customWithoutInterval.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var types = await (await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-types")).Content.ReadAsJsonAsync<MaintenanceTypeWithStatusDto[]>();
        types!.Single(t => t.Id == biennial!.Id).NextDueDate.Should().Be(new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        types.Single(t => t.Id == custom.Id).NextDueDate.Should().Be(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));

        // M4: changing the frequency recalculates from the last record; leaving Custom clears the interval.
        var update = await client.PutAsJsonAsync($"/api/v1/maintenance-types/{custom.Id}",
            new UpdateMaintenanceTypeRequestDto(null, Periodicity.Quarterly, null));
        var updated = await update.Content.ReadAsJsonAsync<MaintenanceTypeDto>();
        updated!.CustomMonths.Should().BeNull();
        types = await (await client.GetAsync($"/api/v1/devices/{deviceId}/maintenance-types")).Content.ReadAsJsonAsync<MaintenanceTypeWithStatusDto[]>();
        types!.Single(t => t.Id == custom.Id).NextDueDate.Should().Be(new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task CreateDevice_WithCatalogueMaintenanceType_CreatesBothAtOnce()
    {
        var (client, houseId, _) = await CreateAuthenticatedClientWithDeviceAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/devices",
            new CreateDeviceRequestDto(
                maintenanceType: new DeviceMaintenanceTypeRequestDto(customDays: null, customMonths: null,
                    lastMaintenance: Month(2026, 3), name: "Entretien annuel", periodicity: HouseFlow.Contracts.Periodicity.Annual),
                name: "Chaudière gaz", type: "Chaudière Gaz", brand: null, model: null, installDate: null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var device = await response.Content.ReadAsJsonAsync<DeviceDto>();

        var detail = await (await client.GetAsync($"/api/v1/devices/{device!.Id}")).Content.ReadAsJsonAsync<DeviceDetailDto>();
        var type = detail!.MaintenanceTypes.Should().ContainSingle().Subject;
        type.Name.Should().Be("Entretien annuel");
        type.NextDueDate.Should().Be(new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        detail.MaintenanceCount.Should().Be(1);
        detail.Status.Should().Be("up_to_date");
        detail.UpToDateCount.Should().Be(1);
        detail.MaintenanceTypesCount.Should().Be(1);
    }

    [Fact]
    public async Task Summaries_ExposeStatusAndCounts_ForHousesAndDevices()
    {
        var (client, houseId, emptyDeviceId) = await CreateAuthenticatedClientWithDeviceAsync();
        var device = await (await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/devices",
            new CreateDeviceRequestDto(maintenanceType: null, name: "VMC", type: "VMC", brand: null, model: null, installDate: null)))
            .Content.ReadAsJsonAsync<DeviceDto>();
        var overdue = await (await client.PostAsJsonAsync($"/api/v1/devices/{device!.Id}/maintenance-types",
            TypeRequest("Bouches", Periodicity.Monthly, Month(2025, 1)))).Content.ReadAsJsonAsync<MaintenanceTypeDto>();
        await client.PostAsJsonAsync($"/api/v1/devices/{device.Id}/maintenance-types", TypeRequest("Filtre", Periodicity.Annual, null));
        var today = ParisClock.Today();
        await client.PostAsJsonAsync($"/api/v1/devices/{device.Id}/maintenance-types",
            TypeRequest("Moteur", Periodicity.Annual, Month(today.Year, today.Month)));

        var house = await (await client.GetAsync($"/api/v1/houses/{houseId}")).Content.ReadAsJsonAsync<HouseDetailDto>();
        house!.Status.Should().Be("overdue");
        house.OverdueCount.Should().Be(1);
        house.PendingCount.Should().Be(1);
        house.UpToDateCount.Should().Be(1);
        house.MaintenanceTypesCount.Should().Be(3);

        var vmc = house.Devices.Single(d => d.Id == device.Id);
        vmc.Status.Should().Be("overdue");
        vmc.OverdueCount.Should().Be(1, "was never sent before the redesign");
        vmc.PendingCount.Should().Be(1, "pending no longer includes overdue");
        vmc.UpToDateCount.Should().Be(1);
        vmc.MaintenanceTypesCount.Should().Be(3);
        // P09 C4 row: the most urgent maintenance (earliest due date) and its name.
        vmc.NextMaintenanceName.Should().Be("Bouches");
        vmc.NextDueDate.Should().Be(new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        var empty = house.Devices.Single(d => d.Id == emptyDeviceId);
        empty.Status.Should().Be("none");
        empty.NextDueDate.Should().BeNull();
        empty.NextMaintenanceName.Should().BeNull();

        var devices = await (await client.GetAsync($"/api/v1/houses/{houseId}/devices")).Content.ReadAsJsonAsync<DeviceSummaryDto[]>();
        devices!.Single(d => d.Id == device.Id).NextMaintenanceName.Should().Be("Bouches");

        var list = await (await client.GetAsync("/api/v1/houses")).Content.ReadAsJsonAsync<HousesListResponseDto>();
        var summary = list!.Houses.Single(h => h.Id == houseId);
        summary.Status.Should().Be("overdue");
        summary.UpToDateCount.Should().Be(1);
        summary.MaintenanceTypesCount.Should().Be(3);
        overdue.Should().NotBeNull();
    }

    #endregion
}
