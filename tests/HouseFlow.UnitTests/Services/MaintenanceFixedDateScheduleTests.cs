using FluentAssertions;
using HouseFlow.Application.Common;
using HouseFlow.Application.Services;
using HouseFlow.Core.Entities;
using HouseFlow.UnitTests.Validation;

namespace HouseFlow.UnitTests.Services;

/// <summary>
/// « À date fixe » (issue #294): due dates on a calendar grid, whatever the real date of the last record.
/// The same cases are replayed client-side in <c>FrontendRulesTests</c> (PeriodicityRules) — keep both in step.
/// </summary>
public class MaintenanceFixedDateScheduleTests
{
    // 2026-06-15 10:00 UTC = 12:00 in Paris.
    private readonly MaintenanceCalculatorService _sut =
        new(new FixedTimeProvider(new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero)));

    private static DateTime Date(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static MaintenanceSchedule Fixed(Periodicity periodicity, int day, int? month = null) =>
        new(periodicity, null, null, ScheduleMode.FixedDate, day, month);

    private static MaintenanceTypeSnapshot Snapshot(
        MaintenanceSchedule schedule, DateTime createdAt, DateTime? baseline = null, DateTime? last = null) =>
        new(Guid.NewGuid(), "Type", schedule.Periodicity, schedule.CustomDays, schedule.CustomMonths,
            schedule.Mode, schedule.AnchorDay, schedule.AnchorMonth, Guid.NewGuid(), createdAt, baseline, last);

    #region Next due date after a record

    [Theory]
    [InlineData(2026, 9, 29, 2026, 11, 1)] // done early: covers Oct 1, next is Nov 1
    [InlineData(2026, 10, 5, 2026, 11, 1)] // done late: still covers Oct 1
    [InlineData(2026, 10, 3, 2026, 11, 1)] // several occurrences missed (Jul-Sep): one record catches up, nothing is claimed back
    [InlineData(2026, 10, 1, 2026, 11, 1)] // done on the occurrence itself
    [InlineData(2026, 12, 31, 2027, 2, 1)] // one day before Jan 1: covers it
    public void Monthly_OnTheFirst_IsDueTheOccurrenceAfterTheOneCovered(int y, int m, int d, int ey, int em, int ed)
    {
        _sut.CalculateNextDueDate(Snapshot(Fixed(Periodicity.Monthly, 1), Date(2025, 1, 1), last: Date(y, m, d)))
            .Should().Be(Date(ey, em, ed));
    }

    [Fact]
    public void Monthly_TieBetweenTwoOccurrences_CoversTheUpcomingOne()
    {
        // Occurrences Jun 15 and Jul 15: Jun 30 is 15 days from each → the upcoming (Jul 15) is covered → due Aug 15.
        Fixed(Periodicity.Monthly, 15).NextDue(Date(2026, 6, 30)).Should().Be(Date(2026, 8, 15));
        // One day earlier it is closer to Jun 15 → due Jul 15.
        Fixed(Periodicity.Monthly, 15).NextDue(Date(2026, 6, 29)).Should().Be(Date(2026, 7, 15));
    }

    [Theory]
    [InlineData(2026, 6, 15, 2027, 10, 1)] // less than half a year early: covers the coming Oct 1
    [InlineData(2026, 3, 15, 2026, 10, 1)] // more than half a year early: covers last Oct 1 (overdue) → due this Oct 1
    [InlineData(2026, 10, 1, 2027, 10, 1)]
    [InlineData(2026, 11, 20, 2027, 10, 1)]
    public void Annual_AnchoredOnOctober1(int y, int m, int d, int ey, int em, int ed)
    {
        Fixed(Periodicity.Annual, 1, 10).NextDue(Date(y, m, d)).Should().Be(Date(ey, em, ed));
    }

    [Theory]
    [InlineData(2027, 2, 20, 2027, 3, 31)] // Feb has 28 days: the 31st becomes the 28th, without drift afterwards
    [InlineData(2027, 3, 1, 2027, 3, 31)]
    [InlineData(2027, 3, 25, 2027, 4, 30)] // closer to Mar 31 → covers it → due Apr 30 (30-day month)
    [InlineData(2028, 2, 20, 2028, 3, 31)] // leap year: Feb 29
    public void Monthly_AnchoredOn31_FallsOnTheLastDayOfShortMonths(int y, int m, int d, int ey, int em, int ed)
    {
        Fixed(Periodicity.Monthly, 31).NextDue(Date(y, m, d)).Should().Be(Date(ey, em, ed));
    }

    [Fact]
    public void Annual_AnchoredOnFeb29_UsesFeb28InCommonYearsAndFeb29InLeapYears()
    {
        var schedule = Fixed(Periodicity.Annual, 29, 2);
        schedule.NextDue(Date(2027, 3, 10)).Should().Be(Date(2028, 2, 29)); // covers 2027-02-28
        schedule.NextDue(Date(2028, 3, 10)).Should().Be(Date(2029, 2, 28)); // covers 2028-02-29
    }

    [Theory]
    [InlineData(2026, 11, 20, 2027, 2, 1)]  // grid Nov 1, Feb 1, May 1, Aug 1: crosses into the next year
    [InlineData(2026, 12, 20, 2027, 5, 1)]  // Feb 1 (43 days) is closer than Nov 1 (49 days) → covers Feb 1
    [InlineData(2026, 6, 15, 2026, 8, 1)]   // May 1 (45 days) is closer than Aug 1 (47 days) → covers May 1
    public void Quarterly_AnchoredInNovember_FollowsTheGridAcrossYears(int y, int m, int d, int ey, int em, int ed)
    {
        Fixed(Periodicity.Quarterly, 1, 11).NextDue(Date(y, m, d)).Should().Be(Date(ey, em, ed));
    }

    [Fact]
    public void Semestrial_AnchoredInMarch_GridIsMarchAndSeptember()
    {
        var schedule = Fixed(Periodicity.Semestrial, 10, 3);
        schedule.NextDue(Date(2026, 3, 12)).Should().Be(Date(2026, 9, 10));
        schedule.NextDue(Date(2026, 8, 1)).Should().Be(Date(2027, 3, 10)); // covers Sep 10
    }

    [Fact]
    public void ARecordIgnoresTimeOfDay()
    {
        Fixed(Periodicity.Monthly, 1).NextDue(new DateTime(2026, 9, 29, 23, 30, 0, DateTimeKind.Utc)).Should().Be(Date(2026, 11, 1));
    }

    #endregion

    #region No history

    [Fact]
    public void NeverDone_Older_IsOverdueRightAway_OnTheCreationDay()
    {
        var created = new DateTime(2026, 6, 10, 8, 0, 0, DateTimeKind.Utc);
        var snapshot = Snapshot(Fixed(Periodicity.Monthly, 1), created, baseline: _sut.NoHistoryBaseline(created, olderThanKnown: true));

        _sut.CalculateNextDueDate(snapshot).Should().Be(Date(2026, 6, 10));
    }

    [Fact]
    public void NeverDone_Unknown_IsDueOnTheFirstOccurrenceFromCreationPlus30Days()
    {
        var created = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc); // + 30 days = Jul 1
        var baseline = _sut.NoHistoryBaseline(created, olderThanKnown: false);

        _sut.CalculateNextDueDate(Snapshot(Fixed(Periodicity.Monthly, 15), created, baseline)).Should().Be(Date(2026, 7, 15));
        _sut.CalculateNextDueDate(Snapshot(Fixed(Periodicity.Monthly, 1), created, baseline)).Should().Be(Date(2026, 7, 1)); // on the grid: kept
        _sut.CalculateNextDueDate(Snapshot(Fixed(Periodicity.Annual, 1, 10), created, baseline)).Should().Be(Date(2026, 10, 1));
    }

    [Fact]
    public void NeverDone_LegacyRowWithoutBaseline_UsesCreationPlus30DaysToo()
    {
        var created = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

        _sut.CalculateNextDueDate(Snapshot(Fixed(Periodicity.Monthly, 15), created, baseline: null)).Should().Be(Date(2026, 7, 15));
    }

    [Fact]
    public void AfterLastTime_NeverDone_IsUnchanged()
    {
        var created = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        var schedule = new MaintenanceSchedule(Periodicity.Monthly, null, null);

        _sut.CalculateNextDueDate(Snapshot(schedule, created, baseline: Date(2026, 7, 1))).Should().Be(Date(2026, 7, 1));
    }

    #endregion

    #region Status window

    [Theory]
    [InlineData(4, "pending")]     // Jul 1 → Aug 1 = 31 days, 10 % = 3.1 → 4 days
    [InlineData(5, "up_to_date")]
    [InlineData(0, "pending")]
    [InlineData(-1, "overdue")]
    public void Monthly_WindowIsTenPercentOfTheGapToTheNextOccurrence(int daysBeforeDue, string expected)
    {
        var due = Date(2026, 7, 1);
        _sut.CalculateStatus(due, due.AddDays(-daysBeforeDue), Fixed(Periodicity.Monthly, 1)).Should().Be(expected);
    }

    [Theory]
    [InlineData(37, "pending")]    // Oct 1 2026 → Oct 1 2027 = 365 days, 10 % = 36.5 → 37 days
    [InlineData(38, "up_to_date")]
    public void Annual_WindowIsTenPercentOfTheYear(int daysBeforeDue, string expected)
    {
        var due = Date(2026, 10, 1);
        _sut.CalculateStatus(due, due.AddDays(-daysBeforeDue), Fixed(Periodicity.Annual, 1, 10)).Should().Be(expected);
    }

    [Fact]
    public void Summarize_CountsFixedDateTypesWithTheirOwnDueDate()
    {
        // Done Jun 3 → covers Jun 1 → due Jul 1 (16 days after Jun 15): up to date.
        var fixedType = Snapshot(Fixed(Periodicity.Monthly, 1), Date(2026, 1, 1), last: Date(2026, 6, 3));
        // « Après la dernière fois » monthly done May 3: due Jun 3, already past.
        var rolling = Snapshot(new MaintenanceSchedule(Periodicity.Monthly, null, null), Date(2026, 1, 1), last: Date(2026, 5, 3));

        var summary = _sut.Summarize([fixedType, rolling]);

        summary.Total.Should().Be(2);
        summary.UpToDate.Should().Be(1);
        summary.Overdue.Should().Be(1);
    }

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_CarriesTheScheduleAndTheFixedDueDate()
    {
        var snapshot = Snapshot(Fixed(Periodicity.Annual, 1, 10), Date(2020, 1, 1), last: Date(2026, 3, 15));

        var result = _sut.CalculateMaintenanceTypeWithStatus(snapshot);

        result.ScheduleMode.Should().Be(ScheduleMode.FixedDate);
        result.AnchorDay.Should().Be(1);
        result.AnchorMonth.Should().Be(10);
        result.NextDueDate.Should().Be(Date(2026, 10, 1));
        result.Status.Should().Be("up_to_date");
    }

    #endregion

    #region Validation

    [Theory]
    [InlineData(Periodicity.Monthly, 1, null)]
    [InlineData(Periodicity.Monthly, 31, null)]
    [InlineData(Periodicity.Quarterly, 15, 11)]
    [InlineData(Periodicity.Semestrial, 1, 3)]
    [InlineData(Periodicity.Annual, 1, 10)]
    [InlineData(Periodicity.Annual, 29, 2)] // accepted, brought back to the 28th in common years
    [InlineData(Periodicity.Annual, 31, 12)]
    public void Validate_FixedDate_AcceptsAConsistentAnchor(Periodicity periodicity, int day, int? month)
    {
        var act = () => MaintenanceSchedule.Validate(periodicity, null, null, ScheduleMode.FixedDate, day, month);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(Periodicity.Custom, 1, null)]    // no grid
    [InlineData(Periodicity.Biennial, 1, 1)]     // no grid
    [InlineData(Periodicity.Annual, null, 10)]   // day missing
    [InlineData(Periodicity.Annual, 1, null)]    // month missing
    [InlineData(Periodicity.Quarterly, 1, null)] // month missing
    [InlineData(Periodicity.Monthly, 1, 3)]      // month forbidden in monthly
    [InlineData(Periodicity.Annual, 30, 2)]      // 30 February
    [InlineData(Periodicity.Annual, 31, 4)]      // 31 April
    [InlineData(Periodicity.Monthly, 0, null)]
    [InlineData(Periodicity.Monthly, 32, null)]
    [InlineData(Periodicity.Annual, 1, 13)]
    public void Validate_FixedDate_RejectsAnInconsistentAnchor(Periodicity periodicity, int? day, int? month)
    {
        var act = () => MaintenanceSchedule.Validate(periodicity, null, null, ScheduleMode.FixedDate, day, month);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_FixedDate_RejectsACustomInterval()
    {
        var act = () => MaintenanceSchedule.Validate(Periodicity.Monthly, null, 2, ScheduleMode.FixedDate, 1, null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(null, 3)]
    public void Validate_AfterLastTime_RejectsAnyAnchor(int? day, int? month)
    {
        var act = () => MaintenanceSchedule.Validate(Periodicity.Annual, null, null, ScheduleMode.AfterLastTime, day, month);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_AfterLastTime_AcceptsNoAnchor_EvenCustom()
    {
        var act = () => MaintenanceSchedule.Validate(Periodicity.Custom, null, 18, ScheduleMode.AfterLastTime, null, null);

        act.Should().NotThrow();
    }

    #endregion
}
