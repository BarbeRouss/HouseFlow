namespace HouseFlow.Web.Rules;

/// <summary>
/// R2 at creation time: first due date of a new maintenance type from its « Dernier entretien »
/// choice, as the backend computes it (P06 live preview). Kinds mirror the API
/// <c>lastMaintenance.kind</c> values.
/// </summary>
public static class InitialDueRules
{
    public const string KindUnknown = "Unknown";
    public const string KindOlder = "Older";
    public const string KindMonth = "Month";

    /// <summary>« Je ne sais pas »: due this many days after creation.</summary>
    public const int UnknownGraceDays = 30;

    /// <summary>
    /// Month + year → a record on the 1st of that month, due one period later.
    /// « Plus ancien » → due on the creation day. « Je ne sais pas » (or an incomplete month) →
    /// creation day + 30 days.
    /// </summary>
    public static DateOnly FirstDue(string? kind, int? year, int? month, int periodMonths, DateOnly today) =>
        kind switch
        {
            KindMonth when year is { } y && month is >= 1 and <= 12 => new DateOnly(y, month.Value, 1).AddMonths(periodMonths),
            KindOlder => today,
            _ => today.AddDays(UnknownGraceDays),
        };

    /// <summary>
    /// Preview wording (P06): overdue → relative (R4 exception: "en retard de N j"), today →
    /// "aujourd'hui", within 60 days → absolute date ("26 oct. 2026"), beyond → month + year.
    /// </summary>
    public static string PreviewLabel(DateOnly due, DateOnly today, string? locale, Func<string, object?, string> t)
    {
        var days = due.DayNumber - today.DayNumber;
        if (days < 0) return t("dates.overdueBy", new { count = -days });
        if (days == 0) return t("dates.today", null);
        return days <= DateFormatter.RelativeWindowDays
            ? DateFormatter.Absolute(due, locale)
            : DateFormatter.MonthYear(due, locale);
    }
}
