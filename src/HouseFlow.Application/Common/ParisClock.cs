namespace HouseFlow.Application.Common;

/// <summary>
/// R1 — every maintenance status / due date is evaluated against the Europe/Paris calendar day,
/// whatever the server's timezone (containers run in UTC: without this, statuses flipped at
/// 00:00 UTC, i.e. 01:00 or 02:00 in Paris). Single source of "today" on the backend.
/// </summary>
/// <remarks>
/// Calendar dates (maintenance dates, due dates) are carried as the UTC midnight of that date —
/// the convention the frontend already uses when it sends <c>yyyy-MM-ddT00:00:00Z</c>.
/// </remarks>
public static class ParisClock
{
    public static readonly TimeZoneInfo Zone = ResolveZone();

    /// <summary>Today's calendar date in Europe/Paris, as a UTC-midnight <see cref="DateTime"/>.</summary>
    public static DateTime Today(TimeProvider? timeProvider = null) =>
        DateOf((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime);

    /// <summary>Europe/Paris calendar date of a UTC instant (e.g. a <c>CreatedAt</c>), as a UTC-midnight value.</summary>
    public static DateTime DateOf(DateTime utcInstant)
    {
        var utc = utcInstant.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc)
            : utcInstant.ToUniversalTime();
        return AsDate(TimeZoneInfo.ConvertTimeFromUtc(utc, Zone));
    }

    /// <summary>Calendar date component of a value that already denotes a date (e.g. a maintenance date).</summary>
    public static DateTime AsDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    private static TimeZoneInfo ResolveZone()
    {
        // The IANA id works on Linux and on Windows with ICU (.NET 6+); the Windows id is the
        // fallback for hosts without ICU data.
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Paris", out var zone)) return zone;
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Romance Standard Time", out zone)) return zone;
        return TimeZoneInfo.Utc;
    }
}
