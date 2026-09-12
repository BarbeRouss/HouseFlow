namespace HouseFlow.Web.Auth;

/// <summary>
/// Holds the access token and the authenticated user in memory only: nothing is
/// written to browser storage, so an XSS cannot read a credential from disk. The
/// session survives page reloads and browser restarts through the HttpOnly refresh
/// token cookie, which <c>App.razor</c> exchanges for a fresh access token at boot.
/// Registered as a singleton so components and the HTTP handler share one instance.
/// </summary>
public sealed class TokenStore
{
    public string? AccessToken { get; private set; }
    public AuthUser? User { get; private set; }

    public void SetSession(string token, AuthUser user)
    {
        AccessToken = token;
        User = user;
    }

    public void SetAccessToken(string? token) => AccessToken = token;

    public void Clear()
    {
        AccessToken = null;
        User = null;
    }
}
