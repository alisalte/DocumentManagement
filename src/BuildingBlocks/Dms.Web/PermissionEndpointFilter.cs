using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Dms.Web;

/// <summary>
/// Endpoint-level guard for system permissions. Handlers check again: this filter is the fast path
/// and the place where a refused attempt becomes an audit record, not the only line of defence.
/// </summary>
public sealed class SystemPermissionFilter(string permissionCode) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var authorizer = context.HttpContext.RequestServices.GetRequiredService<IDmsAuthorizer>();
        var decision = await authorizer.AuthorizeSystemAsync(permissionCode, context.HttpContext.RequestAborted);

        if (decision.Allowed)
        {
            return await next(context);
        }

        var audit = context.HttpContext.RequestServices.GetRequiredService<IAuditWriter>();
        var unitOfWork = context.HttpContext.RequestServices.GetRequiredService<IUnitOfWork>();

        await unitOfWork.BeginAsync(context.HttpContext.RequestAborted);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.AccessDenied,
                Outcome = AuditOutcome.Denied,
                Metadata = new Dictionary<string, object?>
                {
                    ["permission"] = permissionCode,
                    ["path"] = context.HttpContext.Request.Path.Value,
                    ["reason"] = decision.Reason.ToString(),
                },
            },
            context.HttpContext.RequestAborted);
        await unitOfWork.CommitAsync(context.HttpContext.RequestAborted);

        return Results.Problem(
            title: "You do not have permission to do this.",
            detail: decision.Explanation,
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?> { ["code"] = "auth.forbidden" });
    }
}

public static class EndpointConventionExtensions
{
    public static TBuilder RequireSystemPermission<TBuilder>(this TBuilder builder, string permissionCode)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpoint => endpoint.Metadata.Add(new SystemPermissionRequirement(permissionCode)));
        return builder.AddEndpointFilter(new SystemPermissionFilter(permissionCode));
    }
}

/// <summary>Metadata so the permission requirement is visible in OpenAPI and diagnostics.</summary>
public sealed record SystemPermissionRequirement(string PermissionCode);
