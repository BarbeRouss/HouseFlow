using FluentAssertions;
using HouseFlow.Web.Rules;

namespace HouseFlow.UnitTests.Web;

/// <summary>R2 first due date and the P06 preview wording (spec example: today = 26 Sept 2026).</summary>
public class InitialDueRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 26);

    private static string Fr(string key, object? args)
    {
        var count = args?.GetType().GetProperty("count")?.GetValue(args);
        return key switch
        {
            "dates.today" => "aujourd'hui",
            "dates.overdueBy" => $"en retard de {count} j",
            _ => key,
        };
    }

    [Fact]
    public void Month_IsFirstOfMonthPlusPeriod()
    {
        // Chaudière, dernier = mars 2026, 12 mois → 1er mars 2027.
        InitialDueRules.FirstDue(InitialDueRules.KindMonth, 2026, 3, 12, Today).Should().Be(new DateOnly(2027, 3, 1));
    }

    [Fact]
    public void Unknown_IsCreationPlus30Days()
    {
        InitialDueRules.FirstDue(InitialDueRules.KindUnknown, null, null, 12, Today).Should().Be(new DateOnly(2026, 10, 26));
    }

    [Fact]
    public void Older_IsCreationDay()
    {
        InitialDueRules.FirstDue(InitialDueRules.KindOlder, null, null, 24, Today).Should().Be(Today);
    }

    [Fact]
    public void IncompleteMonth_FallsBackToUnknown()
    {
        InitialDueRules.FirstDue(InitialDueRules.KindMonth, 2026, null, 12, Today).Should().Be(new DateOnly(2026, 10, 26));
    }

    [Fact]
    public void PreviewLabel_UsesAbsoluteWithin60Days_MonthYearBeyond_RelativeWhenOverdue()
    {
        InitialDueRules.PreviewLabel(new DateOnly(2026, 10, 26), Today, "fr", Fr).Should().Be("26 oct. 2026");
        InitialDueRules.PreviewLabel(new DateOnly(2027, 3, 1), Today, "fr", Fr).Should().Be("mars 2027");
        InitialDueRules.PreviewLabel(Today, Today, "fr", Fr).Should().Be("aujourd'hui");
        InitialDueRules.PreviewLabel(new DateOnly(2026, 9, 1), Today, "fr", Fr).Should().Be("en retard de 25 j");
    }
}
