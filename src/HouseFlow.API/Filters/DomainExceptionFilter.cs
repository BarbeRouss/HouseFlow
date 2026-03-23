using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

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
