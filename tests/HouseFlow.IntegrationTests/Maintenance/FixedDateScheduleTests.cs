using FluentAssertions;
using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Core.Entities;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Maintenance;

/// <summary>« À date fixe » (issue #294): creation, validation, update and the same due date on every screen.</summary>
[Collection("Integration")]
public class FixedDateScheduleTests
{
    private readonly IntegrationTestFixture _fixture;

    public FixedDateScheduleTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(HttpClient client, Guid houseId, Guid deviceId)> CreateClientWithDeviceAsync()
    {
        var client = _fixture.CreateApiClient();
        var registerRequest = new RegisterRequestDto(firstName: "Test", lastName: "User",
            email: $"fixed-{Guid.NewGuid():N}@example.com", password: "Password123!", consentAccepted: true);
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var houseId = await client.CreateHouseAsync();
        var deviceRequest = new CreateDeviceRequestDto(maintenanceType: null, name: "Chaudière", type: "Chaudière Gaz",
            brand: null, model: null, installDate: null);
        var device = await (await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/devices", deviceRequest))
            .Content.ReadAsJsonAsync<DeviceDto>();
        return (client, houseId, device!.Id);
    }

    private static object Body(string name, string periodicity, string? mode = null, int? anchorDay = null, int? anchorMonth = null,
        int? customMonths = null, object? lastMaintenance = null) => new
        {
            name,
            periodicity,
            scheduleMode = mode,
            anchorDay,
            anchorMonth,
            customMonths,
            lastMaintenance,
        };

    private static object Month(int year, int month) => new { kind = "Month", year, month };

    private static Task<HttpResponseMessage> CreateTypeAsync(HttpClient client, Guid deviceId, object body) =>
        client.PostAsJsonAsync($"/api/v1/devices/{deviceId}/maintenance-types", body);

    private static async Task<MaintenanceTypeWithStatusDto> GetTypeAsync(HttpClient client, Guid deviceId, Guid typeId)
    {
        var types = await client.GetFromJsonAsync<List<MaintenanceTypeWithStatusDto>>($"/api/v1/devices/{deviceId}/maintenance-types",
            JsonOptions);
        return types!.Single(t => t.Id == typeId);
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    #region Create

    [Fact]
    public async Task Create_WithoutScheduleMode_IsAfterLastTime_WithNoAnchor()
    {
        var (client, _, deviceId) = await CreateClientWithDeviceAsync();

        var response = await CreateTypeAsync(client, deviceId, Body("Annuel", "Annual"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var type = await response.Content.ReadAsJsonAsync<MaintenanceTypeDto>();
        type!.ScheduleMode.Should().Be(ScheduleMode.AfterLastTime);
        type.AnchorDay.Should().BeNull();
        type.AnchorMonth.Should().BeNull();
    }

    [Fact]
    public async Task Create_FixedDate_Monthly_StoresTheAnchor()
    {
        var (client, _, deviceId) = await CreateClientWithDeviceAsync();

        var response = await CreateTypeAsync(client, deviceId, Body("Relevé compteur", "Monthly", "FixedDate", anchorDay: 1));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var type = await response.Content.ReadAsJsonAsync<MaintenanceTypeDto>();
        type!.ScheduleMode.Should().Be(ScheduleMode.FixedDate);
        type.AnchorDay.Should().Be(1);
        type.AnchorMonth.Should().BeNull();
    }

    [Fact]
    public async Task Create_FixedDate_Annual_StoresDayAndMonth_AndTheListReadsThemBack()
    {
        var (client, _, deviceId) = await CreateClientWithDeviceAsync();

        var created = await (await CreateTypeAsync(client, deviceId, Body("Chaudière", "Annual", "FixedDate", 1, 10)))
            .Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        var read = await GetTypeAsync(client, deviceId, created!.Id);
        read.ScheduleMode.Should().Be(ScheduleMode.FixedDate);
        read.AnchorDay.Should().Be(1);
        read.AnchorMonth.Should().Be(10);
    }

    public static TheoryData<string, string, string?, int?, int?, int?> InvalidFixedDateBodies => new()
    {
        { "Custom periodicity", "Custom", "FixedDate", 1, null, 6 },
        { "Biennial periodicity", "Biennial", "FixedDate", 1, 1, null },
        { "Missing day", "Annual", "FixedDate", null, 10, null },
        { "Missing month (annual)", "Annual", "FixedDate", 1, null, null },
        { "Missing month (quarterly)", "Quarterly", "FixedDate", 1, null, null },
        { "Month in monthly", "Monthly", "FixedDate", 1, 3, null },
        { "30 February", "Annual", "FixedDate", 30, 2, null },
        { "31 April", "Semestrial", "FixedDate", 31, 4, null },
        { "Custom interval", "Monthly", "FixedDate", 1, null, 2 },
        { "Anchor in AfterLastTime", "Annual", "AfterLastTime", 1, 10, null },
        { "Anchor with default mode", "Monthly", null, 1, null, null },
    };

    [Theory]
    [MemberData(nameof(InvalidFixedDateBodies))]
    public async Task Create_InconsistentSchedule_Returns400_AndStoresNothing(
        string scenario, string periodicity, string? mode, int? day, int? month, int? customMonths)
    {
        var (client, _, deviceId) = await CreateClientWithDeviceAsync();

        var response = await CreateTypeAsync(client, deviceId, Body("Refusé", periodicity, mode, day, month, customMonths));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var types = await client.GetFromJsonAsync<List<MaintenanceTypeWithStatusDto>>($"/api/v1/devices/{deviceId}/maintenance-types", JsonOptions);
        types.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_FixedDate_29February_IsAccepted()
    {
        var (client, _, deviceId) = await CreateClientWithDeviceAsync();

        var response = await CreateTypeAsync(client, deviceId, Body("Bissextile", "Annual", "FixedDate", 29, 2));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    #endregion

    #region Same due date everywhere

    [Fact]
    public async Task FixedDate_DueDateIsTheSameOnDeviceListHouseAndDashboard()
    {
        var (client, houseId, deviceId) = await CreateClientWithDeviceAsync();
        var today = ParisClock.Today();
        // Record on the 1st of the month three months ago → it covers that 1st → due on the 1st two months ago: overdue,
        // so the type is also listed by the dashboard and by the upcoming tasks.
        var recordMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-3);
        var expectedDue = recordMonth.AddMonths(1);
        var created = await (await CreateTypeAsync(client, deviceId,
                Body("Relevé", "Monthly", "FixedDate", anchorDay: 1, lastMaintenance: Month(recordMonth.Year, recordMonth.Month))))
            .Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        var fromList = await GetTypeAsync(client, deviceId, created!.Id);
        var detail = await client.GetFromJsonAsync<DeviceDetailDto>($"/api/v1/devices/{deviceId}", JsonOptions);
        var devices = await client.GetFromJsonAsync<List<DeviceSummaryDto>>($"/api/v1/houses/{houseId}/devices", JsonOptions);
        var dashboard = await client.GetFromJsonAsync<DashboardDto>("/api/v1/dashboard", JsonOptions);
        var upcoming = await client.GetFromJsonAsync<UpcomingTasksResponseDto>("/api/v1/upcoming-tasks", JsonOptions);

        fromList.NextDueDate.Should().Be(expectedDue);
        fromList.Status.Should().Be("overdue");
        detail!.MaintenanceTypes.Single().NextDueDate.Should().Be(expectedDue);
        devices!.Single().NextDueDate.Should().Be(expectedDue);
        devices.Single().Status.Should().Be("overdue");
        var task = dashboard!.Tasks.Single(t => t.MaintenanceTypeId == created.Id);
        task.NextDueDate.Should().Be(expectedDue);
        task.Status.Should().Be("overdue");
        task.ScheduleMode.Should().Be("FixedDate");
        task.AnchorDay.Should().Be(1);
        upcoming!.Tasks.Single(t => t.MaintenanceTypeId == created.Id).NextDueDate.Should().Be(expectedDue);
    }

    [Fact]
    public async Task FixedDate_RecordMadeEarly_DoesNotShiftTheGrid()
    {
        var (client, _, deviceId) = await CreateClientWithDeviceAsync();
        var created = await (await CreateTypeAsync(client, deviceId, Body("Relevé", "Monthly", "FixedDate", anchorDay: 1)))
            .Content.ReadAsJsonAsync<MaintenanceTypeDto>();
        var today = ParisClock.Today();

        await client.PostAsJsonAsync($"/api/v1/maintenance-types/{created!.Id}/instances",
            new LogMaintenanceRequestDto(date: today.AddDays(-1), cost: null, provider: null, notes: null));

        var read = await GetTypeAsync(client, deviceId, created.Id);
        var firstOfThisMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        // Done yesterday: the nearest occurrence is the 1st of this month or of the next one; the due date is the
        // occurrence after it — always on a 1st, whatever the day of the record.
        read.NextDueDate.Day.Should().Be(1);
        read.NextDueDate.Should().BeOnOrAfter(firstOfThisMonth.AddMonths(1)).And.BeOnOrBefore(firstOfThisMonth.AddMonths(2));
    }

    [Fact]
    public async Task AfterLastTime_TypesAreUnchanged()
    {
        var (client, _, deviceId) = await CreateClientWithDeviceAsync();
        var created = await (await CreateTypeAsync(client, deviceId, Body("Mensuel", "Monthly", lastMaintenance: Month(2026, 3))))
            .Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        var read = await GetTypeAsync(client, deviceId, created!.Id);

        read.ScheduleMode.Should().Be(ScheduleMode.AfterLastTime);
        read.NextDueDate.Should().Be(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    #endregion

    #region Update

    private static Task<HttpResponseMessage> UpdateTypeAsync(HttpClient client, Guid typeId, object body) =>
        client.PutAsJsonAsync($"/api/v1/maintenance-types/{typeId}", body);

    [Fact]
    public async Task Update_SwitchesBetweenModes_AndRecomputesFromTheExistingHistory()
    {
        var (client, _, deviceId) = await CreateClientWithDeviceAsync();
        // Record on 2026-03-01 (« mois » choice).
        var created = await (await CreateTypeAsync(client, deviceId, Body("Chaudière", "Annual", lastMaintenance: Month(2026, 3))))
            .Content.ReadAsJsonAsync<MaintenanceTypeDto>();
        (await GetTypeAsync(client, deviceId, created!.Id)).NextDueDate.Should().Be(new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        // → fixed date, every 1 October: March 1 2026 is closer to Oct 1 2025 (151 days) than to Oct 1 2026 (214 days) → it covers
        // the previous one, and the due date is Oct 1 2026.
        var toFixed = await UpdateTypeAsync(client, created.Id, new { scheduleMode = "FixedDate", anchorDay = 1, anchorMonth = 10 });
        toFixed.StatusCode.Should().Be(HttpStatusCode.OK);
        var fixedRead = await GetTypeAsync(client, deviceId, created.Id);
        fixedRead.ScheduleMode.Should().Be(ScheduleMode.FixedDate);
        fixedRead.NextDueDate.Should().Be(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        // → back to « après la dernière fois »: the anchor is cleared and the due date is again last record + period.
        var toRolling = await UpdateTypeAsync(client, created.Id, new { scheduleMode = "AfterLastTime" });
        toRolling.StatusCode.Should().Be(HttpStatusCode.OK);
        var rollingRead = await GetTypeAsync(client, deviceId, created.Id);
        rollingRead.ScheduleMode.Should().Be(ScheduleMode.AfterLastTime);
        rollingRead.AnchorDay.Should().BeNull();
        rollingRead.AnchorMonth.Should().BeNull();
        rollingRead.NextDueDate.Should().Be(new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Update_OmittedScheduleFields_KeepTheirValue()
    {
        var (client, _, deviceId) = await CreateClientWithDeviceAsync();
        var created = await (await CreateTypeAsync(client, deviceId, Body("Chaudière", "Annual", "FixedDate", 1, 10)))
            .Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        var response = await UpdateTypeAsync(client, created!.Id, new { name = "Chaudière gaz" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var read = await GetTypeAsync(client, deviceId, created.Id);
        read.Name.Should().Be("Chaudière gaz");
        read.ScheduleMode.Should().Be(ScheduleMode.FixedDate);
        read.AnchorDay.Should().Be(1);
        read.AnchorMonth.Should().Be(10);
    }

    [Fact]
    public async Task Update_ValidatesTheMergedState()
    {
        var (client, _, deviceId) = await CreateClientWithDeviceAsync();
        var created = await (await CreateTypeAsync(client, deviceId, Body("Chaudière", "Annual", "FixedDate", 1, 10)))
            .Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        // Fixed date + a periodicity without grid.
        (await UpdateTypeAsync(client, created!.Id, new { periodicity = "Biennial" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await UpdateTypeAsync(client, created.Id, new { periodicity = "Custom", customMonths = 18 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // 30 February (day sent, month kept from the stored anchor… then changed to February).
        (await UpdateTypeAsync(client, created.Id, new { anchorDay = 30, anchorMonth = 2 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // Anchor sent while going back to AfterLastTime.
        (await UpdateTypeAsync(client, created.Id, new { scheduleMode = "AfterLastTime", anchorDay = 1 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Nothing was stored by the refused updates.
        var read = await GetTypeAsync(client, deviceId, created.Id);
        read.ScheduleMode.Should().Be(ScheduleMode.FixedDate);
        read.Periodicity.Should().Be(Periodicity.Annual);
        read.AnchorDay.Should().Be(1);
        read.AnchorMonth.Should().Be(10);
    }

    [Fact]
    public async Task Update_AfterLastTimeType_CannotReceiveAnAnchorWithoutSwitchingMode()
    {
        var (client, _, deviceId) = await CreateClientWithDeviceAsync();
        var created = await (await CreateTypeAsync(client, deviceId, Body("Annuel", "Annual")))
            .Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        (await UpdateTypeAsync(client, created!.Id, new { anchorDay = 1, anchorMonth = 10 })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_FixedDateMonthlyToAnnual_NeedsAMonth_AndDropsItBackToMonthly()
    {
        var (client, _, deviceId) = await CreateClientWithDeviceAsync();
        var created = await (await CreateTypeAsync(client, deviceId, Body("Relevé", "Monthly", "FixedDate", anchorDay: 1)))
            .Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        (await UpdateTypeAsync(client, created!.Id, new { periodicity = "Annual" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await UpdateTypeAsync(client, created.Id, new { periodicity = "Annual", anchorMonth = 10 })).StatusCode.Should().Be(HttpStatusCode.OK);
        // Back to monthly: the month of the previous grid is dropped, not rejected.
        (await UpdateTypeAsync(client, created.Id, new { periodicity = "Monthly" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var read = await GetTypeAsync(client, deviceId, created.Id);
        read.AnchorMonth.Should().BeNull();
        read.AnchorDay.Should().Be(1);
    }

    #endregion
}
