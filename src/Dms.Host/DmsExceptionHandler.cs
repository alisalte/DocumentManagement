using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Dms.Host;

/// <summary>
/// Turns unhandled exceptions into ProblemDetails. Validation failures become 400 with the field
/// errors; everything else becomes a 500 that never leaks internals to the caller.
/// </summary>
public sealed class DmsExceptionHandler(ILogger<DmsExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is ValidationException validation)
        {
            var errors = validation.Errors
                .GroupBy(failure => failure.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(f => f.ErrorMessage).ToArray());

            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(
                new ValidationProblemDetails(errors)
                {
                    Title = "The request is not valid.",
                    Status = StatusCodes.Status400BadRequest,
                },
                cancellationToken);

            return true;
        }

        logger.LogError(exception, "Unhandled exception for {Method} {Path}.",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Title = "The request could not be completed.",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "An unexpected error occurred. The correlation id is in the trace headers.",
            },
            cancellationToken);

        return true;
    }
}
