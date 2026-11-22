using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HouseFlow.Web.Api;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace HouseFlow.Web.Auth;

/// <summary>Result of <see cref="SessionRefresher.RefreshAsync"/>.</summary>
public enum RefreshStatus
{
    /// <summary>New access token (and user) stored in <see cref="TokenStore"/>.</summary>
    Refreshed,

    /// <summary>
    /// The API answered 401: the refresh cookie is missing, expired or revoked, or the account is
    /// restricted (<see cref="RefreshOutcome.ErrorCode"/>). The session is over — and only then.
    /// </summary>
    Rejected,

    /// <summary>
    /// The API could not answer in time (network error, CORS-less or throttled 429, 5xx, timeout):
    /// nothing is known about the session, so it is kept — never cleared on a transient failure.
    /// </summary>
    Unavailable,
}

public sealed record RefreshOutcome(RefreshStatus Status, string? ErrorCode = null);

/// <summary>
/// Exchanges the HttpOnly refresh-token cookie for a new access token — the one place that does it,
/// for the boot restore (<c>App.razor</c>) and the 401 replay (<see cref="AuthMessageHandler"/>).
/// <list type="bullet">
/// <item><b>Single flight</b>: concurrent callers share the refresh in progress, so a burst of 401s
/// rotates the refresh token once (a second rotation of the same cookie would look like a reuse).</item>
/// <item><b>Only a 401 ends the session.</b> 429 (waits <c>Retry-After</c>), 5xx, 408, a network
/// error or an unreadable answer are retried with exponential backoff within <see cref="Budget"/>,
/// then reported as <see cref="RefreshStatus.Unavailable"/>, which callers treat as "keep the session".</item>
/// </list>
/// Singleton: the handler is resolved in the HTTP client factory's own scope.
/// </summary>
public sealed class SessionRefresher
{
    /// <summary>
    /// Time allowed for one refresh, retries included. The API of an ephemeral environment scales to
    /// zero and needs ~30 s to cold-start; a throttled refresh asks to wait up to the limiter window.
    /// </summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(45);

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(8);

    private readonly TokenStore _tokens;
    private readonly RetryState _retry;
    private readonly string _apiBaseUrl;
    private Task<RefreshOutcome>? _inflight;

    public SessionRefresher(TokenStore tokens, RetryState retry, AppConfig config)
    {
        _tokens = tokens;
        _retry = retry;
        _apiBaseUrl = config.ApiBaseUrl;
    }

    /// <summary>
    /// Refreshes the session, or joins the refresh already in progress. Never throws for an HTTP or
    /// network failure: see <see cref="RefreshStatus"/>.
    /// </summary>
    public Task<RefreshOutcome> RefreshAsync()
    {
        // Blazor WebAssembly is single-threaded: no lock needed around the shared task.
        if (_inflight is { IsCompleted: false } running) return running;
        return _inflight = RunAsync();
    }

    private async Task<RefreshOutcome> RunAsync()
    {
        var deadline = DateTime.UtcNow + Budget;
        using var client = new HttpClient { BaseAddress = new Uri(_apiBaseUrl), Timeout = Timeout.InfiniteTimeSpan };
        var waiting = false;
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) return new RefreshOutcome(RefreshStatus.Unavailable);

                TimeSpan wait;
                try
                {
                    using var cts = new CancellationTokenSource(remaining);
                    using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
                    req.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
                    using var resp = await client.SendAsync(req, cts.Token);

                    if (resp.IsSuccessStatusCode)
                    {
                        var data = await resp.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: cts.Token);
                        if (!string.IsNullOrEmpty(data?.AccessToken))
                        {
                            await StoreAsync(data);
                            return new RefreshOutcome(RefreshStatus.Refreshed);
                        }
                        wait = Backoff(attempt); // 200 without a token: garbled answer, try again.
                    }
                    else if (resp.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        return new RefreshOutcome(RefreshStatus.Rejected, await ReadErrorCodeAsync(resp, cts.Token));
                    }
                    else if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        // Never sooner than asked, never in a tight loop (Retry-After: 0).
                        var asked = await RetryAfterAsync(resp, cts.Token) ?? TimeSpan.Zero;
                        var backoff = Backoff(attempt);
                        wait = asked > backoff ? asked : backoff;
                    }
                    else if ((int)resp.StatusCode >= 500 || resp.StatusCode == HttpStatusCode.RequestTimeout)
                    {
                        wait = Backoff(attempt);
                    }
                    else
                    {
                        // Any other status says nothing about the cookie (it is not a 401): keep the
                        // session, but retrying the same request would get the same answer.
                        return new RefreshOutcome(RefreshStatus.Unavailable);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
                {
                    // API unreachable — which is also how the browser reports a 429 or 5xx sent
                    // without CORS headers — timed out, or answered something unreadable.
                    wait = Backoff(attempt);
                }

                // Waiting past the budget is pointless: report it now rather than keep the caller hanging.
                if (wait >= deadline - DateTime.UtcNow) return new RefreshOutcome(RefreshStatus.Unavailable);
                if (!waiting) { _retry.Enter(); waiting = true; }
                await Task.Delay(wait);
            }
        }
        finally
        {
            if (waiting) _retry.Exit();
        }
    }

    private async Task StoreAsync(AuthResponse data)
    {
        // The refresh answer carries the user (as login does); fall back to the one already held.
        if (!string.IsNullOrEmpty(data.User?.Id))
            await _tokens.SetSessionAsync(data.AccessToken, AuthUser.FromDto(data.User));
        else if (_tokens.User is { } user)
            await _tokens.SetSessionAsync(data.AccessToken, user);
        else
            _tokens.SetAccessToken(data.AccessToken);
    }

    private static TimeSpan Backoff(int attempt)
    {
        var ms = Math.Min(500 * Math.Pow(2, attempt - 1), MaxBackoff.TotalMilliseconds);
        var jitter = ms * 0.25 * (Random.Shared.NextDouble() * 2 - 1);
        return TimeSpan.FromMilliseconds(ms + jitter);
    }

    /// <summary>
    /// Delay requested by a 429: the <c>Retry-After</c> header when the browser exposes it (cross
    /// origin it must be listed in <c>Access-Control-Expose-Headers</c>), else a <c>retryAfter</c>
    /// number of seconds in the JSON body.
    /// </summary>
    private static async Task<TimeSpan?> RetryAfterAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.Headers.RetryAfter is { } header)
        {
            if (header.Delta is { } delta) return delta;
            if (header.Date is { } date)
            {
                var left = date - DateTimeOffset.UtcNow;
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }
        if (resp.Content.Headers.ContentType?.MediaType?.Contains("json") != true) return null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("retryAfter", out var seconds) &&
                   seconds.TryGetDouble(out var s) && s >= 0
                ? TimeSpan.FromSeconds(s)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.Content.Headers.ContentType?.MediaType?.Contains("json") != true) return null;
        try
        {
            var body = await resp.Content.ReadFromJsonAsync<ApiErrorBody>(cancellationToken: ct);
            return string.IsNullOrWhiteSpace(body?.Code) ? null : body!.Code;
        }
        catch (JsonException)
        {
            return null; // Not a ProblemDetails body: no code to act on.
        }
    }
}
