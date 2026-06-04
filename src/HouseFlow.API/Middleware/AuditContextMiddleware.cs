using System.Security.Claims;
using HouseFlow.Infrastructure.Data;

namespace HouseFlow.API.Middleware;

/// <summary>
/// Middleware that sets the audit context (user identity, IP, user agent)
/// on the DbContext for every authenticated request, ensuring all database
/// changes are properly attributed in the AuditLogs table.
/// </summary>
public class AuditContextMiddleware
{
    private readonly RequestDelegate _next;

    public AuditContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, HouseFlowDbContext dbContext)
    {
        Guid? userId = null;
        string? username = null;

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdClaim, out var parsed))
            {
                userId = parsed;
            }

            username = context.User.FindFirst(ClaimTypes.Email)?.Value;
        }

        var ipAddress = context.Connection.RemoteIpAddress?.ToString();
        var userAgent = context.Request.Headers.UserAgent.ToString();

        dbContext.SetAuditContext(userId, username, ipAddress, userAgent);

        await _next(context);
    }
}
