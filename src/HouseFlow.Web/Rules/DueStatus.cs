using System.Globalization;

namespace HouseFlow.Web.Rules;

/// <summary>R1 maintenance status. Order matters: lower = more urgent.</summary>
public enum DueStatus
{
    /// <summary>En retard: nextDueDate &lt; today.</summary>
    Overdue = 0,

    /// <summary>À faire: today ≤ nextDueDate ≤ today + 10 % of the period.</summary>
    Due = 1,

    /// <summary>À jour: nextDueDate beyond the "À faire" window.</summary>
    Ok = 2,
}

/// <summary>
/// R1 status rules — the single front-end source for the "À faire" window (10 % of the period). "Today" is always the
/// Europe/Paris date (<see cref="ParisClock"/>).
/// </summary>
public static class StatusRules
{
    /// <summary>A maintenance is "À faire" when due within this share of its period (inclusive, min. 1 day).</summary>
    public const double DueSoonWindowRatio = 0.10;

    /// <summary>Window in days for a period of <paramref name="periodMonths"/> months, counted from <paramref name="due"/>.</summary>
    public static int WindowDays(DateOnly due, int periodMonths) =>
        Math.Max(1, (int)Math.Ceiling((due.AddMonths(periodMonths).DayNumber - due.DayNumber) * DueSoonWindowRatio));

    /// <summary>Status of a due date; <paramref name="periodMonths"/> defaults to a year when the period is unknown.</summary>
    public static DueStatus Compute(DateOnly nextDueDate, DateOnly today, int periodMonths = 12)
    {
        if (nextDueDate < today) return DueStatus.Overdue;
        if (nextDueDate <= today.AddDays(WindowDays(nextDueDate, periodMonths))) return DueStatus.Due;
        return DueStatus.Ok;
    }

    public static DueStatus Compute(DateOnly nextDueDate, int periodMonths = 12) => Compute(nextDueDate, ParisClock.Today, periodMonths);

    /// <summary>Status from an API date string; null when the date is missing or unreadable.</summary>
    public static DueStatus? Compute(string? nextDueDate) =>
        DateFormatter.ParseDate(nextDueDate) is { } d ? Compute(d) : null;

    /// <summary>Maps the API status strings (<c>overdue</c>, <c>pending</c>, <c>up_to_date</c>).</summary>
    public static DueStatus? FromApi(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "overdue" => DueStatus.Overdue,
        "pending" or "due" => DueStatus.Due,
        "up_to_date" or "uptodate" or "ok" => DueStatus.Ok,
        _ => null,
    };

    /// <summary>Status of a device or house = its most urgent maintenance (R1). Null if none.</summary>
    public static DueStatus? MostUrgent(IEnumerable<DueStatus> statuses)
    {
        DueStatus? worst = null;
        foreach (var s in statuses)
            if (worst is null || s < worst) worst = s;
        return worst;
    }

    /// <summary>i18n key of the status label ("En retard", "À faire", "À jour").</summary>
    public static string LabelKey(DueStatus status) => status switch
    {
        DueStatus.Overdue => "status.overdue",
        DueStatus.Due => "status.due",
        _ => "status.ok",
    };
}

/// <summary>
/// R4 date formatting. Absolute dates use the app locale ("8 oct. 2026" / "Oct 8, 2026"),
/// never InvariantCulture. Relative labels apply within 60 days ("en retard de 24 j",
/// "aujourd'hui", "dans 12 j"); beyond, full month + year ("mars 2027", "octobre 2026") — except overdue dates, which
/// always stay relative ("en retard de 90 j", decision 21).
/// Pure functions: the translator is passed in so this stays unit-testable.
/// </summary>
public static class DateFormatter
{
    /// <summary>Relative wording is used when |due − today| ≤ this many days.</summary>
    public const int RelativeWindowDays = 60;

    public static CultureInfo Culture(string? locale) =>
        locale == "en" ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("fr-FR");

    private static string AbsolutePattern(string? locale) => locale == "en" ? "MMM d, yyyy" : "d MMM yyyy";

    /// <summary>"8 oct. 2026" (fr) / "Oct 8, 2026" (en).</summary>
    public static string Absolute(DateOnly date, string? locale) =>
        date.ToString(AbsolutePattern(locale), Culture(locale));

    /// <summary>Full month + year: "octobre 2026" (fr) / "October 2026" (en).</summary>
    public static string MonthYear(DateOnly date, string? locale) =>
        date.ToString("MMMM yyyy", Culture(locale));

    /// <summary>
    /// Capital initial when a label opens a cell or a sentence (specs/ux C3: « En retard de 8 j »,
    /// « Dans 12 j », « Octobre 2026 »): the French culture gives lower-case month names.
    /// </summary>
    public static string Capitalize(string text, string? locale) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0], Culture(locale)) + text[1..];

    /// <summary>
    /// R4 relative label for a due date. <paramref name="t"/> resolves the keys
    /// <c>dates.today</c>, <c>dates.overdueBy</c> and <c>dates.inDays</c> (arg <c>count</c>).
    /// </summary>
    public static string Relative(DateOnly due, DateOnly today, string? locale, Func<string, object?, string> t)
    {
        var days = due.DayNumber - today.DayNumber;
        if (days == 0) return t("dates.today", null);
        // Decision 21: an overdue item is always "en retard de N j", however old.
        if (days > RelativeWindowDays) return MonthYear(due, locale);
        return days < 0
            ? t("dates.overdueBy", new { count = -days })
            : t("dates.inDays", new { count = days });
    }

    /// <summary>
    /// Parses an API date ("2026-10-08", "2026-10-08T00:00:00Z", with or without offset) into the
    /// calendar date it denotes in Europe/Paris. Machine format → invariant parsing (display
    /// formatting is what R4 localizes). Null when missing or unreadable.
    /// </summary>
    public static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = value.Trim();

        // Pure date: no time-zone conversion.
        if (s.Length == 10 && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;

        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            return ParisClock.ToParisDate(dto);

        return null;
    }
}

/// <summary>Localized amounts (R4 spirit: never InvariantCulture for display).</summary>
public static class MoneyFormatter
{
    /// <summary>"1 250,5 €" (fr) / "1,250.5 €" (en): up to two decimals, euro sign after.</summary>
    public static string Euros(decimal amount, string? locale) =>
        amount.ToString("#,0.##", DateFormatter.Culture(locale)) + " €";
}
