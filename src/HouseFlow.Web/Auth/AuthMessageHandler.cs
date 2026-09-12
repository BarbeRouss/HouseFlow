using System.Net;
using System.Net.Http.Json;
using HouseFlow.Web.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace HouseFlow.Web.Auth;

/// <summary>
/// Attaches the bearer token, always sends cookies (needed for the HttpOnly
/// refresh-token cookie), transparently refreshes the access token once on a 401
/// (single-flight), and retries idempotent requests on transient failures
/// (network error / 5xx / 408) with exponential backoff — surfacing that via
/// <see cref="RetryState"/> so the UI can show a "reconnecting" indicator.
/// </summary>
public sealed class AuthMessageHandler : DelegatingHandler
{
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private const int MaxRetries = 3;

    private readonly TokenStore _tokens;
    private readonly NavigationManager _nav;
    private readonly AppAuthStateProvider _authState;
    private readonly RetryState _retry;
    private readonly string _apiBaseUrl;

    public AuthMessageHandler(
        TokenStore tokens,
        NavigationManager nav,
        AppAuthStateProvider authState,
        RetryState retry,
        AppConfig config)
    {
        _tokens = tokens;
        _nav = nav;
        _authState = authState;
        _retry = retry;
        _apiBaseUrl = config.ApiBaseUrl;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the body so the request can be re-issued (retries / refresh replay).
        if (request.Content is not null) await request.Content.LoadIntoBufferAsync();

        var response = await SendWithRetryAsync(request, _tokens.AccessToken, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized || IsAuthEndpoint(request.RequestUri))
            return response;

        response.Dispose();

        var newToken = await RefreshAsync(cancellationToken);
        if (newToken is null)
        {
            OnRefreshFailed();
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        return await SendWithRetryAsync(request, newToken, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpRequestMessage original, string? token, CancellationToken ct)
    {
        var idempotent = IsIdempotent(original.Method);
        var attempt = 0;
        var entered = false;
        try
        {
            while (true)
            {
                using var req = await CloneAsync(original);
                req.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
                Apply(req, token);

                HttpResponseMessage? response = null;
                var transient = false;
                try
                {
                    response = await base.SendAsync(req, ct);
                    transient = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout;
                }
                catch (HttpRequestException) when (idempotent && attempt < MaxRetries)
                {
                    transient = true;
                }

                if (response is not null && (!transient || !idempotent || attempt >= MaxRetries))
                    return response;

                response?.Dispose();

                if (!entered) { _retry.Enter(); entered = true; }
                attempt++;
                await Task.Delay(GetBackoff(attempt), ct);
            }
        }
        finally
        {
            if (entered) _retry.Exit();
        }
    }

    private static TimeSpan GetBackoff(int attempt)
    {
        var ms = Math.Min(100 * Math.Pow(2, attempt - 1), 2000);
        var jitter = ms * 0.25 * (Random.Shared.NextDouble() * 2 - 1);
        return TimeSpan.FromMilliseconds(ms + jitter);
    }

    private async Task<string?> RefreshAsync(CancellationToken ct)
    {
        await RefreshLock.WaitAsync(ct);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
            req.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var data = await resp.Content.ReadFromJsonAsync<RefreshResponse>(cancellationToken: ct);
            if (string.IsNullOrEmpty(data?.AccessToken)) return null;

            _tokens.SetAccessToken(data.AccessToken);
            return data.AccessToken;
        }
        catch
        {
            return null;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private void OnRefreshFailed()
    {
        _tokens.Clear();
        _authState.NotifyChanged();
        _nav.NavigateTo($"/{CurrentLocale()}/login", forceLoad: false);
    }

    private string CurrentLocale()
    {
        var path = new Uri(_nav.Uri).AbsolutePath.Trim('/');
        var first = path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is "fr" or "en" ? first : "fr";
    }

    private static void Apply(HttpRequestMessage request, string? token)
    {
        request.Headers.Remove("Authorization");
        if (!string.IsNullOrEmpty(token))
            request.Headers.Add("Authorization", $"Bearer {token}");
    }

    private static bool IsAuthEndpoint(Uri? uri)
    {
        var path = uri?.AbsolutePath ?? "";
        return path.Contains("/auth/login") || path.Contains("/auth/register") ||
               path.Contains("/auth/refresh");
    }

    private static bool IsIdempotent(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Put || method == HttpMethod.Delete ||
        method == HttpMethod.Head || method == HttpMethod.Options;

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

    private sealed class RefreshResponse
    {
        public string? AccessToken { get; set; }
    }
}
