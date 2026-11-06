using System.Net;
using HouseFlow.Web.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace HouseFlow.Web.Auth;

/// <summary>
/// Attaches the bearer token, always sends cookies (needed for the HttpOnly
/// refresh-token cookie), transparently refreshes the access token once on a 401
/// (single-flight, <see cref="SessionRefresher"/>), and retries idempotent requests on transient
/// failures (network error / 5xx / 408) with exponential backoff — surfacing that via
/// <see cref="RetryState"/> so the UI can show a "reconnecting" indicator.
/// The local session is cleared only when the API refuses the refresh (401); a refresh that could
/// not get an answer (429, 5xx, network) keeps it and the request fails as a transient 503.
/// </summary>
public sealed class AuthMessageHandler : DelegatingHandler
{
    private const int MaxRetries = 3;

    private readonly TokenStore _tokens;
    private readonly NavigationManager _nav;
    private readonly AppAuthStateProvider _authState;
    private readonly RetryState _retry;
    private readonly RedirectGuard _redirect;
    private readonly SessionRefresher _refresher;

    public AuthMessageHandler(
        TokenStore tokens,
        NavigationManager nav,
        AppAuthStateProvider authState,
        RetryState retry,
        RedirectGuard redirect,
        SessionRefresher refresher)
    {
        _tokens = tokens;
        _nav = nav;
        _authState = authState;
        _retry = retry;
        _redirect = redirect;
        _refresher = refresher;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the body so the request can be re-issued (retries / refresh replay).
        if (request.Content is not null) await request.Content.LoadIntoBufferAsync();

        var sentWith = _tokens.AccessToken;
        var response = await SendWithRetryAsync(request, sentWith, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized || IsAuthEndpoint(request.RequestUri))
            return response;

        response.Dispose();

        // Another request refreshed the token while this one was in flight: replay with it.
        if (_tokens.AccessToken is { Length: > 0 } current && current != sentWith)
            return await SendWithRetryAsync(request, current, cancellationToken);

        var outcome = await _refresher.RefreshAsync();
        switch (outcome.Status)
        {
            case RefreshStatus.Refreshed:
                return await SendWithRetryAsync(request, _tokens.AccessToken, cancellationToken);
            case RefreshStatus.Rejected:
                await OnRefreshRejectedAsync(outcome.ErrorCode);
                return new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = request };
            default:
                // Nothing is known about the session: keep it, and let the page show its usual
                // "retry" state for a transient failure instead of a logout.
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request };
        }
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

    /// <summary>The API refused the refresh cookie (401): the session is over.</summary>
    private async Task OnRefreshRejectedAsync(string? errorCode)
    {
        if (errorCode == ApiErrorCodes.AccountRestricted)
        {
            // Art. 18: the session is over and the user must learn why — P02 with the
            // "compte suspendu" message, no returnUrl (logging in again is refused too).
            await _tokens.ClearAsync();
            _redirect.SetSigningOut();
            _authState.NotifyChanged();
            _nav.NavigateTo(AppRoutes.RestrictedLoginUrl(AppRoutes.CurrentLocale(_nav)), replace: true);
            _redirect.ConsumeSigningOut();
            return;
        }

        // R6: back to the page the user was on after logging in again. Computed before the
        // auth-state change, since the protected layouts react to it by navigating themselves.
        var loginUrl = AppRoutes.LoginUrl(_nav);
        await _tokens.ClearAsync();
        _authState.NotifyChanged();
        if (AppRoutes.SafeReturnUrl("/" + _nav.ToBaseRelativePath(_nav.Uri)) is not null)
            _nav.NavigateTo(loginUrl, replace: true);
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
}
