using System.Globalization;
using HouseFlow.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore.Storage;

namespace HouseFlow.API.Filters;

/// <summary>
/// Global exception filter that maps domain exceptions to ProblemDetails responses (with a machine
/// <c>code</c> when the exception carries one — see <see cref="ErrorCodes"/>), eliminating
/// repetitive try/catch blocks in controllers.
/// </summary>
public class DomainExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            // The execution strategy gave up on a transient database failure (serialization
            // conflicts that kept recurring, but also a database outage): report it as 5xx so it
            // stays visible to monitoring, without leaking EF's internal message.
            case RetryLimitExceededException:
                Handle(context, ApiProblem.Create(context.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "The service is temporarily unavailable. Please try again."));
                break;

            // Authentication failures raised outside the auth controller (defensive: 401, not 403).
            case AuthenticationFailedException authFailed:
                Handle(context, ApiProblem.Create(context.HttpContext, StatusCodes.Status401Unauthorized,
                    authFailed.Message, authFailed.ErrorCode));
                break;

            // Authenticated but not allowed on an existing resource.
            case UnauthorizedAccessException:
                Handle(context, ApiProblem.Create(context.HttpContext, StatusCodes.Status403Forbidden,
                    "Access denied", ErrorCodes.Forbidden));
                break;

            case KeyNotFoundException notFound:
                Handle(context, ApiProblem.Create(context.HttpContext, StatusCodes.Status404NotFound,
                    notFound.Message, ErrorCodes.NotFound));
                break;

            // Quota utilisateur dépassé (ex. export RGPD limité à 1/heure) : 429 + Retry-After.
            case TooManyRequestsException tooMany:
                context.HttpContext.Response.Headers.RetryAfter =
                    tooMany.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                Handle(context, ApiProblem.Create(context.HttpContext, StatusCodes.Status429TooManyRequests,
                    tooMany.Message, tooMany.ErrorCode));
                break;

            case ConflictException conflict:
                Handle(context, ApiProblem.Create(context.HttpContext, StatusCodes.Status409Conflict,
                    conflict.Message, conflict.ErrorCode));
                break;

            case InvalidOperationException ex:
                Handle(context, ApiProblem.Create(context.HttpContext, StatusCodes.Status400BadRequest,
                    ex.Message, (ex as ICodedException)?.ErrorCode));
                break;
        }
    }

    private static void Handle(ExceptionContext context, IActionResult result)
    {
        context.Result = result;
        context.ExceptionHandled = true;
    }
}

/// <summary>Builds the API's error responses: RFC 9457 ProblemDetails plus an optional machine <c>code</c>.</summary>
public static class ApiProblem
{
    public static ObjectResult Create(HttpContext httpContext, int status, string? detail, string? code = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrase(status),
            Detail = detail,
            Instance = httpContext.Request.Path
        };
        if (code != null) problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }

    /// <summary>Problem response for a coded exception (e.g. from the auth service).</summary>
    public static ObjectResult FromException(HttpContext httpContext, int status, Exception exception) =>
        Create(httpContext, status, exception.Message, (exception as ICodedException)?.ErrorCode);

    private static string ReasonPhrase(int status) =>
        Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(status);
}
