using Microsoft.JSInterop;

namespace HouseFlow.Web.Auth;

/// <summary>
/// Holds the access token and the authenticated user in memory only: nothing sensitive
/// is written to browser storage, so an XSS cannot read a credential from disk. The
/// session survives page reloads and browser restarts through the HttpOnly refresh
/// token cookie, which <c>App.razor</c> exchanges for a fresh access token at boot.
/// <para>
/// The only thing persisted is a <b>session hint</b> (localStorage
/// <c>houseflow_session</c> = "1"): it tells the next boot that a refresh is worth
/// attempting, so a logged-out visitor gets the login page immediately instead of
/// waiting on the API (a cold start can take 30 s). The hint carries no secret: a stale
/// hint just costs one 401, a missing one costs one login.
/// </para>
/// Registered as a singleton so components and the HTTP handler share one instance.
/// </summary>
public sealed class TokenStore
{
    private const string SessionHintKey = "houseflow_session";

    private readonly IJSRuntime _js;

    public TokenStore(IJSRuntime js) => _js = js;

    public string? AccessToken { get; private set; }
    public AuthUser? User { get; private set; }

    /// <summary>Whether a previous visit left a session behind (refresh cookie probably present).</summary>
    public async Task<bool> HasSessionHintAsync()
    {
        try { return await _js.InvokeAsync<string?>("localStorage.getItem", SessionHintKey) is not null; }
        catch { return false; }
    }

    public async Task SetSessionAsync(string token, AuthUser user)
    {
        AccessToken = token;
        User = user;
        try { await _js.InvokeVoidAsync("localStorage.setItem", SessionHintKey, "1"); } catch { /* storage unavailable */ }
    }

    public void SetAccessToken(string? token) => AccessToken = token;

    /// <summary>Forget the session (logout, or the server said the cookie is no longer valid).</summary>
    public async Task ClearAsync()
    {
        AccessToken = null;
        User = null;
        try { await _js.InvokeVoidAsync("localStorage.removeItem", SessionHintKey); } catch { /* storage unavailable */ }
    }
}
