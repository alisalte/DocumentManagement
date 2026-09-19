using Dms.Application;
using Dms.Authorization.Application;
using Dms.Authorization.Contracts;
using Dms.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Dms.Authorization.Infrastructure;

public static class AuthorizationEndpoints
{
    public sealed record CreateRoleRequest(string Code, string Name, string? Description);

    public sealed record SetRolePermissionsRequest(IReadOnlyList<string> PermissionCodes);

    public sealed record GrantPermissionRequest(
        SubjectType SubjectType,
        Guid SubjectId,
        string PermissionCode,
        PermissionEffect Effect = PermissionEffect.Allow,
        bool Inherit = true,
        string? Reason = null);

    public static IEndpointRouteBuilder MapAuthorizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var catalog = endpoints.MapGroup("/api/v1/permissions").WithTags("Permissions").RequireAuthorization();

        catalog.MapGet("/catalog", () => Results.Ok(PermissionCatalog.All.Select(definition => new
            {
                definition.Code,
                Scope = definition.Scope.ToString(),
                definition.RequiresView,
                definition.Description,
            })))
            .WithSummary("The permission catalog as defined in code.");

        catalog.MapGet("/mine", async (IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.QueryAsync(new GetMyPermissionsQuery(), ct);
                return result.ToHttpResult();
            })
            .WithSummary("System permissions of the signed-in user.");

        catalog.MapGet("/explain", async (
                Guid userId,
                ResourceType resourceType,
                Guid resourceId,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var query = new ExplainPermissionsQuery(userId, resourceType, resourceId);
                var result = await dispatcher.QueryAsync(query, ct);
                return result.ToHttpResult();
            })
            .WithSummary("Why a user can or cannot do each thing on a resource.");

        catalog.MapDelete("/{entryId:guid}", async (Guid entryId, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(new RevokeResourcePermissionCommand(entryId), ct);
                return result.ToHttpResult();
            })
            .WithSummary("Revoke one ACL entry.");

        var resources = endpoints.MapGroup("/api/v1/resources/{resourceType}/{resourceId:guid}/permissions")
            .WithTags("Permissions")
            .RequireAuthorization();

        resources.MapGet("", async (
            ResourceType resourceType,
            Guid resourceId,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.QueryAsync(new GetResourcePermissionsQuery(resourceType, resourceId), ct);
            return result.ToHttpResult();
        });

        resources.MapPost("", async (
            ResourceType resourceType,
            Guid resourceId,
            GrantPermissionRequest request,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var command = new GrantResourcePermissionCommand(
                resourceType,
                resourceId,
                request.SubjectType,
                request.SubjectId,
                request.PermissionCode,
                request.Effect,
                request.Inherit,
                request.Reason);

            var result = await dispatcher.SendAsync(command, ct);
            return result.ToHttpResult(id => Results.Created($"/api/v1/permissions/{id}", new { id }));
        });

        var roles = endpoints.MapGroup("/api/v1/admin/roles")
            .WithTags("Administration: roles")
            .RequireAuthorization()
            .RequireSystemPermission(PermissionCodes.AdminManageRoles);

        roles.MapGet("", async (IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.QueryAsync(new ListRolesQuery(), ct);
            return result.ToHttpResult();
        });

        roles.MapPost("", async (CreateRoleRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var command = new CreateRoleCommand(request.Code, request.Name, request.Description);
            var result = await dispatcher.SendAsync(command, ct);
            return result.ToHttpResult(id => Results.Created($"/api/v1/admin/roles/{id}", new { id }));
        });

        roles.MapPut("/{roleId:guid}/permissions", async (
            Guid roleId,
            SetRolePermissionsRequest request,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new SetRolePermissionsCommand(roleId, request.PermissionCodes),
                ct);

            return result.ToHttpResult();
        });

        roles.MapPut("/{roleId:guid}/users/{userId:guid}", async (
            Guid roleId,
            Guid userId,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new AssignRoleCommand(userId, roleId), ct);
            return result.ToHttpResult();
        });

        roles.MapDelete("/{roleId:guid}/users/{userId:guid}", async (
            Guid roleId,
            Guid userId,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new UnassignRoleCommand(userId, roleId), ct);
            return result.ToHttpResult();
        });

        return endpoints;
    }
}
