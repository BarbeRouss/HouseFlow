using System.Text.Json;
using Microsoft.JSInterop;

namespace HouseFlow.Web.Auth;

/// <summary>
/// Holds the access token (localStorage: houseflow_access_token) and user
/// (sessionStorage: houseflow_auth_user), mirroring the former React auth
/// context. An in-memory cache lets the HTTP handler read the token
/// synchronously; storage is the source of truth across full page reloads.
/// </summary>
public sealed class TokenStore
{
    private const string AccessTokenKey = "houseflow_access_token";
    private const string AuthUserKey = "houseflow_auth_user";

    private readonly IJSRuntime _js;

    public TokenStore(IJSRuntime js) => _js = js;

    public string? AccessToken { get; private set; }
    public AuthUser? User { get; private set; }

    /// <summary>Load token + user from browser storage into the in-memory cache.</summary>
    public async Task InitializeAsync()
    {
        AccessToken = await _js.InvokeAsync<string?>("hf.localGet", AccessTokenKey);
        var userJson = await _js.InvokeAsync<string?>("hf.sessionGet", AuthUserKey);
        User = ParseUser(userJson);
    }

    public async Task SetSessionAsync(string token, AuthUser user)
    {
        AccessToken = token;
        User = user;
        await _js.InvokeVoidAsync("hf.localSet", AccessTokenKey, token);
        await _js.InvokeVoidAsync("hf.sessionSet", AuthUserKey, JsonSerializer.Serialize(user));
    }

    public async Task SetAccessTokenAsync(string? token)
    {
        AccessToken = token;
        await _js.InvokeVoidAsync("hf.localSet", AccessTokenKey, token);
    }

    public async Task ClearAsync()
    {
        AccessToken = null;
        User = null;
        await _js.InvokeVoidAsync("hf.localRemove", AccessTokenKey);
        await _js.InvokeVoidAsync("hf.sessionRemove", AuthUserKey);
    }

    private static AuthUser? ParseUser(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<AuthUser>(json); }
        catch { return null; }
    }
}
