using FluentAssertions;
using HouseFlow.Application.Common;

namespace HouseFlow.UnitTests.Common;

/// <summary>
/// R1 — "today" is the Europe/Paris calendar day. Around the daylight-saving changes the UTC offset
/// moves between +1 and +2: the Paris midnight must be found at 23:00 UTC in winter and 22:00 UTC in
/// summer, including on the two nights where the offset itself changes.
/// </summary>
public class ParisClockTests
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Theory]
    // Spring forward: Sunday 2026-03-29, 02:00 CET → 03:00 CEST (01:00 UTC).
    [InlineData("2026-03-28T22:59:00Z", "2026-03-28")] // 23:59 CET on the 28th
    [InlineData("2026-03-28T23:00:00Z", "2026-03-29")] // Paris midnight, still UTC+1
    [InlineData("2026-03-29T00:30:00Z", "2026-03-29")] // 01:30 CET, before the switch
    [InlineData("2026-03-29T01:30:00Z", "2026-03-29")] // 03:30 CEST, after the switch
    [InlineData("2026-03-29T21:59:00Z", "2026-03-29")] // 23:59 CEST
    [InlineData("2026-03-29T22:00:00Z", "2026-03-30")] // Paris midnight, now UTC+2
    // Fall back: Sunday 2026-10-25, 03:00 CEST → 02:00 CET (01:00 UTC).
    [InlineData("2026-10-24T21:59:00Z", "2026-10-24")] // 23:59 CEST
    [InlineData("2026-10-24T22:00:00Z", "2026-10-25")] // Paris midnight, still UTC+2
    [InlineData("2026-10-25T00:30:00Z", "2026-10-25")] // 02:30 CEST (first occurrence)
    [InlineData("2026-10-25T01:30:00Z", "2026-10-25")] // 02:30 CET (second occurrence)
    [InlineData("2026-10-25T22:59:00Z", "2026-10-25")] // 23:59 CET
    [InlineData("2026-10-25T23:00:00Z", "2026-10-26")] // Paris midnight, now UTC+1
    public void DateOf_AcrossDaylightSavingChanges_ReturnsTheParisCalendarDay(string utcInstant, string expectedDay)
    {
        var instant = DateTime.Parse(utcInstant, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var expected = DateTime.SpecifyKind(DateTime.Parse(expectedDay), DateTimeKind.Utc);

        var day = ParisClock.DateOf(instant);

        day.Should().Be(expected);
        day.Kind.Should().Be(DateTimeKind.Utc, "calendar days travel as UTC midnight");
    }

    [Theory]
    [InlineData("2026-03-29T22:30:00Z", "2026-03-30")]
    [InlineData("2026-10-25T22:30:00Z", "2026-10-25")]
    public void Today_UsesTheParisDay_OnDaylightSavingNights(string utcNow, string expectedDay)
    {
        var provider = new FixedTimeProvider(DateTimeOffset.Parse(utcNow));

        ParisClock.Today(provider).Should().Be(DateTime.SpecifyKind(DateTime.Parse(expectedDay), DateTimeKind.Utc));
    }

    [Fact]
    public void DateOf_TreatsUnspecifiedKindAsUtc()
    {
        var unspecified = new DateTime(2026, 3, 29, 22, 30, 0, DateTimeKind.Unspecified);

        ParisClock.DateOf(unspecified).Should().Be(new DateTime(2026, 3, 30, 0, 0, 0, DateTimeKind.Utc));
    }
}
