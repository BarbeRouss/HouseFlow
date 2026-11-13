using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using HouseFlow.Application.OAuth;
using HouseFlow.Infrastructure.OAuth;
using Microsoft.Extensions.Logging.Abstractions;

namespace HouseFlow.UnitTests.OAuth;

/// <summary>The download of a client metadata document (#308): what it accepts, what it refuses, how often.</summary>
public class ClientMetadataDocumentFetcherTests
{
    private static readonly Uri Url = new("https://claude.ai/.well-known/oauth-client-metadata");
    private const string Caller = "203.0.113.7";

    private const string Json = """{ "client_id": "https://claude.ai/.well-known/oauth-client-metadata", "client_name": "Claude", "redirect_uris": ["https://claude.ai/cb"] }""";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>A body without a Content-Length: only the bytes actually read can be trusted.</summary>
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(bytes, 0, bytes.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static HttpResponseMessage Ok(string body = Json, string contentType = "application/json", Action<HttpResponseMessage>? configure = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8) { Headers = { ContentType = MediaTypeHeaderValue.Parse(contentType) } }
        };
        configure?.Invoke(response);
        return response;
    }

    private static ClientMetadataDocumentFetcher Fetcher(StubHandler handler, TimeProvider? clock = null) =>
        new(NullLogger<ClientMetadataDocumentFetcher>.Instance, clock, handler);

    [Fact]
    public async Task Document_IsParsed_WithItsCacheLifetime()
    {
        var handler = new StubHandler(_ => Ok(configure: r => r.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromMinutes(20) }));

        var result = await Fetcher(handler).FetchAsync(Url, Caller);

        result.Succeeded.Should().BeTrue();
        result.Document!.ClientId.Should().Be(Url.AbsoluteUri);
        result.Document.ClientName.Should().Be("Claude");
        result.Document.RedirectUris.Should().Equal("https://claude.ai/cb");
        result.MaxAge.Should().Be(TimeSpan.FromMinutes(20));
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.Headers.Authorization.Should().BeNull("no credential is ever sent to a document host");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    public async Task AnyStatusButOk_IsRefused_RedirectsIncluded(HttpStatusCode status)
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(Json, Encoding.UTF8, "application/json") };
            response.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data");
            return response;
        });

        var result = await Fetcher(handler).FetchAsync(Url, Caller);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(ClientMetadataFetchFailure.BadStatus);
        handler.Calls.Should().Be(1, "a redirect is never followed");
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    public async Task OtherContentTypes_AreRefused(string contentType)
    {
        var result = await Fetcher(new StubHandler(_ => Ok(contentType: contentType))).FetchAsync(Url, Caller);

        result.Failure.Should().Be(ClientMetadataFetchFailure.BadContentType);
    }

    [Fact]
    public async Task JsonContentType_WithACharset_IsAccepted()
    {
        var result = await Fetcher(new StubHandler(_ => Ok(contentType: "application/json; charset=utf-8"))).FetchAsync(Url, Caller);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ADocumentOverSixteenKilobytes_IsRefused()
    {
        var body = Json + new string(' ', ClientMetadataDocumentFetcher.MaxDocumentBytes);

        var result = await Fetcher(new StubHandler(_ => Ok(body))).FetchAsync(Url, Caller);

        result.Failure.Should().Be(ClientMetadataFetchFailure.TooLarge);
    }

    [Fact]
    public async Task TheSizeIsCountedAsReceived_NotTakenFromContentLength()
    {
        var body = Encoding.UTF8.GetBytes(Json + new string(' ', ClientMetadataDocumentFetcher.MaxDocumentBytes));
        var handler = new StubHandler(_ =>
        {
            var content = new UnknownLengthContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var result = await Fetcher(handler).FetchAsync(Url, Caller);

        result.Failure.Should().Be(ClientMetadataFetchFailure.TooLarge);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{ "redirect_uris": "https://claude.ai/cb" }""")]
    public async Task ABodyThatIsNotAClientDocument_IsRefused(string body)
    {
        var result = await Fetcher(new StubHandler(_ => Ok(body))).FetchAsync(Url, Caller);

        result.Failure.Should().Be(ClientMetadataFetchFailure.InvalidJson);
    }

    [Fact]
    public async Task ANetworkFailure_IsRefused()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        var result = await Fetcher(handler).FetchAsync(Url, Caller);

        result.Failure.Should().Be(ClientMetadataFetchFailure.Unreachable);
    }

    [Fact]
    public async Task ARefusedAddress_IsReportedAsSuch()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("blocked", new NonPublicAddressException("internal.example", "address")));

        var result = await Fetcher(handler).FetchAsync(Url, Caller);

        result.Failure.Should().Be(ClientMetadataFetchFailure.NonPublicAddress);
    }

    [Fact]
    public async Task AFailure_IsRemembered_ForAMinute()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-11-11T12:00:00Z"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var fetcher = Fetcher(handler, clock);

        await fetcher.FetchAsync(Url, Caller);
        var second = await fetcher.FetchAsync(Url, Caller);
        clock.Now += TimeSpan.FromSeconds(59);
        await fetcher.FetchAsync(Url, Caller);

        second.Failure.Should().Be(ClientMetadataFetchFailure.BadStatus);
        handler.Calls.Should().Be(1, "no request leaves while the failure is remembered");

        clock.Now += TimeSpan.FromSeconds(2);
        await fetcher.FetchAsync(Url, Caller);
        handler.Calls.Should().Be(2, "and one is made again once the minute is over");
    }

    [Fact]
    public async Task ACallerIsLimited_AcrossUrls()
    {
        var handler = new StubHandler(_ => Ok());
        var fetcher = Fetcher(handler);

        var results = new List<ClientMetadataFetchResult>();
        for (var i = 0; i < ClientMetadataDocumentFetcher.FetchesPerCallerPerMinute + 1; i++)
            results.Add(await fetcher.FetchAsync(new Uri($"https://client{i}.example/doc"), Caller));

        results.Take(ClientMetadataDocumentFetcher.FetchesPerCallerPerMinute).Should().OnlyContain(r => r.Succeeded);
        results.Last().Failure.Should().Be(ClientMetadataFetchFailure.RateLimited);
        handler.Calls.Should().Be(ClientMetadataDocumentFetcher.FetchesPerCallerPerMinute);

        (await fetcher.FetchAsync(new Uri("https://other.example/doc"), "198.51.100.1")).Succeeded.Should().BeTrue("another caller has its own bucket");
    }

    [Fact]
    public async Task AUrlIsLimited_WhoeverAsks()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-11-11T12:00:00Z"));
        var handler = new StubHandler(_ => Ok());
        var fetcher = Fetcher(handler, clock);

        var results = new List<ClientMetadataFetchResult>();
        for (var i = 0; i < ClientMetadataDocumentFetcher.FetchesPerUrlPerMinute + 1; i++)
            results.Add(await fetcher.FetchAsync(Url, $"198.51.100.{i}"));

        results.Last().Failure.Should().Be(ClientMetadataFetchFailure.RateLimited);

        clock.Now += TimeSpan.FromSeconds(61);
        (await fetcher.FetchAsync(Url, "198.51.100.99")).Succeeded.Should().BeTrue("the window is over");
    }

    // ---- the real handler: no connection is ever made to a non-public address (no network needed).

    [Theory]
    [InlineData("https://localhost/client.json")]
    [InlineData("https://127.0.0.1/client.json")]
    [InlineData("https://[::1]/client.json")]
    [InlineData("https://10.0.0.5/client.json")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://168.63.129.16/machine")]
    [InlineData("https://[::ffff:10.0.0.1]/client.json")]
    public async Task TheRealHandler_RefusesNonPublicAddresses(string url)
    {
        using var fetcher = new ClientMetadataDocumentFetcher(NullLogger<ClientMetadataDocumentFetcher>.Instance);

        var result = await fetcher.FetchAsync(new Uri(url), Caller);

        result.Failure.Should().Be(ClientMetadataFetchFailure.NonPublicAddress);
    }

    [Fact]
    public async Task TheRealHandler_RefusesAPortOtherThan443()
    {
        using var fetcher = new ClientMetadataDocumentFetcher(NullLogger<ClientMetadataDocumentFetcher>.Instance);

        var result = await fetcher.FetchAsync(new Uri("https://claude.ai:8443/client.json"), Caller);

        result.Failure.Should().Be(ClientMetadataFetchFailure.NonPublicAddress);
    }
}
