namespace HouseFlow.Web.Rules;

/// <summary>
/// Maintenance periodicity (M4, P10 subtitles, R2 next due date). Mirrors the API values
/// (Annual | Semestrial | Quarterly | Monthly | Biennial | Custom with customMonths, legacy
/// customDays) and the backend <c>MaintenanceCalculatorService.CalculateNextDueDate</c>.
/// </summary>
public static class PeriodicityRules
{
    public const string Annual = "Annual";
    public const string Semestrial = "Semestrial";
    public const string Quarterly = "Quarterly";
    public const string Monthly = "Monthly";
    public const string Biennial = "Biennial";
    public const string Custom = "Custom";

    /// <summary>Period length in months; null for a legacy day-based custom period.</summary>
    public static int? Months(string? periodicity, int? customMonths) => periodicity switch
    {
        Annual => 12,
        Biennial => 24,
        Semestrial => 6,
        Quarterly => 3,
        Monthly => 1,
        Custom => customMonths,
        _ => 12,
    };

    public const string AfterLastTime = "AfterLastTime";
    public const string FixedDate = "FixedDate";

    public static bool IsFixedDate(string? scheduleMode) => scheduleMode == FixedDate;

    /// <summary>Months between two occurrences of the « À date fixe » grid (1, 3, 6, 12); null when the periodicity has none.</summary>
    public static int? GridMonths(string? periodicity) => periodicity switch
    {
        Monthly => 1,
        Quarterly => 3,
        Semestrial => 6,
        Annual => 12,
        _ => null,
    };

    /// <summary>
    /// R2: next due date after a record made on <paramref name="done"/>. « Après la dernière fois » (default): the
    /// date + the period. « À date fixe »: the occurrence after the one the record covers — the occurrence nearest to
    /// its date, the upcoming one on a tie. Mirrors <c>MaintenanceSchedule.NextDue</c> on the backend (same unit cases).
    /// </summary>
    public static DateOnly NextDue(
        DateOnly done, string? periodicity, int? customMonths, int? customDays,
        string? scheduleMode = null, int? anchorDay = null, int? anchorMonth = null)
    {
        if (IsFixedDate(scheduleMode) && GridMonths(periodicity) is { } step)
        {
            var previous = LastOccurrenceOnOrBefore(done, step, anchorDay, anchorMonth);
            var next = OccurrenceAfter(done, step, anchorDay, anchorMonth);
            var covered = done.DayNumber - previous.DayNumber < next.DayNumber - done.DayNumber ? previous : next;
            return OccurrenceAfter(covered, step, anchorDay, anchorMonth);
        }
        if (Months(periodicity, customMonths) is { } m) return done.AddMonths(m);
        return done.AddDays(customDays is > 0 ? customDays.Value : 365);
    }

    /// <summary>First grid occurrence on or after <paramref name="date"/> (« Je ne sais pas »: from creation + 30 days).</summary>
    public static DateOnly FirstOccurrenceOnOrAfter(DateOnly date, string? periodicity, int? anchorDay, int? anchorMonth) =>
        GridMonths(periodicity) is { } step ? OccurrenceAfter(date.AddDays(-1), step, anchorDay, anchorMonth) : date;

    // Occurrences sit on month indexes (year * 12 + month - 1) congruent to the anchor month modulo the step;
    // the day is recomputed for each occurrence (min(anchorDay, days in month)) so an anchor on the 31st never drifts.
    private static int Phase(int step, int? anchorMonth) => step == 1 ? 0 : ((anchorMonth ?? 1) - 1) % step;

    private static DateOnly Occurrence(int monthIndex, int? anchorDay)
    {
        var year = monthIndex / 12;
        var month = monthIndex % 12 + 1;
        return new DateOnly(year, month, Math.Min(anchorDay ?? 1, DateTime.DaysInMonth(year, month)));
    }

    private static int MonthIndexOnOrBefore(DateOnly date, int step, int? anchorMonth)
    {
        var index = date.Year * 12 + date.Month - 1;
        return index - ((index - Phase(step, anchorMonth)) % step + step) % step;
    }

    private static DateOnly LastOccurrenceOnOrBefore(DateOnly date, int step, int? anchorDay, int? anchorMonth)
    {
        var index = MonthIndexOnOrBefore(date, step, anchorMonth);
        var occurrence = Occurrence(index, anchorDay);
        return occurrence <= date ? occurrence : Occurrence(index - step, anchorDay);
    }

    private static DateOnly OccurrenceAfter(DateOnly date, int step, int? anchorDay, int? anchorMonth)
    {
        var index = MonthIndexOnOrBefore(date, step, anchorMonth);
        var occurrence = Occurrence(index, anchorDay);
        return occurrence > date ? occurrence : Occurrence(index + step, anchorDay);
    }

    /// <summary>
    /// « Le 1er de chaque mois » / « Chaque année le 1er octobre » / « Tous les 3 mois, à partir du 1er janvier »
    /// (keys <c>maintenance.fixedMonthly</c>, <c>fixedYearly</c>, <c>fixedEvery</c>; day and month are pre-formatted
    /// for the locale: « 1er » / « 1st », « octobre » / « October »).
    /// </summary>
    public static string FixedDateLabel(
        string? periodicity, int? anchorDay, int? anchorMonth, string? locale, Func<string, object?, string> t)
    {
        var day = OrdinalDay(anchorDay ?? 1, locale);
        var month = anchorMonth is >= 1 and <= 12
            ? new DateOnly(2001, anchorMonth.Value, 1).ToString("MMMM", DateFormatter.Culture(locale))
            : "";
        return periodicity switch
        {
            Monthly => t("maintenance.fixedMonthly", new { day }),
            Annual => t("maintenance.fixedYearly", new { day, month }),
            _ => t("maintenance.fixedEvery", new { count = GridMonths(periodicity) ?? 0, day, month }),
        };
    }

    /// <summary>« 1er » then « 2 », « 15 » in French; « 1st », « 2nd », « 3rd », « 4th »… in English.</summary>
    public static string OrdinalDay(int day, string? locale)
    {
        if (locale != "en") return day == 1 ? "1er" : day.ToString();
        var suffix = day % 100 is >= 11 and <= 13 ? "th" : (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return day + suffix;
    }

    /// <summary>
    /// "Tous les ans" / "Tous les 6 mois" / "Tous les 2 ans": translation key + count.
    /// Whole years are said in years (24 months → "Tous les 2 ans").
    /// Keys: <c>maintenance.everyYears</c>, <c>maintenance.everyMonths</c>, <c>maintenance.everyDays</c>.
    /// </summary>
    public static (string Key, int Count) Words(string? periodicity, int? customMonths, int? customDays)
    {
        if (Months(periodicity, customMonths) is { } m)
            return m % 12 == 0 ? ("maintenance.everyYears", m / 12) : ("maintenance.everyMonths", m);
        return ("maintenance.everyDays", customDays ?? 0);
    }
}
