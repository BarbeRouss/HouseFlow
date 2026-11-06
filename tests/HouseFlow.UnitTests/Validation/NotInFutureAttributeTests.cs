using FluentAssertions;
using HouseFlow.Application.Common;

namespace HouseFlow.UnitTests.Validation;

public class NotInFutureAttributeTests
{
    private readonly NotInFutureAttribute _attribute = new();

    [Fact]
    public void IsValid_WithPastDate_ReturnsTrue()
    {
        _attribute.IsValid(ParisClock.Today().AddDays(-1)).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithTodayInParis_ReturnsTrue()
    {
        _attribute.IsValid(ParisClock.Today()).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithFutureDate_ReturnsFalse()
    {
        _attribute.IsValid(ParisClock.Today().AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void IsValid_WithNull_ReturnsTrue()
    {
        _attribute.IsValid(null).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithFarFutureDate_ReturnsFalse()
    {
        _attribute.IsValid(DateTime.UtcNow.AddYears(1)).Should().BeFalse();
    }

    [Fact]
    public void IsValid_WithFarPastDate_ReturnsTrue()
    {
        _attribute.IsValid(DateTime.UtcNow.AddYears(-10)).Should().BeTrue();
    }

    [Fact]
    public void IsInFuture_ParisMidnightAlreadyPassed_TodaysDateIsNotFuture()
    {
        // 2026-03-10 23:30 UTC = 2026-03-11 00:30 in Paris: "today" is already the 11th,
        // so a record dated 2026-03-11T00:00:00Z (how the frontend sends dates) is valid.
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 3, 10, 23, 30, 0, TimeSpan.Zero));
        var parisToday = new DateTime(2026, 3, 11, 0, 0, 0, DateTimeKind.Utc);

        NotInFutureAttribute.IsInFuture(parisToday, clock).Should().BeFalse();
        NotInFutureAttribute.IsInFuture(parisToday.AddDays(1), clock).Should().BeTrue();
    }
}

/// <summary>Fixed clock for date-rule tests.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
