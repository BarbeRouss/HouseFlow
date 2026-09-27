using Microsoft.AspNetCore.Components;
using HouseFlow.Web.Localization;
using HouseFlow.Web.Rules;

namespace HouseFlow.Web.Components;

/// <summary>
/// Base component that re-renders when the active locale changes and exposes a
/// terse <c>T(...)</c> translation helper plus the R4 date / amount formatters.
/// Components that display localized text should inherit this.
/// </summary>
public abstract class AppComponentBase : ComponentBase, IDisposable
{
    [Inject] protected LocalizationState Loc { get; set; } = default!;

    protected string Locale => Loc.Locale;

    protected string T(string key, object? args = null) => Loc.T(key, args);

    // ---- R4 formatting (localized, never InvariantCulture) ----

    /// <summary>Absolute date "8 oct. 2026" / "Oct 8, 2026". Unparseable input is returned as-is.</summary>
    protected string FormatDate(string? iso) =>
        DateFormatter.ParseDate(iso) is { } d ? DateFormatter.Absolute(d, Loc.Locale) : iso ?? "";

    protected string FormatDate(DateOnly date) => DateFormatter.Absolute(date, Loc.Locale);

    /// <summary>Month + year "mars 2027" / "Mar 2027".</summary>
    protected string FormatMonthYear(string? iso) =>
        DateFormatter.ParseDate(iso) is { } d ? DateFormatter.MonthYear(d, Loc.Locale) : iso ?? "";

    /// <summary>R4 relative label against today (Europe/Paris): "en retard de 24 j", "aujourd'hui", "dans 12 j", "mars 2027".</summary>
    protected string RelativeDate(DateOnly due) =>
        DateFormatter.Relative(due, ParisClock.Today, Loc.Locale, (k, a) => Loc.T(k, a));

    protected string RelativeDate(string? iso) =>
        DateFormatter.ParseDate(iso) is { } d ? RelativeDate(d) : iso ?? "";

    /// <summary>"250 €", "1 250,5 €".</summary>
    protected string FormatMoney(decimal amount) => MoneyFormatter.Euros(amount, Loc.Locale);

    private bool _subscribed;

    protected override void OnInitialized()
    {
        if (_subscribed) return;
        Loc.OnChange += OnLocaleChanged;
        _subscribed = true;
    }

    private void OnLocaleChanged() => InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        if (_subscribed) Loc.OnChange -= OnLocaleChanged;
    }
}
