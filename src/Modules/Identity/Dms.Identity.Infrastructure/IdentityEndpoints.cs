using Dms.Application;
using Dms.Authorization.Contracts;
using Dms.Identity.Application;
using Dms.Identity.Domain;
using Dms.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Dms.Identity.Infrastructure;

public static class IdentityEndpoints
{
    public sealed record LoginRequest(string Username, string Password);

    public sealed record RefreshRequest(string RefreshToken);

    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    public sealed record CreateUserRequest(
        string Username,
        string DisplayName,
        string? Email,
        string Password,
        bool IsSystemAdmin = false,
        bool MustChangePassword = true);

    public sealed record SetActiveRequest(bool IsActive);

    public sealed record CreateGroupRequest(string Code, string Name, GroupKind Kind = GroupKind.Other);

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/v1/auth").WithTags("Authentication");

        auth.MapPost("/login", async (LoginRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(new LoginCommand(request.Username, request.Password), ct);
                return result.ToHttpResult();
            })
            .AllowAnonymous()
            .RequireRateLimiting("login")
            .WithSummary("Sign in with a local account.");

        auth.MapPost("/refresh", async (RefreshRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(new RefreshTokenCommand(request.RefreshToken), ct);
                return result.ToHttpResult();
            })
            .AllowAnonymous()
            .RequireRateLimiting("login")
            .WithSummary("Exchange a refresh token for a new access token.");

        auth.MapPost("/logout", async (RefreshRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(new LogoutCommand(request.RefreshToken), ct);
                return result.ToHttpResult();
            })
            .RequireAuthorization()
            .WithSummary("Revoke the current refresh token.");

        auth.MapGet("/me", async (IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.QueryAsync(new GetCurrentUserQuery(), ct);
                return result.ToHttpResult();
            })
            .RequireAuthorization()
            .WithSummary("The signed-in user, their groups and their system permissions.");

        auth.MapPost("/change-password",
                async (ChangePasswordRequest request, IDispatcher dispatcher, CancellationToken ct) =>
                {
                    var command = new ChangeOwnPasswordCommand(request.CurrentPassword, request.NewPassword);
                    var result = await dispatcher.SendAsync(command, ct);
                    return result.ToHttpResult();
                })
            .RequireAuthorization()
            .WithSummary("Change your own password; all other sessions are ended.");

        var users = endpoints.MapGroup("/api/v1/admin/users")
            .WithTags("Administration: users")
            .RequireAuthorization()
            .RequireSystemPermission(PermissionCodes.AdminManageUsers);

        users.MapGet("", async (
            string? search,
            int? skip,
            int? take,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.QueryAsync(new ListUsersQuery(search, skip ?? 0, take ?? 50), ct);
            return result.ToHttpResult();
        });

        users.MapPost("", async (CreateUserRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var command = new CreateUserCommand(
                request.Username,
                request.DisplayName,
                request.Email,
                request.Password,
                request.IsSystemAdmin,
                request.MustChangePassword);

            var result = await dispatcher.SendAsync(command, ct);
            return result.ToHttpResult(id => Results.Created($"/api/v1/admin/users/{id}", new { id }));
        });

        users.MapPost("/{id:guid}/active", async (
            Guid id,
            SetActiveRequest request,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new SetUserActiveCommand(id, request.IsActive), ct);
            return result.ToHttpResult();
        });

        var groups = endpoints.MapGroup("/api/v1/admin/groups")
            .WithTags("Administration: groups")
            .RequireAuthorization()
            .RequireSystemPermission(PermissionCodes.AdminManageGroups);

        groups.MapGet("", async (IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.QueryAsync(new ListGroupsQuery(), ct);
            return result.ToHttpResult();
        });

        groups.MapPost("", async (CreateGroupRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new CreateGroupCommand(request.Code, request.Name, request.Kind),
                ct);

            return result.ToHttpResult(id => Results.Created($"/api/v1/admin/groups/{id}", new { id }));
        });

        groups.MapPut("/{groupId:guid}/members/{userId:guid}", async (
            Guid groupId,
            Guid userId,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new AddUserToGroupCommand(userId, groupId), ct);
            return result.ToHttpResult();
        });

        groups.MapDelete("/{groupId:guid}/members/{userId:guid}", async (
            Guid groupId,
            Guid userId,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new RemoveUserFromGroupCommand(userId, groupId), ct);
            return result.ToHttpResult();
        });

        return endpoints;
    }
}
