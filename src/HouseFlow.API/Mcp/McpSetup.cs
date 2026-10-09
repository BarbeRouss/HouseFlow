using System.Text.Json;
using HouseFlow.API.OAuth;
using HouseFlow.Application.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace HouseFlow.API.Mcp;

/// <summary>
/// The MCP server (issue #305): Streamable HTTP on <c>/mcp</c>, a resource server of the embedded
/// authorization server (#304). It accepts OAuth access tokens only — never the REST API's own JWTs
/// or API keys — whose audience is the MCP resource and whose scopes cover the tool called.
/// </summary>
public static class McpSetup
{
    public const string AuthorizationPolicy = "McpRead";
    public const string ProtectedResourceMetadataPath = "/.well-known/oauth-protected-resource";

    public static IServiceCollection AddHouseFlowMcp(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<McpCaller>();

        services.AddAuthorizationBuilder().AddPolicy(AuthorizationPolicy, policy => policy
            .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.Resource is HttpContext http && McpCaller.IsTokenForThisServer(http, context.User)));

        services.AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true) // no session to keep: every call re-authenticates
            .WithTools<HouseFlowTools>();

        return services;
    }

    /// <summary>Before authentication: the challenge is written by the authorization middleware.</summary>
    public static void UseHouseFlowMcpChallenge(this WebApplication app)
    {
        // A 401 tells the client where to discover the authorization server (RFC 9728 §5.1).
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(OAuthResources.McpPath))
            {
                context.Response.OnStarting(() =>
                {
                    if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
                    {
                        var metadata = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{ProtectedResourceMetadataPath}";
                        context.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{metadata}\"";
                    }
                    return Task.CompletedTask;
                });
            }
            await next();
        });
    }

    public static void MapHouseFlowMcp(this WebApplication app)
    {
        app.MapMcp(OAuthResources.McpPath).RequireAuthorization(AuthorizationPolicy);

        // RFC 9728: with and without the path suffix of the resource.
        app.MapGet(ProtectedResourceMetadataPath, ProtectedResourceMetadata).AllowAnonymous();
        app.MapGet(ProtectedResourceMetadataPath + OAuthResources.McpPath, ProtectedResourceMetadata).AllowAnonymous();
    }

    private static IResult ProtectedResourceMetadata(HttpRequest request, IOptions<OAuthOptions> options)
    {
        var settings = options.Value;
        var origin = $"{request.Scheme}://{request.Host}{request.PathBase}/";
        var issuer = string.IsNullOrEmpty(settings.Issuer) ? origin : settings.Issuer;

        return Results.Json(new Dictionary<string, object>
        {
            ["resource"] = OAuthResources.Allowed(request, settings)[0],
            ["authorization_servers"] = new[] { issuer },
            ["scopes_supported"] = OAuthScopes.Grantable,
            ["bearer_methods_supported"] = new[] { "header" },
            ["resource_name"] = "HouseFlow"
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
