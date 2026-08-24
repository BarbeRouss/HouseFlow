namespace HouseFlow.Web.Auth;

/// <summary>
/// Coordinates redirects between auth forms and the auth layout. A login/register
/// form sets the flag before navigating to its own destination (e.g. register →
/// /houses/{id}/devices/new); the auth layout consumes it and skips its own
/// redirect to /dashboard. Mirrors the former redirect-guard.ts.
/// </summary>
public sealed class RedirectGuard
{
    private bool _redirecting;

    public void SetFormRedirecting() => _redirecting = true;

    public bool Consume()
    {
        if (!_redirecting) return false;
        _redirecting = false;
        return true;
    }
}
