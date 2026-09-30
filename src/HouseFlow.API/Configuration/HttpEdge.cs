using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using HouseFlow.API.Middleware;
using HouseFlow.Application.Common;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using IPNetwork = System.Net.IPNetwork;

namespace HouseFlow.API.Configuration;

/// <summary>Rate-limiting policies, referenced by <c>[EnableRateLimiting]</c> on the controllers.</summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Credential checks (login, register): 5 per minute per client — the brute-force guard.
    /// </summary>
    public const string Credentials = "auth";

    /// <summary>
    /// Session upkeep (refresh, logout, revoke): 60 per minute per client. The access token lives
    /// in memory only, so every page load, new tab or browser restart calls <c>/auth/refresh</c>;
    /// under the 5/min credentials limit, the 5th reload of a minute logged the user out. There is
    /// nothing to brute-force here (a refresh token is 512 random bits), the limit only caps abuse.
    /// </summary>
    public const string Session = "session";
}

/// <summary>
/// The API's HTTP edge: reverse-proxy headers, ProblemDetails for every error status, security
/// headers, CORS and rate limiting — registered and ordered in one place, shared by Program.cs
/// and the tests that exercise it.
/// </summary>
public static class HttpEdge
{
    /// <summary>
    /// Networks whose <c>X-Forwarded-For</c> / <c>X-Forwarded-Proto</c> are trusted, on top of the
    /// loopback defaults.
    /// <para>
    /// <b>100.64.0.0/10</b> — the Azure Container Apps ingress (Envoy). The API container's only
    /// entry point is that ingress (<c>infrastructure/terraform/environment/apps.tf</c>:
    /// <c>ingress { external_enabled = true, target_port = 8080 }</c>, no other exposure), and the
    /// ingress reaches the app from this shared-address range (observed as
    /// <c>::ffff:100.100.0.181</c> — an IPv4-mapped IPv6 address, which the middleware matches
    /// against the IPv4 network). The range is not routable on the Internet, so no client can
    /// forge a header from it. Without this entry the proxy headers were ignored: every request
    /// seemed to come from the ingress (one rate-limit bucket for everyone, the proxy IP in the
    /// audit trail and in the consent proof) and over plain HTTP (no <c>Secure</c> cookie, no HSTS).
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<IPNetwork> TrustedProxyNetworks = [IPNetwork.Parse("100.64.0.0/10")];

    public static void ConfigureForwardedHeaders(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // One trusted hop: only the entry appended by the ingress is consumed; whatever the client
        // itself put in X-Forwarded-For stays untrusted (see HttpContextExtensions.GetClientIp).
        options.ForwardLimit = 1;
        foreach (var network in TrustedProxyNetworks)
            options.KnownIPNetworks.Add(network);
    }

    /// <summary>
    /// Machine <c>code</c> of a ProblemDetails built by the framework (bare <c>NotFound()</c>,
    /// model validation, challenge 401, rate-limiter 429, route-constraint 404…), aligned with the
    /// codes <c>ApiProblem</c> sets itself.
    /// </summary>
    public static string? DefaultCode(int? status) => status switch
    {
        StatusCodes.Status400BadRequest => ErrorCodes.ValidationFailed,
        StatusCodes.Status401Unauthorized => ErrorCodes.Unauthorized,
        StatusCodes.Status403Forbidden => ErrorCodes.Forbidden,
        StatusCodes.Status404NotFound => ErrorCodes.NotFound,
        StatusCodes.Status429TooManyRequests => ErrorCodes.RateLimited,
        _ => null
    };

    /// <summary>
    /// A 429 repeats its <c>Retry-After</c> header in the body as <c>retryAfter</c> (seconds): the
    /// client reads it even where a proxy or a CORS configuration would hide the header.
    /// </summary>
    public static void AddRetryAfter(ProblemDetails problem, HttpContext context)
    {
        if (problem.Status != StatusCodes.Status429TooManyRequests || problem.Extensions.ContainsKey("retryAfter")) return;
        if (int.TryParse(context.Response.Headers.RetryAfter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            problem.Extensions["retryAfter"] = seconds;
    }

    public static IServiceCollection AddHouseFlowHttpEdge(this IServiceCollection services, bool rateLimiting)
    {
        services.Configure<ForwardedHeadersOptions>(ConfigureForwardedHeaders);

        // IProblemDetailsService: used by UseStatusCodePages below for every error response that
        // has no body yet. The same options customise the MVC factory's ProblemDetails.
        services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
        {
            AddRetryAfter(ctx.ProblemDetails, ctx.HttpContext);
            if (ctx.ProblemDetails.Extensions.ContainsKey("code")) return;
            if (DefaultCode(ctx.ProblemDetails.Status) is { } code)
                ctx.ProblemDetails.Extensions["code"] = code;
        });

        if (rateLimiting)
            services.AddRateLimiter(ConfigureRateLimiter);

        return services;
    }

    /// <summary>
    /// Middleware order matters: forwarded headers first (everything below needs the real client
    /// IP and scheme), status code pages before anything that can end a request with a bare
    /// status, and CORS <b>before</b> the rate limiter — a 429 without
    /// <c>Access-Control-Allow-Origin</c> is an opaque network error to the browser, which the
    /// frontend could not tell from an outage.
    /// </summary>
    public static IApplicationBuilder UseHouseFlowHttpEdge(this IApplicationBuilder app, bool rateLimiting)
    {
        app.UseForwardedHeaders();
        app.UseStatusCodePages();
        app.UseHttpsRedirection();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseCors();
        if (rateLimiting)
            app.UseRateLimiter();
        return app;
    }

    private static void ConfigureRateLimiter(RateLimiterOptions options)
    {
        // The body is written by UseStatusCodePages: ProblemDetails, code `rate_limited`.
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddPolicy(RateLimitPolicies.Credentials, ctx => PerClient(ctx, permitsPerMinute: 5));
        options.AddPolicy(RateLimitPolicies.Session, ctx => PerClient(ctx, permitsPerMinute: 60));

        // Global fallback: 200 requests per minute per client.
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => PerClient(ctx, permitsPerMinute: 200));

        options.OnRejected = (context, _) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }
            return ValueTask.CompletedTask;
        };
    }

    private static RateLimitPartition<string> PerClient(HttpContext context, int permitsPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientPartitionKey(context.Connection.RemoteIpAddress),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });

    /// <summary>
    /// Rate-limit bucket of a client: its IPv4 address, or its IPv6 <b>/64</b> — one subscriber
    /// line is handed a whole /64, so bucketing per /128 would let a single client rotate
    /// addresses at will.
    /// </summary>
    public static string ClientPartitionKey(IPAddress? address)
    {
        if (address == null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}
