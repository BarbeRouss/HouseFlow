using System.Security.Claims;
using HouseFlow.API.Extensions;
using HouseFlow.API.OAuth;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace HouseFlow.API.Mcp;

/// <summary>
/// Who calls a tool: the user behind the OAuth token. The subject feeds the very same per-household
/// access control as the REST API (the Application services take the user id), and every call is
/// audited — user, tool, time — without the token nor any of the data returned.
/// </summary>
public sealed class McpCaller
{
    public const string AuditEntityType = "McpTool";
    public const string AuditAction = "McpCall";

    private readonly IHttpContextAccessor _accessor;
    private readonly IApplicationDbContext _context;

    public McpCaller(IHttpContextAccessor accessor, IApplicationDbContext context)
    {
        _accessor = accessor;
        _context = context;
    }

    /// <summary>The token's audience (RFC 8707) is this MCP server, and it grants at least a scope of ours.</summary>
    public static bool IsTokenForThisServer(HttpContext http, ClaimsPrincipal user)
    {
        var options = http.RequestServices.GetRequiredService<IOptions<OAuthOptions>>().Value;
        var allowed = OAuthResources.Allowed(http.Request, options);
        return user.GetAudiences().Concat(user.GetResources()).Any(resource => allowed.Contains(OAuthResources.Normalize(resource) ?? resource, StringComparer.Ordinal))
            && user.HasScope(Application.OAuth.OAuthScopes.HousesRead);
    }

    /// <summary>Checks the scope the tool needs, audits the call and returns the user id.</summary>
    public async Task<Guid> BeginAsync(string tool, string requiredScope, CancellationToken cancellationToken)
    {
        var http = _accessor.HttpContext ?? throw new McpException("No HTTP context.");
        if (!http.User.HasScope(requiredScope))
            throw new McpException($"This tool requires the '{requiredScope}' scope.");
        if (!Guid.TryParse(http.User.GetClaim(Claims.Subject), out var userId))
            throw new McpException("The token has no valid subject.");

        var ip = http.GetClientIp();
        var userAgent = http.Request.Headers.UserAgent.ToString();
        _context.SetAuditContext(userId, null, ip, userAgent);
        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = AuditEntityType,
            EntityId = tool,
            Action = AuditAction,
            UserId = userId,
            Timestamp = DateTime.UtcNow,
            IpAddress = ip,
            UserAgent = userAgent
        });
        await _context.SaveChangesAsync(cancellationToken);
        return userId;
    }
}
