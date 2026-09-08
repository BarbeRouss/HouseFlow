using Microsoft.AspNetCore.Components;
using HouseFlow.Web.Localization;

namespace HouseFlow.Web.Components;

/// <summary>
/// Base component that re-renders when the active locale changes and exposes a
/// terse <c>T(...)</c> translation helper. Components that display localized
/// text should inherit this.
/// </summary>
public abstract class AppComponentBase : ComponentBase, IDisposable
{
    [Inject] protected LocalizationState Loc { get; set; } = default!;

    protected string Locale => Loc.Locale;

    protected string T(string key, object? args = null) => Loc.T(key, args);

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
