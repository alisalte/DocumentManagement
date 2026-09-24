using Dms.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace Dms.Web;

/// <summary>
/// Maps domain results onto HTTP. One place, so no endpoint invents its own status codes.
/// </summary>
public static class ApiResults
{
    public static IResult ToHttpResult(this Result result, int successStatusCode = StatusCodes.Status204NoContent) =>
        result.IsSuccess
            ? Results.StatusCode(successStatusCode)
            : Problem(result.Error);

    public static IResult ToHttpResult<TValue>(this Result<TValue> result, Func<TValue, IResult>? onSuccess = null) =>
        result.IsSuccess
            ? onSuccess?.Invoke(result.Value) ?? Results.Ok(result.Value)
            : Problem(result.Error);

    public static IResult Problem(Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status500InternalServerError,
        };

        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
        if (error.FieldErrors is { Count: > 0 } fields)
        {
            // Same shape as ASP.NET Core's ValidationProblemDetails, so clients handle both alike.
            extensions["errors"] = fields;
        }

        return Results.Problem(
            title: TitleFor(error.Type),
            detail: error.Message,
            statusCode: statusCode,
            extensions: extensions);
    }

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.Validation => "The request is not valid.",
        ErrorType.Unauthorized => "Authentication is required.",
        ErrorType.Forbidden => "You do not have permission to do this.",
        ErrorType.NotFound => "The resource was not found.",
        ErrorType.Conflict => "The request conflicts with the current state.",
        ErrorType.TooManyRequests => "Too many attempts.",
        _ => "The request could not be completed.",
    };
}
