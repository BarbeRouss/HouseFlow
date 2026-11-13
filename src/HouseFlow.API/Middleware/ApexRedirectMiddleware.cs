using HouseFlow.API.Configuration;

namespace HouseFlow.API.Middleware;

/// <summary>
/// Answers every request addressed to the apex domain with a <b>308</b> to the same path and query
/// on the canonical host (<see cref="ApexRedirectOptions"/>, issue #217). 308 rather than 301:
/// the method and body are preserved. Registered first in the pipeline, ahead of
/// <c>UseHouseFlowHttpEdge</c>: these requests are not API traffic, so the rate limiter and CORS
/// never see them. The ingress has already upgraded plain HTTP to HTTPS on the same host, so the
/// redirect never leaves HTTPS; the target is fixed by configuration, never derived from the
/// request, hence no open redirect.
/// </summary>
public sealed class ApexRedirectMiddleware(RequestDelegate next, ApexRedirectOptions options)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (!string.Equals(context.Request.Host.Host, options.Host, StringComparison.OrdinalIgnoreCase))
            return next(context);

        var request = context.Request;
        context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
        context.Response.Headers.Location =
            options.Target + request.PathBase.ToUriComponent() + request.Path.ToUriComponent() + request.QueryString.ToUriComponent();
        return Task.CompletedTask;
    }
}
