using System.Net;
using System.Net.Http.Json;
using HouseFlow.Frontend.Wasm.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace HouseFlow.Frontend.Wasm.Services;

/// <summary>
/// Replicates the axios interceptor of the Next.js client:
///  - attaches the access token as a Bearer header,
///  - sends cookies (HttpOnly refresh token) on every request,
///  - on 401 (non-auth endpoint), refreshes the token once and retries.
/// </summary>
public sealed class BearerHandler(
    TokenStore tokenStore,
    AppConfig config,
    NavigationManager navigation) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Always send/receive cookies (needed for the refresh-token cookie).
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

        var isAuthEndpoint = IsAuthEndpoint(request.RequestUri);

        if (!isAuthEndpoint)
            await AttachTokenAsync(request);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized || isAuthEndpoint)
            return response;

        // 401 → try a single refresh, then replay the original request.
        var newToken = await TryRefreshAsync(cancellationToken);
        if (newToken is null)
        {
            navigation.NavigateTo("/login", forceLoad: false);
            return response;
        }

        response.Dispose();
        var retry = await CloneAsync(request);
        retry.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        retry.Headers.Authorization = new("Bearer", newToken);
        return await base.SendAsync(retry, cancellationToken);
    }

    private async Task AttachTokenAsync(HttpRequestMessage request)
    {
        var token = await tokenStore.GetTokenAsync();
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new("Bearer", token);
    }

    private async Task<string?> TryRefreshAsync(CancellationToken ct)
    {
        var refresh = new HttpRequestMessage(HttpMethod.Post, $"{config.ApiBaseUrl}/auth/refresh");
        refresh.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        var result = await base.SendAsync(refresh, ct);
        if (!result.IsSuccessStatusCode)
        {
            await tokenStore.SetTokenAsync(null);
            return null;
        }

        var auth = await result.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: ct);
        if (string.IsNullOrEmpty(auth?.AccessToken))
        {
            await tokenStore.SetTokenAsync(null);
            return null;
        }

        await tokenStore.SetTokenAsync(auth.AccessToken);
        return auth.AccessToken;
    }

    private bool IsAuthEndpoint(Uri? uri)
        => uri is not null &&
           (uri.AbsolutePath.EndsWith("/auth/login", StringComparison.Ordinal) ||
            uri.AbsolutePath.EndsWith("/auth/register", StringComparison.Ordinal) ||
            uri.AbsolutePath.EndsWith("/auth/refresh", StringComparison.Ordinal));

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }
}
