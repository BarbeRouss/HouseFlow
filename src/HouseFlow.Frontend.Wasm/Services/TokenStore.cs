using Microsoft.JSInterop;

namespace HouseFlow.Frontend.Wasm.Services;

/// <summary>
/// Access token storage: in-memory + localStorage (mirrors the Next.js client behavior).
/// Refresh tokens live in an HttpOnly cookie managed by the API, never accessible here.
/// </summary>
public sealed class TokenStore(IJSRuntime js)
{
    private const string Key = "houseflow_access_token";
    private string? _memoryToken;

    public async ValueTask<string?> GetTokenAsync()
    {
        if (_memoryToken is not null)
            return _memoryToken;

        var stored = await js.InvokeAsync<string?>("localStorage.getItem", Key);
        _memoryToken = stored;
        return stored;
    }

    public async ValueTask SetTokenAsync(string? token)
    {
        _memoryToken = token;
        if (token is null)
            await js.InvokeVoidAsync("localStorage.removeItem", Key);
        else
            await js.InvokeVoidAsync("localStorage.setItem", Key, token);
    }
}
