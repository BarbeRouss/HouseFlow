namespace HouseFlow.Web.Api;

/// <summary>
/// Tracks whether any transient request is currently being retried, so the UI
/// can show a "reconnecting" indicator (mirrors the former client's retry state).
/// </summary>
public sealed class RetryState
{
    private int _active;

    public bool IsRetrying => _active > 0;

    public event Action? OnChange;

    public void Enter()
    {
        _active++;
        OnChange?.Invoke();
    }

    public void Exit()
    {
        if (_active > 0) _active--;
        OnChange?.Invoke();
    }
}
