using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore.Storage;

namespace HouseFlow.API.Filters;

/// <summary>
/// Global exception filter that maps domain exceptions to HTTP responses,
/// eliminating repetitive try/catch blocks in controllers.
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
                context.Result = new ObjectResult(new { error = "The service is temporarily unavailable. Please try again." })
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable
                };
                context.ExceptionHandled = true;
                break;

            case UnauthorizedAccessException:
                context.Result = new ForbidResult();
                context.ExceptionHandled = true;
                break;

            case KeyNotFoundException:
                context.Result = new NotFoundObjectResult(new { error = context.Exception.Message });
                context.ExceptionHandled = true;
                break;

            case InvalidOperationException ex:
                context.Result = new BadRequestObjectResult(new { error = ex.Message });
                context.ExceptionHandled = true;
                break;
        }
    }
}
