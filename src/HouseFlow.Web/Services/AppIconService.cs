using Microsoft.JSInterop;

namespace HouseFlow.Web.Services;

/// <summary>
/// Global status shown by the app icon (header logo C1, favicon) — specs/ux README « Assets ».
/// The value names are the icon file suffixes (<c>icons/icon-{late|due|ok|none}.svg</c>).
/// </summary>
public enum AppStatus
{
    /// <summary>Loading, no maintenance at all, or onboarding (P05, P06): neutral icon, never green before the data.</summary>
    None,
    /// <summary>Everything up to date. Also the fixed icon of the public pages (P01–P04, P14, P15).</summary>
    Ok,
    /// <summary>At least one maintenance to do, none overdue.</summary>
    Due,
    /// <summary>At least one overdue maintenance.</summary>
    Late,
}

public static class AppStatusExtensions
{
    /// <summary>File suffix / CSS value: "none" | "ok" | "due" | "late".</summary>
    public static string Key(this AppStatus status) => status switch
    {
        AppStatus.Ok => "ok",
        AppStatus.Due => "due",
        AppStatus.Late => "late",
        _ => "none",
    };
}

/// <summary>
/// Which app icon the page shows (header logo + <c>&lt;link rel="icon"&gt;</c>). Layouts claim a
/// scope with <see cref="Components.AppIconScope"/>: a fixed status (public layout → Ok, onboarding →
/// None) or, with no fixed status, the global status of <see cref="NavCounterService"/> (app layout).
/// The most recently opened scope wins — a nested layout (SetupLayout inside MainLayout) overrides its
/// parent — and closing it falls back to the previous one. The favicon is swapped through
/// <c>hf.setAppIcon</c> (wwwroot/js/app.js); index.html sets the first one before Blazor boots.
/// </summary>
public sealed class AppIconService : IDisposable
{
    private readonly NavCounterService _counters;
    private readonly IJSRuntime _js;
    private readonly List<Scope> _scopes = new();
    private AppStatus? _applied;

    public AppIconService(NavCounterService counters, IJSRuntime js)
    {
        _counters = counters;
        _js = js;
        _counters.OnChange += Refresh;
    }

    /// <summary>Status of the icon to show now.</summary>
    public AppStatus Current => _scopes.Count == 0 ? AppStatus.None : _scopes[^1].Fixed ?? _counters.Status;

    public event Action? OnChange;

    /// <summary>Opens a scope; dispose it when the layout goes away. <paramref name="fixedStatus"/> null = follow the global status.</summary>
    public IDisposable Open(AppStatus? fixedStatus)
    {
        var scope = new Scope(this, fixedStatus);
        _scopes.Add(scope);
        Refresh();
        return scope;
    }

    private void Close(Scope scope)
    {
        if (_scopes.Remove(scope)) Refresh();
    }

    private void Refresh()
    {
        var current = Current;
        if (current == _applied) return;
        _applied = current;
        OnChange?.Invoke();
        _ = ApplyFaviconAsync(current);
    }

    private async Task ApplyFaviconAsync(AppStatus status)
    {
        try
        {
            await _js.InvokeVoidAsync("hf.setAppIcon", status.Key());
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException)
        {
            // Cosmetic only: keep the previous favicon.
        }
    }

    public void Dispose() => _counters.OnChange -= Refresh;

    private sealed class Scope(AppIconService owner, AppStatus? fixedStatus) : IDisposable
    {
        public AppStatus? Fixed { get; } = fixedStatus;
        public void Dispose() => owner.Close(this);
    }
}
