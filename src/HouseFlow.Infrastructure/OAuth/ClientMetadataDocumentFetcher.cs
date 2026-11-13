using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using HouseFlow.Application.OAuth;
using Microsoft.Extensions.Logging;

namespace HouseFlow.Infrastructure.OAuth;

/// <summary>
/// Downloads Client ID Metadata Documents (issue #308) — the one place where HouseFlow fetches a URL
/// handed over by an anonymous caller, hence the SSRF boundary:
/// <list type="bullet">
/// <item>the connection goes to a <b>resolved public address</b> only (<see cref="OutboundAddressPolicy"/>,
/// checked in <c>ConnectCallback</c>, on the address actually connected to — not on a DNS answer that
/// could change), on port 443, with the TLS name check left on;</item>
/// <item>no redirect followed, no proxy, no cookie, no credential, no automatic decompression;</item>
/// <item>200 and <c>application/json</c> only, at most 16 KB read whatever <c>Content-Length</c> says,
/// 5 seconds for the whole exchange;</item>
/// <item>rate-limited per caller and per URL, and a failure is remembered for a minute.</item>
/// </list>
/// Logs the host and the reason of a refusal, never the document.
/// </summary>
public sealed class ClientMetadataDocumentFetcher : IClientMetadataDocumentFetcher, IDisposable
{
    public const int MaxDocumentBytes = 16 * 1024;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    public const int FetchesPerCallerPerMinute = 10;
    public const int FetchesPerUrlPerMinute = 6;

    private const int MaxNegativeEntries = 1000;
    private const int HttpsPort = 443;

    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly ILogger<ClientMetadataDocumentFetcher> _logger;
    private readonly FixedWindowCounter _perCaller;
    private readonly FixedWindowCounter _perUrl;
    private readonly ConcurrentDictionary<string, (ClientMetadataFetchFailure Failure, DateTimeOffset Until)> _failures = new(StringComparer.Ordinal);

    /// <param name="primaryHandler">Replaces the real, address-filtering handler: tests only.</param>
    public ClientMetadataDocumentFetcher(
        ILogger<ClientMetadataDocumentFetcher> logger, TimeProvider? time = null, HttpMessageHandler? primaryHandler = null)
    {
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _perCaller = new FixedWindowCounter(FetchesPerCallerPerMinute, _time);
        _perUrl = new FixedWindowCounter(FetchesPerUrlPerMinute, _time);
        // The timeout below only covers the headers: the exchange as a whole is bounded per request.
        _http = new HttpClient(primaryHandler ?? CreateHandler(), disposeHandler: true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    public async Task<ClientMetadataFetchResult> FetchAsync(Uri url, string callerKey, CancellationToken cancellationToken = default)
    {
        var key = url.AbsoluteUri;
        var now = _time.GetUtcNow();

        // A recent failure is answered from memory: no request leaves, and the limits are not consumed.
        if (_failures.TryGetValue(key, out var failed))
        {
            if (failed.Until > now) return ClientMetadataFetchResult.Failed(failed.Failure);
            _failures.TryRemove(key, out _);
        }

        if (!_perCaller.TryAcquire(callerKey) || !_perUrl.TryAcquire(key))
        {
            _logger.LogWarning("Client metadata fetch refused for {Host}: rate limited", url.IdnHost);
            return ClientMetadataFetchResult.Failed(ClientMetadataFetchFailure.RateLimited);
        }

        var result = await DownloadAsync(url, cancellationToken);
        if (!result.Succeeded)
        {
            _logger.LogWarning("Client metadata fetch failed for {Host}: {Reason}", url.IdnHost, result.Failure);
            Remember(key, result.Failure, now);
        }

        return result;
    }

    private async Task<ClientMetadataFetchResult> DownloadAsync(Uri url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url) { Version = HttpVersion.Version11 };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            // A 3xx is a failure: AllowAutoRedirect is off, and a redirect could lead anywhere.
            if (response.StatusCode != HttpStatusCode.OK)
                return ClientMetadataFetchResult.Failed(ClientMetadataFetchFailure.BadStatus);
            if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                return ClientMetadataFetchResult.Failed(ClientMetadataFetchFailure.BadContentType);
            if (response.Content.Headers.ContentLength > MaxDocumentBytes)
                return ClientMetadataFetchResult.Failed(ClientMetadataFetchFailure.TooLarge);

            var body = await ReadBoundedAsync(response.Content, timeout.Token);
            if (body is null)
                return ClientMetadataFetchResult.Failed(ClientMetadataFetchFailure.TooLarge);

            try
            {
                var document = JsonSerializer.Deserialize<ClientIdMetadataDocument>(body);
                return document is null
                    ? ClientMetadataFetchResult.Failed(ClientMetadataFetchFailure.InvalidJson)
                    : ClientMetadataFetchResult.Success(document, response.Headers.CacheControl?.MaxAge);
            }
            catch (JsonException)
            {
                return ClientMetadataFetchResult.Failed(ClientMetadataFetchFailure.InvalidJson);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or SocketException)
        {
            // The caller going away is not a failure of the document.
            cancellationToken.ThrowIfCancellationRequested();
            return ClientMetadataFetchResult.Failed(IsNonPublicAddress(ex)
                ? ClientMetadataFetchFailure.NonPublicAddress
                : ClientMetadataFetchFailure.Unreachable);
        }
    }

    /// <summary>The body, or null beyond <see cref="MaxDocumentBytes"/>: counted as it is read.</summary>
    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxDocumentBytes) return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private void Remember(string key, ClientMetadataFetchFailure failure, DateTimeOffset now)
    {
        if (_failures.Count >= MaxNegativeEntries)
        {
            foreach (var expired in _failures.Where(entry => entry.Value.Until <= now).Select(entry => entry.Key).ToList())
                _failures.TryRemove(expired, out _);
            if (_failures.Count >= MaxNegativeEntries) return; // memory stays bounded; the rate limits still apply
        }

        _failures[key] = (failure, now + ClientMetadataCachePolicy.NegativeTtl);
    }

