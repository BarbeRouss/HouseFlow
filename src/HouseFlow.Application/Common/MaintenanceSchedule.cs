using HouseFlow.Core.Entities;

namespace HouseFlow.Application.Common;

/// <summary>
/// How a maintenance type's due dates are laid out: its period and, for « À date fixe », the calendar grid.
/// Pure value object — the single place that turns « last record » into « next due date » (R2) and that knows the
/// length of the period after a due date (R1 window), for both schedule modes.
/// </summary>
/// <remarks>
/// <b>AfterLastTime</b> (default): due = last record + period (calendar months, or legacy days).<br/>
/// <b>FixedDate</b>: due dates are the grid occurrences — the <see cref="AnchorDay"/> of every month (monthly) or of
/// every 3 / 6 / 12 months starting from <see cref="AnchorMonth"/>. A record covers the occurrence <i>nearest</i> to its
/// date (the upcoming one on a tie) and the due date is the occurrence after it: it never depends on how early or late
/// the record was made, beyond which occurrence it falls closest to. The day is recomputed for each occurrence
/// (<c>min(anchorDay, days in month)</c>), never chained with AddMonths, so an anchor on the 31st does not drift.
/// Mirrored client-side by <c>HouseFlow.Web.Rules.PeriodicityRules</c> (same cases in both unit test suites).
/// </remarks>
public readonly record struct MaintenanceSchedule(
    Periodicity Periodicity,
    int? CustomDays,
    int? CustomMonths,
    ScheduleMode Mode = ScheduleMode.AfterLastTime,
    int? AnchorDay = null,
    int? AnchorMonth = null)
{
    public bool IsFixedDate => Mode == ScheduleMode.FixedDate;

    /// <summary>Months between two occurrences of the fixed-date grid (1, 3, 6 or 12); null if the periodicity has no grid.</summary>
    public static int? GridMonths(Periodicity periodicity) => periodicity switch
    {
        Periodicity.Monthly => 1,
        Periodicity.Quarterly => 3,
        Periodicity.Semestrial => 6,
        Periodicity.Annual => 12,
        _ => null
    };

    /// <summary>R2: the next due date after a record made on <paramref name="lastDate"/>.</summary>
    public DateTime NextDue(DateTime lastDate)
    {
        var date = ParisClock.AsDate(lastDate);
        return IsFixedDate ? NextDueOnGrid(date) : AfterPeriod(date);
    }

    /// <summary>Length in days of the period that follows a due date: the R1 « à venir » window is a share of it.</summary>
    public double PeriodDaysAfter(DateTime due)
    {
        var day = due.Date;
        var next = IsFixedDate ? OccurrenceAfter(day) : AfterPeriod(day);
        return (next - day).TotalDays;
    }

    /// <summary>
    /// R2 without history, fixed-date mode: the first grid occurrence on or after the stored baseline — except a baseline
    /// that is the creation day itself (« Plus ancien »), which stays as is: already overdue.
    /// </summary>
    public DateTime NoHistoryDue(DateTime baseline, DateTime createdOn)
    {
        var day = ParisClock.AsDate(baseline);
        return !IsFixedDate || day <= createdOn ? day : FirstOccurrenceOnOrAfter(day);
    }

    /// <summary>
    /// Validates the schedule as it will be stored (for an update: after merging with the existing values).
    /// « À date fixe » needs a periodicity with a grid (monthly to annual), an anchor day, and an anchor month
    /// except for monthly; « Après la dernière fois » carries no anchor. Throws <see cref="InvalidOperationException"/> (400).
    /// </summary>
    public static void Validate(
        Periodicity periodicity, int? customDays, int? customMonths, ScheduleMode mode, int? anchorDay, int? anchorMonth)
    {
        if (mode == ScheduleMode.AfterLastTime)
        {
            if (anchorDay is not null || anchorMonth is not null)
                throw new InvalidOperationException("anchorDay and anchorMonth are only allowed when scheduleMode is FixedDate.");
            return;
        }

        if (GridMonths(periodicity) is null)
            throw new InvalidOperationException("scheduleMode FixedDate requires a Monthly, Quarterly, Semestrial or Annual periodicity.");
        if (customDays is not null || customMonths is not null)
            throw new InvalidOperationException("customDays and customMonths are not allowed when scheduleMode is FixedDate.");
        if (anchorDay is null or < 1 or > 31)
            throw new InvalidOperationException("anchorDay (1-31) is required when scheduleMode is FixedDate.");

        if (periodicity == Periodicity.Monthly)
        {
            if (anchorMonth is not null)
                throw new InvalidOperationException("anchorMonth is not allowed for a Monthly FixedDate type.");
            return;
        }

        if (anchorMonth is null or < 1 or > 12)
            throw new InvalidOperationException("anchorMonth (1-12) is required for a Quarterly, Semestrial or Annual FixedDate type.");
        // 29 February is accepted (brought back to the 28th in non-leap years); 30 and 31 February are not.
        if (anchorDay > DateTime.DaysInMonth(2024, anchorMonth.Value))
            throw new InvalidOperationException($"anchorDay {anchorDay} does not exist in month {anchorMonth}.");
    }

    private DateTime AfterPeriod(DateTime date) => Periodicity switch
    {
        Periodicity.Annual => date.AddYears(1),
        Periodicity.Biennial => date.AddYears(2),
        Periodicity.Semestrial => date.AddMonths(6),
        Periodicity.Quarterly => date.AddMonths(3),
        Periodicity.Monthly => date.AddMonths(1),
        Periodicity.Custom when CustomMonths.HasValue => date.AddMonths(CustomMonths.Value),
        Periodicity.Custom when CustomDays.HasValue => date.AddDays(CustomDays.Value),
        Periodicity.Custom => throw new ArgumentException(
            "customMonths or customDays is required when periodicity is Custom.", "customDays"),
        _ => date.AddYears(1)
    };

    private DateTime NextDueOnGrid(DateTime date)
    {
        var previous = LastOccurrenceOnOrBefore(date);
        var next = OccurrenceAfter(date);
        // Nearest occurrence is covered; on a tie (strictly not closer), the upcoming one.
        var covered = (date - previous) < (next - date) ? previous : next;
        return OccurrenceAfter(covered);
    }

    // --- grid -------------------------------------------------------------------------------------------------

    private int Step => GridMonths(Periodicity)
        ?? throw new InvalidOperationException($"No fixed-date grid for periodicity {Periodicity}.");

    // Occurrences sit on month indexes (year * 12 + month - 1) congruent to the anchor month modulo the step.
    private int Phase => Periodicity == Periodicity.Monthly ? 0 : ((AnchorMonth ?? 1) - 1) % Step;

    private DateTime Occurrence(int monthIndex)
    {
        var year = monthIndex / 12;
        var month = monthIndex % 12 + 1;
        var day = Math.Min(AnchorDay ?? 1, DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    }

    private int MonthIndexOnOrBefore(DateTime date)
    {
        var index = date.Year * 12 + date.Month - 1;
        var gap = ((index - Phase) % Step + Step) % Step;
        return index - gap;
    }

    private DateTime LastOccurrenceOnOrBefore(DateTime date)
    {
        var occurrence = Occurrence(MonthIndexOnOrBefore(date));
        // The occurrence of the current grid month may fall after the date (e.g. the 15th when the date is the 10th).
        return occurrence <= date ? occurrence : Occurrence(MonthIndexOnOrBefore(date) - Step);
    }

    private DateTime OccurrenceAfter(DateTime date)
    {
        var occurrence = Occurrence(MonthIndexOnOrBefore(date));
        return occurrence > date ? occurrence : Occurrence(MonthIndexOnOrBefore(date) + Step);
    }

    private DateTime FirstOccurrenceOnOrAfter(DateTime date) => OccurrenceAfter(date.AddDays(-1));
}
