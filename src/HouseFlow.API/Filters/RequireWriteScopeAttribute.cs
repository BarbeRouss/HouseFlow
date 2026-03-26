using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HouseFlow.API.Filters;

/// <summary>
/// Global filter that enforces ReadWrite scope for API key authenticated write operations.
/// GET/HEAD/OPTIONS requests are always allowed. POST/PUT/DELETE/PATCH require ReadWrite scope.
/// JWT-authenticated requests (no api_key_scope claim) are not affected.
/// API key management endpoints (/api/v1/users/api-keys) are excluded so users can manage their own keys.
/// </summary>
public class ApiKeyScopeEnforcementFilter : IAuthorizationFilter
{
    private static readonly HashSet<string> ReadOnlyMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "OPTIONS"
    };

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Only enforce on write methods
        if (ReadOnlyMethods.Contains(context.HttpContext.Request.Method))
            return;

        var scopeClaim = context.HttpContext.User.FindFirst("api_key_scope");

        // No scope claim means JWT auth — allow
        if (scopeClaim == null)
            return;

        // Allow API key management endpoints (create/revoke own keys)
        var path = context.HttpContext.Request.Path.Value;
        if (path != null && path.StartsWith("/api/v1/users/api-keys", StringComparison.OrdinalIgnoreCase))
            return;

        if (scopeClaim.Value == "ReadOnly")
        {
            context.Result = new ObjectResult(new { error = "This API key has read-only access. A ReadWrite key is required for this operation." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
