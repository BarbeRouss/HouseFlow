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

    /// <summary>R2: next due date after a record made on <paramref name="done"/>.</summary>
    public static DateOnly NextDue(DateOnly done, string? periodicity, int? customMonths, int? customDays)
    {
        if (Months(periodicity, customMonths) is { } m) return done.AddMonths(m);
        return done.AddDays(customDays is > 0 ? customDays.Value : 365);
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
