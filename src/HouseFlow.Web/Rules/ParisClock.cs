namespace HouseFlow.Web.Rules;

/// <summary>
/// "Today" for every status / relative-date computation (R1, R4): the calendar date in
/// Europe/Paris, whatever the browser's time zone. Single source, so the front end and the
/// API (which uses the same zone) agree on what "overdue" means around midnight.
/// </summary>
public static class ParisClock
{
    private static readonly TimeZoneInfo Zone = ResolveZone();

    /// <summary>Test seam: overrides the current instant (null = system clock).</summary>
    public static Func<DateTimeOffset>? NowOverride { get; set; }

    public static DateTimeOffset Now => NowOverride?.Invoke() ?? DateTimeOffset.UtcNow;

    /// <summary>Today's date in Europe/Paris.</summary>
    public static DateOnly Today => ToParisDate(Now);

    /// <summary>Calendar date of an instant, seen from Europe/Paris.</summary>
    public static DateOnly ToParisDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);

    private static TimeZoneInfo ResolveZone()
    {
        // IANA id first (Linux, browser/WASM ICU data), then the Windows id.
        foreach (var id in new[] { "Europe/Paris", "Romance Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the next identifier.
            }
            catch (InvalidTimeZoneException)
            {
                // Corrupt zone data for this id: try the next identifier.
            }
        }
        // No time-zone data at all (invariant runtime): the local zone is the best we have.
        return TimeZoneInfo.Local;
    }
}
