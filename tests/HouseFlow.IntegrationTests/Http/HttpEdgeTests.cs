using System.Net;
using System.Text.Json;
using FluentAssertions;
using HouseFlow.API.Authentication;
using HouseFlow.API.Configuration;
using HouseFlow.Application.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HouseFlow.IntegrationTests.Http;

/// <summary>
/// The API's HTTP edge (<see cref="HttpEdge"/>) in process, as configured for the Azure
/// environments: trusted ingress, rate-limiting policies, ProblemDetails 429 with CORS headers,
/// Secure cookie and HSTS. The Aspire-hosted API of the other integration tests runs in
/// Development, where rate limiting is off — hence a dedicated host here.
/// </summary>
public class HttpEdgeTests
{
    private const string Origin = "https://www.houseflow.test";

    // ---------------------------------------------------------------- forwarded headers

    private static async Task<HttpContext> ThroughForwardedHeadersAsync(string remoteIp, string forwardedFor, string forwardedProto)
    {
        var options = new ForwardedHeadersOptions();
        HttpEdge.ConfigureForwardedHeaders(options);
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
        await middleware.Invoke(context);
        return context;
    }

    [Fact]
    public async Task ForwardedHeaders_FromTheContainerAppsIngress_GiveTheRealClientIpAndScheme()
    {
        // As observed in production: the ingress connects from an IPv4-mapped IPv6 address of
        // 100.64.0.0/10. Only the entry it appended (the right-most) is consumed.
        var context = await ThroughForwardedHeadersAsync("::ffff:100.100.0.181", "203.0.113.66, 87.64.119.99", "https");

        context.Connection.RemoteIpAddress!.ToString().Should().Be("87.64.119.99");
        context.Request.IsHttps.Should().BeTrue();
    }

    [Fact]
    public async Task ForwardedHeaders_FromAnUntrustedPeer_AreIgnored()
    {
        var context = await ThroughForwardedHeadersAsync("198.51.100.20", "87.64.119.99", "https");

        context.Connection.RemoteIpAddress!.ToString().Should().Be("198.51.100.20");
        context.Request.IsHttps.Should().BeFalse();
    }

    [Theory]
    [InlineData("87.64.119.99", "87.64.119.99")]
    [InlineData("::ffff:87.64.119.99", "87.64.119.99")]
    [InlineData("2001:db8:85a3:8d3:1319:8a2e:370:7348", "2001:db8:85a3:8d3::/64")]
    public void RateLimitPartition_IsTheIPv4AddressOrTheIPv6Slash64(string address, string expected) =>
        HttpEdge.ClientPartitionKey(IPAddress.Parse(address)).Should().Be(expected);

    // ---------------------------------------------------------------- rate limiting (in a real host)

    private sealed class EdgeHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public HttpClient Client { get; }

        private EdgeHost(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public static async Task<EdgeHost> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Production,
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
                .WithOrigins(Origin).AllowCredentials().WithExposedHeaders("Retry-After")));
            builder.Services.AddHouseFlowHttpEdge(rateLimiting: true);

            var app = builder.Build();
            app.UseHouseFlowHttpEdge(rateLimiting: true);
            app.MapPost("/login", () => "ok").RequireRateLimiting(RateLimitPolicies.Credentials);
            app.MapPost("/refresh", () => "ok").RequireRateLimiting(RateLimitPolicies.Session);
            await app.StartAsync();

            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.First();
            return new EdgeHost(app, new HttpClient { BaseAddress = new Uri(address) });
        }

        public Task<HttpResponseMessage> PostAsync(string path, string? clientIp = null, bool viaHttps = false)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add("Origin", Origin);
            // The test client connects from loopback, a trusted proxy by default: it plays the ingress.
            if (clientIp != null) request.Headers.Add("X-Forwarded-For", clientIp);
            if (viaHttps) request.Headers.Add("X-Forwarded-Proto", "https");
            return Client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task CredentialsLimit_Returns429ProblemDetails_WithRetryAfterAndCorsHeaders()
    {
        await using var host = await EdgeHost.StartAsync();
        for (var i = 0; i < 5; i++)
            (await host.PostAsync("/login", "198.51.100.1")).StatusCode.Should().Be(HttpStatusCode.OK);

        var rejected = await host.PostAsync("/login", "198.51.100.1");

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().Should().Be(ErrorCodes.RateLimited);
        var retryAfter = rejected.Headers.RetryAfter?.Delta;
        retryAfter.Should().NotBeNull().And.BeGreaterThan(TimeSpan.Zero);
        body.RootElement.GetProperty("retryAfter").GetInt32().Should().Be((int)retryAfter!.Value.TotalSeconds,
            "the body repeats Retry-After for clients that cannot read the header");
        // CORS runs before the limiter: without this header the browser sees a network error.
        rejected.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle(Origin);
    }

    [Fact]
    public async Task SessionRefresh_HasItsOwnGenerousLimit_IndependentOfTheCredentialsOne()
    {
        await using var host = await EdgeHost.StartAsync();
        for (var i = 0; i < 6; i++) await host.PostAsync("/login", "198.51.100.2");

        // A dozen page loads in a minute — each one refreshes the session — stay fine even once
        // the login limit of the same client is exhausted.
        for (var i = 0; i < 12; i++)
            (await host.PostAsync("/refresh", "198.51.100.2")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RateLimits_ArePerRealClient_NotPerProxy()
    {
        await using var host = await EdgeHost.StartAsync();
        for (var i = 0; i < 6; i++) await host.PostAsync("/login", "198.51.100.3");

        (await host.PostAsync("/login", "198.51.100.4")).StatusCode
            .Should().Be(HttpStatusCode.OK, "another client behind the same ingress has its own bucket");
    }

    [Fact]
    public async Task HttpsThroughTheIngress_GetsHsts_OutsideDevelopment()
    {
        await using var host = await EdgeHost.StartAsync();

        var response = await host.PostAsync("/refresh", "198.51.100.5", viaHttps: true);

        response.Headers.Contains("Strict-Transport-Security").Should().BeTrue();
    }

    // ---------------------------------------------------------------- refresh cookie

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "HouseFlow.API";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static CookieOptions CookieOverHttp(string environment)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<IHostEnvironment>(new FakeEnvironment(environment))
                .BuildServiceProvider()
        };
        context.Request.Scheme = "http";
        return RefreshTokenCookie.Options(context.Request, SameSiteMode.Lax, expires: null);
    }

    [Fact]
    public void RefreshCookie_IsSecureOutsideDevelopment_WhateverTheDetectedScheme()
    {
        CookieOverHttp(Environments.Production).Secure.Should().BeTrue();
        CookieOverHttp(Environments.Staging).Secure.Should().BeTrue();
        CookieOverHttp(Environments.Development).Secure.Should().BeFalse("plain HTTP on localhost in development");
    }
}
