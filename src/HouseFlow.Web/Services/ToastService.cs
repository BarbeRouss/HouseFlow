namespace HouseFlow.Web.Services;

/// <summary>A text action shown on the right of a toast ("Ajouter des détails", "Annuler", "Réessayer").</summary>
public sealed record ToastAction(string Label, Func<Task> OnClick, string? TestId = null);

/// <summary>One toast. <see cref="TestId"/> lets a page keep a stable E2E hook (e.g. <c>export-success</c>).</summary>
public sealed record ToastMessage(string Text, IReadOnlyList<ToastAction> Actions, bool IsError = false, string? TestId = null)
{
    public Guid Id { get; } = Guid.NewGuid();
}

/// <summary>
/// C5 toast: a single toast at a time (a new one replaces the previous), auto-dismissed after
/// 6 s, held while hovered or focused (<see cref="SetHold"/>, driven by <c>ToastHost</c>).
/// Scoped = one per browser tab in WebAssembly, so a toast survives a navigation
/// (e.g. "{n} entretiens créés" shown by P06 right before going to P07).
/// </summary>
public sealed class ToastService : IDisposable
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(6);

    /// <summary>After a hold ends, the toast stays at least this long.</summary>
    private static readonly TimeSpan MinRemainingAfterHold = TimeSpan.FromSeconds(2);

    private CancellationTokenSource? _timer;
    private TimeSpan _remaining;
    private DateTimeOffset _deadline;
    private bool _held;

    public ToastMessage? Current { get; private set; }

    public event Action? OnChange;

    public void Show(string text, params ToastAction[] actions) => Show(new ToastMessage(text, actions));

    public void ShowError(string text, params ToastAction[] actions) => Show(new ToastMessage(text, actions, IsError: true));

    public void Show(ToastMessage message)
    {
        Current = message;
        _remaining = Duration;
        if (!_held) StartTimer();
        OnChange?.Invoke();
    }

    /// <summary>Hides the current toast (or only the given one, if it is still displayed).</summary>
    public void Dismiss(Guid? id = null)
    {
        if (Current is null || (id is not null && Current.Id != id)) return;
        StopTimer();
        Current = null;
        _held = false;
        OnChange?.Invoke();
    }

    /// <summary>Runs a toast action: the toast is dismissed first, then the callback runs.</summary>
    public async Task InvokeAsync(ToastAction action)
    {
        Dismiss();
        await action.OnClick();
    }

    /// <summary>Hover / focus inside the toast holds it on screen; releasing restarts the countdown.</summary>
    public void SetHold(bool held)
    {
        if (Current is null) { _held = false; return; }
        if (held == _held) return;
        _held = held;
        if (held)
        {
            _remaining = _deadline - DateTimeOffset.UtcNow;
            StopTimer();
        }
        else
        {
            if (_remaining < MinRemainingAfterHold) _remaining = MinRemainingAfterHold;
            StartTimer();
        }
    }

    private void StartTimer()
    {
        StopTimer();
        var cts = new CancellationTokenSource();
        _timer = cts;
        _deadline = DateTimeOffset.UtcNow + _remaining;
        _ = ExpireAsync(Current!.Id, _remaining, cts.Token);
    }

    private void StopTimer()
    {
        _timer?.Cancel();
        _timer?.Dispose();
        _timer = null;
    }

    private async Task ExpireAsync(Guid id, TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
        }
        catch (TaskCanceledException)
        {
            return; // Replaced, dismissed or held: nothing to expire.
        }
        if (Current?.Id == id && !_held) Dismiss(id);
    }

    public void Dispose() => StopTimer();
}
