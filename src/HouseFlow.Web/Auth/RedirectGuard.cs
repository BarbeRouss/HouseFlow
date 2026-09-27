namespace HouseFlow.Web.Auth;

/// <summary>
/// Coordinates redirects between auth forms / logout and the layouts.
/// <list type="bullet">
/// <item>A login/register form sets <see cref="SetFormRedirecting"/> before navigating to its own
/// destination; the auth layout consumes it and skips its own redirect to /dashboard.</item>
/// <item>An explicit logout sets <see cref="SetSigningOut"/>: the protected layouts then skip their
/// "no session → /login?returnUrl=" redirect, since the logout navigates to the landing page (P01).</item>
/// </list>
/// </summary>
public sealed class RedirectGuard
{
    private bool _redirecting;
    private bool _signingOut;

    public void SetFormRedirecting() => _redirecting = true;

    public bool Consume()
    {
        if (!_redirecting) return false;
        _redirecting = false;
        return true;
    }

    public void SetSigningOut() => _signingOut = true;

    public bool ConsumeSigningOut()
    {
        if (!_signingOut) return false;
        _signingOut = false;
        return true;
    }
}