    private static bool IsNonPublicAddress(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
            if (exception is NonPublicAddressException) return true;
        return false;
    }

    private static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        Credentials = null,
        PreAuthenticate = false,
        AutomaticDecompression = DecompressionMethods.None,
        MaxResponseHeadersLength = 16, // KB
        ConnectTimeout = TimeSpan.FromSeconds(3),
        ConnectCallback = ConnectToPublicAddressAsync
    };

    /// <summary>
    /// Resolves the host and connects to one of the addresses — after checking that <b>all</b> of
    /// them are public (<see cref="OutboundAddressPolicy"/>). The socket then goes to an address
    /// already checked, so nothing can change between the check and the connection (DNS rebinding);
    /// TLS still verifies the certificate against the host name. Public for the tests.
    /// </summary>
    public static async ValueTask<Stream> ConnectToPublicAddressAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        if (context.DnsEndPoint.Port != HttpsPort)
            throw new NonPublicAddressException(host, "port");

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(address => !OutboundAddressPolicy.IsPublic(address)))
            throw new NonPublicAddressException(host, "address");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, HttpsPort, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}

/// <summary>A document host that resolves to a non-public address, or a non-443 port: nothing is sent.</summary>
public sealed class NonPublicAddressException(string host, string what)
    : IOException($"{what} of {host} is not allowed for a client metadata document");

/// <summary>
/// At most <c>limit</c> acquisitions per key per minute (fixed window), in memory — per replica,
/// which is enough here. The keys are bounded: stale windows are dropped, and past the cap a new
/// key is refused rather than remembered.
/// </summary>
internal sealed class FixedWindowCounter(int limit, TimeProvider time)
{
    private const int MaxKeys = 10_000;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTimeOffset Start, int Count)> _windows = new(StringComparer.Ordinal);

    public bool TryAcquire(string key)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (_windows.TryGetValue(key, out var window) && now - window.Start < Window)
            {
                if (window.Count >= limit) return false;
                _windows[key] = (window.Start, window.Count + 1);
                return true;
            }

            if (_windows.Count >= MaxKeys)
            {
                foreach (var stale in _windows.Where(entry => now - entry.Value.Start >= Window).Select(entry => entry.Key).ToList())
                    _windows.Remove(stale);
                if (_windows.Count >= MaxKeys) return false;
            }

            _windows[key] = (now, 1);
            return true;
        }
    }
}
