using FluentAssertions;
using HouseFlow.Application.Common;
using HouseFlow.Application.Services;
using HouseFlow.Core.Entities;
using HouseFlow.UnitTests.Validation;

namespace HouseFlow.UnitTests.Services;

public class MaintenanceCalculatorServiceTests
{
    // 2026-06-15 10:00 UTC = 12:00 in Paris: "today" is 2026-06-15.
    private static readonly DateTime Today = Date(2026, 6, 15);
    private readonly MaintenanceCalculatorService _sut =
        new(new FixedTimeProvider(new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero)));

    private static DateTime Date(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    #region CalculateNextDueDate (R2: last date + periodicity)

    [Fact]
    public void CalculateNextDueDate_Annual_AddsOneYear()
    {
        _sut.CalculateNextDueDate(Date(2024, 3, 15), Periodicity.Annual, null).Should().Be(Date(2025, 3, 15));
    }

    [Fact]
    public void CalculateNextDueDate_Biennial_AddsTwoYears()
    {
        _sut.CalculateNextDueDate(Date(2024, 3, 15), Periodicity.Biennial, null).Should().Be(Date(2026, 3, 15));
    }

    [Fact]
    public void CalculateNextDueDate_Semestrial_AddsSixMonths()
    {
        _sut.CalculateNextDueDate(Date(2024, 3, 15), Periodicity.Semestrial, null).Should().Be(Date(2024, 9, 15));
    }

    [Fact]
    public void CalculateNextDueDate_Quarterly_AddsThreeMonths()
    {
        _sut.CalculateNextDueDate(Date(2024, 3, 15), Periodicity.Quarterly, null).Should().Be(Date(2024, 6, 15));
    }

    [Fact]
    public void CalculateNextDueDate_Monthly_AddsOneMonth()
    {
        _sut.CalculateNextDueDate(Date(2024, 3, 15), Periodicity.Monthly, null).Should().Be(Date(2024, 4, 15));
    }

    [Fact]
    public void CalculateNextDueDate_CustomMonths_AddsCalendarMonths()
    {
        _sut.CalculateNextDueDate(Date(2024, 3, 15), Periodicity.Custom, null, customMonths: 18)
            .Should().Be(Date(2025, 9, 15));
    }

    [Fact]
    public void CalculateNextDueDate_CustomMonths_WinsOverCustomDays()
    {
        _sut.CalculateNextDueDate(Date(2024, 3, 15), Periodicity.Custom, 10, customMonths: 2)
            .Should().Be(Date(2024, 5, 15));
    }

    [Fact]
    public void CalculateNextDueDate_CustomDays_AddsSpecifiedDays()
    {
        _sut.CalculateNextDueDate(Date(2024, 3, 15), Periodicity.Custom, 45).Should().Be(Date(2024, 4, 29));
    }

    [Fact]
    public void CalculateNextDueDate_CustomWithoutInterval_ThrowsArgumentException()
    {
        var act = () => _sut.CalculateNextDueDate(Date(2024, 3, 15), Periodicity.Custom, null);
        act.Should().Throw<ArgumentException>().WithParameterName("customDays");
    }

    [Fact]
    public void CalculateNextDueDate_LeapYear_Feb29_AnnualGoesToFeb28()
    {
        _sut.CalculateNextDueDate(Date(2024, 2, 29), Periodicity.Annual, null).Should().Be(Date(2025, 2, 28));
    }

    [Fact]
    public void CalculateNextDueDate_UnknownPeriodicity_DefaultsToOneYear()
    {
        _sut.CalculateNextDueDate(Date(2024, 3, 15), (Periodicity)999, null).Should().Be(Date(2025, 3, 15));
    }

    [Fact]
    public void CalculateNextDueDate_IgnoresTimeOfDay()
    {
        var lastDate = new DateTime(2024, 3, 15, 22, 30, 0, DateTimeKind.Utc);
        _sut.CalculateNextDueDate(lastDate, Periodicity.Annual, null).Should().Be(Date(2025, 3, 15));
    }

    #endregion

    #region CalculateNextDueDate (R2: no history)

    [Fact]
    public void CalculateNextDueDate_NoHistory_UsesStoredBaseline()
    {
        var snapshot = Snapshot(Periodicity.Annual, createdAt: Date(2026, 1, 10), baseline: Date(2026, 1, 10), last: null);
        _sut.CalculateNextDueDate(snapshot).Should().Be(Date(2026, 1, 10));
    }

    [Fact]
    public void CalculateNextDueDate_NoHistoryNoBaseline_IsCreationPlus30Days()
    {
        // Rows created before BaselineDueDate existed: « Je ne sais pas » rule.
        var snapshot = Snapshot(Periodicity.Annual, createdAt: new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), baseline: null, last: null);
        _sut.CalculateNextDueDate(snapshot).Should().Be(Date(2026, 7, 1));
    }

    [Fact]
    public void CalculateNextDueDate_WithHistory_IgnoresBaseline()
    {
        var snapshot = Snapshot(Periodicity.Annual, createdAt: Date(2026, 1, 1), baseline: Date(2026, 1, 1), last: Date(2026, 3, 1));
        _sut.CalculateNextDueDate(snapshot).Should().Be(Date(2027, 3, 1));
    }

    [Fact]
    public void NoHistoryBaseline_Unknown_IsParisCreationDatePlus30Days()
    {
        // 2026-03-10 23:30 UTC is already 2026-03-11 in Paris.
        _sut.NoHistoryBaseline(new DateTime(2026, 3, 10, 23, 30, 0, DateTimeKind.Utc), olderThanKnown: false)
            .Should().Be(Date(2026, 4, 10));
    }

    [Fact]
    public void NoHistoryBaseline_Older_IsParisCreationDate()
    {
        _sut.NoHistoryBaseline(new DateTime(2026, 3, 10, 23, 30, 0, DateTimeKind.Utc), olderThanKnown: true)
            .Should().Be(Date(2026, 3, 11));
    }

    #endregion

    #region CalculateStatus (R1)

    [Fact]
    public void CalculateStatus_Yesterday_IsOverdue()
    {
        _sut.CalculateStatus(Today.AddDays(-1), Today).Should().Be("overdue");
    }

    [Fact]
    public void CalculateStatus_Today_IsPending()
    {
        _sut.CalculateStatus(Today, Today).Should().Be("pending");
    }

    [Fact]
    public void CalculateStatus_Exactly30DaysAway_IsPending()
    {
        _sut.CalculateStatus(Today.AddDays(30), Today).Should().Be("pending");
    }

    [Fact]
    public void CalculateStatus_31DaysAway_IsUpToDate()
    {
        _sut.CalculateStatus(Today.AddDays(31), Today).Should().Be("up_to_date");
    }

    [Fact]
    public void Today_UsesEuropeParisCalendarDay()
    {
        // 2026-06-15 22:30 UTC = 2026-06-16 00:30 in Paris (UTC+2 in summer).
        var sut = new MaintenanceCalculatorService(new FixedTimeProvider(new DateTimeOffset(2026, 6, 15, 22, 30, 0, TimeSpan.Zero)));
        sut.Today.Should().Be(Date(2026, 6, 16));
    }

    #endregion

    #region Summarize (R1 aggregate, R3 counters)

    [Fact]
    public void Summarize_NoTypes_StatusNone()
    {
        var result = _sut.Summarize(Array.Empty<MaintenanceTypeSnapshot>());

        result.Status.Should().Be("none");
        result.Total.Should().Be(0);
        result.UpToDate.Should().Be(0);
        result.Score.Should().Be(100);
    }

    [Fact]
    public void Summarize_AllUpToDate()
    {
        var result = _sut.Summarize(new[]
        {
            Snapshot(Periodicity.Annual, last: Today.AddMonths(-1)),
            Snapshot(Periodicity.Annual, last: Today.AddMonths(-2)),
        });

        result.Status.Should().Be("up_to_date");
        result.UpToDate.Should().Be(2);
        result.Total.Should().Be(2);
        result.Score.Should().Be(100);
    }

    [Fact]
    public void Summarize_MixedStatuses_CountsEachSeparately_WorstWins()
    {
        var result = _sut.Summarize(new[]
        {
            Snapshot(Periodicity.Annual, last: Today.AddMonths(-1)),       // up to date
            Snapshot(Periodicity.Monthly, last: Today.AddDays(-10)),       // due in ~20 days → pending
            Snapshot(Periodicity.Monthly, last: Today.AddMonths(-3)),      // overdue
            Snapshot(Periodicity.Annual, createdAt: Today, baseline: Today.AddDays(30), last: null) // unknown → pending
        });

        result.Status.Should().Be("overdue");
        result.Overdue.Should().Be(1);
        result.Pending.Should().Be(2);
        result.UpToDate.Should().Be(1);
        result.Total.Should().Be(4);
        result.Score.Should().Be(25);
    }

    [Fact]
    public void Summarize_PendingOnly_StatusPending()
    {
        var result = _sut.Summarize(new[] { Snapshot(Periodicity.Annual, createdAt: Today, baseline: Today.AddDays(30), last: null) });
        result.Status.Should().Be("pending");
    }

    [Fact]
    public void Summarize_OlderThanKnown_IsPendingOnCreationDayThenOverdue()
    {
        var created = Snapshot(Periodicity.Annual, createdAt: Today, baseline: Today, last: null);
        _sut.Summarize(new[] { created }).Status.Should().Be("pending");

        var createdYesterday = Snapshot(Periodicity.Annual, createdAt: Today.AddDays(-1), baseline: Today.AddDays(-1), last: null);
        _sut.Summarize(new[] { createdYesterday }).Status.Should().Be("overdue");
    }

    #endregion

    #region CalculateMaintenanceTypeWithStatus

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_NoInstance_NextDueDateNeverNull()
    {
        var snapshot = Snapshot(Periodicity.Annual, createdAt: Today, baseline: null, last: null);

        var result = _sut.CalculateMaintenanceTypeWithStatus(snapshot);

        result.LastMaintenanceDate.Should().BeNull();
        result.NextDueDate.Should().Be(Today.AddDays(30));
        result.Status.Should().Be("pending");
    }

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_WithInstance_ReturnsDatesAndStatus()
    {
        var id = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var snapshot = new MaintenanceTypeSnapshot(id, "Filter", Periodicity.Custom, null, 24, deviceId, Date(2020, 1, 1), null, Date(2025, 1, 10));

        var result = _sut.CalculateMaintenanceTypeWithStatus(snapshot);

        result.Id.Should().Be(id);
        result.Name.Should().Be("Filter");
        result.DeviceId.Should().Be(deviceId);
        result.CustomMonths.Should().Be(24);
        result.LastMaintenanceDate.Should().Be(Date(2025, 1, 10));
        result.NextDueDate.Should().Be(Date(2027, 1, 10));
        result.Status.Should().Be("up_to_date");
    }

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_OverdueInstance()
    {
        var result = _sut.CalculateMaintenanceTypeWithStatus(Snapshot(Periodicity.Monthly, last: Today.AddMonths(-3)));
        result.Status.Should().Be("overdue");
    }

    [Fact]
    public void Snapshot_FromEntity_TakesLatestInstance()
    {
        var type = new MaintenanceType
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Periodicity = Periodicity.Annual,
            CreatedAt = Date(2020, 1, 1),
            MaintenanceInstances =
            {
                new MaintenanceInstance { Id = Guid.NewGuid(), Date = Date(2025, 1, 1) },
                new MaintenanceInstance { Id = Guid.NewGuid(), Date = Date(2026, 1, 1) },
                new MaintenanceInstance { Id = Guid.NewGuid(), Date = Date(2024, 1, 1) },
            }
        };

        MaintenanceTypeSnapshot.From(type).LastMaintenanceDate.Should().Be(Date(2026, 1, 1));
    }

    #endregion

    #region MostUrgent (P09 C4 row: most urgent maintenance of a device)

    [Fact]
    public void MostUrgent_ReturnsEarliestNextDueDate()
    {
        var upToDate = Snapshot(Periodicity.Annual, last: Today.AddMonths(-1)) with { Name = "Entretien annuel" };
        var overdue = Snapshot(Periodicity.Monthly, last: Today.AddMonths(-3)) with { Name = "Ramonage" };
        var due = Snapshot(Periodicity.Annual, last: Today.AddMonths(-12).AddDays(10)) with { Name = "Test" };

        var result = _sut.MostUrgent(new[] { upToDate, due, overdue });

        result.Should().NotBeNull();
        result!.Name.Should().Be("Ramonage");
        result.NextDueDate.Should().Be(Today.AddMonths(-2));
        result.Status.Should().Be(MaintenanceStatuses.Overdue);
    }

    [Fact]
    public void MostUrgent_Empty_ReturnsNull()
    {
        _sut.MostUrgent(Array.Empty<MaintenanceTypeSnapshot>()).Should().BeNull();
    }

    #endregion

    private static MaintenanceTypeSnapshot Snapshot(
        Periodicity periodicity, DateTime? createdAt = null, DateTime? baseline = null, DateTime? last = null) =>
        new(Guid.NewGuid(), "Type", periodicity, null, null, Guid.NewGuid(), createdAt ?? Date(2020, 1, 1), baseline, last);
}
