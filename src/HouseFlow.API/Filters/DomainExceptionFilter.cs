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
            // The execution strategy gave up on a transient failure (e.g. a Postgres 40001 that
            // kept conflicting): the request did not apply and can safely be retried.
            case RetryLimitExceededException:
                context.Result = new ConflictObjectResult(new { error = "The request conflicted with a concurrent update. Please try again." });
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
