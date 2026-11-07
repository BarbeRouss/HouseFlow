using FluentAssertions;
using HouseFlow.Web.Components;
using HouseFlow.Web.Rules;

namespace HouseFlow.UnitTests.Web;

/// <summary>Front-end R1 (status) and R4 (dates) rules, linked from src/HouseFlow.Web/Rules.</summary>
public class FrontendRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 26);

    // Minimal translator mirroring the fr catalog keys used by DateFormatter.Relative.
    private static string Fr(string key, object? args)
    {
        var count = args?.GetType().GetProperty("count")?.GetValue(args);
        return key switch
        {
            "dates.today" => "aujourd'hui",
            "dates.overdueBy" => $"en retard de {count} j",
            "dates.inDays" => $"dans {count} j",
            _ => key,
        };
    }

    [Theory]
    [InlineData("Climatisation", "snowflake", "air")]
    [InlineData("Alarme", "siren", "safe")]
    [InlineData("Pompe hydrophore", "droplets", "water")]
    public void DeviceVisuals_GivesTheAirConditionerAlarmAndPressurePumpTheirOwnIcon(string value, string icon, string tint)
    {
        DeviceVisuals.Icon(value).Should().Be(icon);
        DeviceVisuals.Tint(value).Should().Be(tint);
    }

    [Theory]
    [InlineData(-1, 12, DueStatus.Overdue)]
    [InlineData(0, 12, DueStatus.Due)]
    [InlineData(37, 12, DueStatus.Due)]   // 10 % d'un an = 37 j
    [InlineData(38, 12, DueStatus.Ok)]
    [InlineData(10, 3, DueStatus.Due)]    // 10 % d'un trimestre (92 j) = 10 j
    [InlineData(11, 3, DueStatus.Ok)]
    [InlineData(3, 1, DueStatus.Due)]     // 10 % d'un mois (30 j) = 3 j
    [InlineData(4, 1, DueStatus.Ok)]
    public void Compute_AppliesTenPercentOfPeriodWindow(int offsetDays, int months, DueStatus expected)
    {
        StatusRules.Compute(Today.AddDays(offsetDays), Today, months).Should().Be(expected);
    }

    [Theory]
    [InlineData("overdue", DueStatus.Overdue)]
    [InlineData("pending", DueStatus.Due)]
    [InlineData("up_to_date", DueStatus.Ok)]
    public void FromApi_MapsApiStatuses(string api, DueStatus expected)
    {
        StatusRules.FromApi(api).Should().Be(expected);
    }

    [Fact]
    public void FromApi_UnknownStatus_ReturnsNull()
    {
        StatusRules.FromApi("whatever").Should().BeNull();
    }

    [Fact]
    public void MostUrgent_ReturnsWorstStatus()
    {
        StatusRules.MostUrgent(new[] { DueStatus.Ok, DueStatus.Overdue, DueStatus.Due }).Should().Be(DueStatus.Overdue);
        StatusRules.MostUrgent(Array.Empty<DueStatus>()).Should().BeNull();
    }

    [Theory]
    [InlineData(0, "aujourd'hui")]
    [InlineData(-24, "en retard de 24 j")]
    [InlineData(12, "dans 12 j")]
    [InlineData(60, "dans 60 j")]
    [InlineData(-60, "en retard de 60 j")]
    public void Relative_WithinSixtyDays_IsRelative(int offsetDays, string expected)
    {
        DateFormatter.Relative(Today.AddDays(offsetDays), Today, "fr", Fr).Should().Be(expected);
    }

    [Fact]
    public void Relative_BeyondSixtyDays_IsMonthAndYear()
    {
        DateFormatter.Relative(new DateOnly(2027, 3, 10), Today, "fr", Fr).Should().Be("mars 2027");
        DateFormatter.Relative(new DateOnly(2027, 3, 10), Today, "en", Fr).Should().Be("March 2027");
        // Full month name, never the abbreviation (« octobre 2026 », not « oct. 2026 »).
        DateFormatter.MonthYear(new DateOnly(2026, 10, 1), "fr").Should().Be("octobre 2026");
    }

    [Theory]
    [InlineData("octobre 2026", "fr", "Octobre 2026")]
    [InlineData("en retard de 8 j", "fr", "En retard de 8 j")]
    [InlineData("in 12 days", "en", "In 12 days")]
    [InlineData("", "fr", "")]
    public void Capitalize_UppercasesTheFirstLetterOnly(string text, string locale, string expected)
    {
        DateFormatter.Capitalize(text, locale).Should().Be(expected);
    }

    [Fact]
    public void Relative_OverdueBeyondSixtyDays_StaysRelative()
    {
        DateFormatter.Relative(Today.AddDays(-90), Today, "fr", Fr).Should().Be("en retard de 90 j");
    }

    [Fact]
    public void Absolute_IsLocalized()
    {
        var date = new DateOnly(2026, 10, 8);
        DateFormatter.Absolute(date, "fr").Should().Be("8 oct. 2026");
        DateFormatter.Absolute(date, "en").Should().Be("Oct 8, 2026");
    }

    [Theory]
    [InlineData("2026-10-08", 2026, 10, 8)]
    [InlineData("2026-10-08T00:00:00Z", 2026, 10, 8)]
    [InlineData("2026-10-07T22:30:00Z", 2026, 10, 8)] // 00:30 in Paris (UTC+2)
    public void ParseDate_ReturnsTheParisCalendarDate(string value, int y, int m, int d)
    {
        DateFormatter.ParseDate(value).Should().Be(new DateOnly(y, m, d));
    }

    [Fact]
    public void ParseDate_InvalidOrEmpty_ReturnsNull()
    {
        DateFormatter.ParseDate(null).Should().BeNull();
        DateFormatter.ParseDate("not a date").Should().BeNull();
    }

    [Fact]
    public void ParisToday_UsesEuropeParis()
    {
        // 23:30 UTC on 30 Sept = 01:30 on 1 Oct in Paris.
        ParisClock.ToParisDate(new DateTimeOffset(2026, 9, 30, 23, 30, 0, TimeSpan.Zero))
            .Should().Be(new DateOnly(2026, 10, 1));
    }

    [Fact]
    public void Euros_AreLocalized()
    {
        MoneyFormatter.Euros(250m, "fr").Should().Be("250 €");
        MoneyFormatter.Euros(250.5m, "en").Should().Be("250.5 €");
    }

    [Theory]
    [InlineData("Annual", null, "maintenance.everyYears", 1)]
    [InlineData("Biennial", null, "maintenance.everyYears", 2)]
    [InlineData("Semestrial", null, "maintenance.everyMonths", 6)]
    [InlineData("Quarterly", null, "maintenance.everyMonths", 3)]
    [InlineData("Custom", 36, "maintenance.everyYears", 3)]
    [InlineData("Custom", 18, "maintenance.everyMonths", 18)]
    public void Periodicity_InWords(string periodicity, int? customMonths, string key, int count)
    {
        PeriodicityRules.Words(periodicity, customMonths, null).Should().Be((key, count));
    }

    [Fact]
    public void Periodicity_NextDue_MirrorsTheApi()
    {
        var done = new DateOnly(2026, 3, 1);
        PeriodicityRules.NextDue(done, "Annual", null, null).Should().Be(new DateOnly(2027, 3, 1));
        PeriodicityRules.NextDue(done, "Semestrial", null, null).Should().Be(new DateOnly(2026, 9, 1));
        PeriodicityRules.NextDue(done, "Custom", 18, null).Should().Be(new DateOnly(2027, 9, 1));
        PeriodicityRules.NextDue(done, "Custom", null, 90).Should().Be(done.AddDays(90));
    }
}
