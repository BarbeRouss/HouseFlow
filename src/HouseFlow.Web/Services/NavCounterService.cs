using HouseFlow.Web.Api;
using HouseFlow.Web.Auth;

namespace HouseFlow.Web.Services;

/// <summary>
/// R3 counters shared by the nav badge (C1/C2) and the pages: "à traiter" (En retard + À faire,
/// all visible houses) and "en retard". Values are <c>null</c> until received (C6: the badge
/// stays hidden, never shows 0 before the response).
/// <para>
/// Pages that already fetched the same data call <see cref="Set"/>; pages that changed it
/// ("C'est fait", delete…) call <see cref="RefreshAsync"/>.
/// </para>
/// </summary>
public sealed class NavCounterService
{
    private readonly ApiService _api;
    private readonly TokenStore _tokens;
    private readonly ILogger<NavCounterService> _logger;

    private Task? _inFlight;
    private string? _loadedForUserId;

    public NavCounterService(ApiService api, TokenStore tokens, ILogger<NavCounterService> logger)
    {
        _api = api;
        _tokens = tokens;
        _logger = logger;
    }

    /// <summary>En retard + À faire. Null = not loaded yet.</summary>
    public int? ToProcess { get; private set; }

    /// <summary>En retard. Null = not loaded yet.</summary>
    public int? Overdue { get; private set; }

    /// <summary>All maintenance types of the visible houses. Null = not loaded yet.</summary>
    public int? Total { get; private set; }

    /// <summary>
    /// Global status (header logo C1, tab bar badge C2, favicon): the most urgent of every visible
    /// house — same source as the badge. <see cref="AppStatus.None"/> until received, and when there
    /// is no maintenance at all (never « ok » before the data).
    /// </summary>
    public AppStatus Status => ToProcess is null ? AppStatus.None
        : Overdue > 0 ? AppStatus.Late
        : ToProcess > 0 ? AppStatus.Due
        : Total > 0 ? AppStatus.Ok
        : AppStatus.None;

    public event Action? OnChange;

    /// <summary>Loads the counters once per signed-in user.</summary>
    public Task EnsureLoadedAsync()
    {
        var userId = _tokens.User?.Id;
        if (userId is null) return Task.CompletedTask;
        if (_loadedForUserId == userId && ToProcess is not null) return Task.CompletedTask;
        if (_loadedForUserId != userId) Reset();
        return RefreshAsync();
    }

    /// <summary>Reloads the counters; the previous values stay displayed until the answer (C6).</summary>
    public Task RefreshAsync() =>
        _inFlight is { IsCompleted: false } ? _inFlight : _inFlight = LoadAsync();

    /// <summary>Pushes counters computed by a page that already holds the data.</summary>
    public void Set(int toProcess, int overdue, int total)
    {
        _loadedForUserId = _tokens.User?.Id;
        if (ToProcess == toProcess && Overdue == overdue && Total == total) return;
        ToProcess = toProcess;
        Overdue = overdue;
        Total = total;
        OnChange?.Invoke();
    }

    /// <summary>Pushes the counters of a dashboard response a page already fetched (P07).</summary>
    public void SetFrom(Dashboard dashboard) => Set(dashboard.ToHandleCount, dashboard.OverdueCount, dashboard.TotalCount);

    /// <summary>Forgets the counters (logout, user switch).</summary>
    public void Reset()
    {
        _loadedForUserId = null;
        if (ToProcess is null && Overdue is null && Total is null) return;
        ToProcess = null;
        Overdue = null;
        Total = null;
        OnChange?.Invoke();
    }

    private async Task LoadAsync()
    {
        try
        {
            // Single source of the R3 counters: GET /dashboard (same numbers as P07).
            SetFrom(await _api.GetDashboardAsync());
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            // Non-critical chrome: keep the previous value (or keep the badge hidden) and let the
            // page's own AsyncSection report the error to the user.
            _logger.LogWarning("Nav counters could not be loaded: {Error}", ex.Message);
        }
    }
}
