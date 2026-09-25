using Dms.Application;
using Dms.Authorization.Contracts;
using Dms.Identity.Application;
using Dms.Identity.Contracts;
using Dms.Identity.Domain;
using Dms.SharedKernel;
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

    public sealed record SetManagerRequest(Guid? ManagerId);

    public sealed record CreateGroupRequest(string Code, string Name, GroupKind Kind = GroupKind.Other);

    public sealed record UpdateUserRequest(string DisplayName, string? Email);

    public sealed record ResetPasswordRequest(string NewPassword, bool MustChangePassword = true);

    public sealed record SetAdminRequest(bool IsSystemAdmin);

    public sealed record UpdateGroupRequest(string Name, bool IsActive = true);

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

        users.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new GetUserQuery(id), ct)).ToHttpResult())
            .WithSummary("One user with their manager and groups.");

        users.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest request, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new UpdateUserCommand(id, request.DisplayName, request.Email), ct)).ToHttpResult())
            .WithSummary("Change a user's display name and email.");

        users.MapPost("/{id:guid}/password", async (Guid id, ResetPasswordRequest request, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new ResetUserPasswordCommand(id, request.NewPassword, request.MustChangePassword), ct)).ToHttpResult())
            .WithSummary("Set a new password for someone else; their sessions end. Not for your own account.");

        users.MapPut("/{id:guid}/admin", async (Guid id, SetAdminRequest request, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new SetUserAdminCommand(id, request.IsSystemAdmin), ct)).ToHttpResult())
            .WithSummary("Make or unmake a system administrator (system administrators only; never the last one).");

        users.MapPut("/{id:guid}/manager", async (Guid id, SetManagerRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.SendAsync(new SetUserManagerCommand(id, request.ManagerId), ct)).ToHttpResult());

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

        groups.MapPut("/{groupId:guid}", async (Guid groupId, UpdateGroupRequest request, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new UpdateGroupCommand(groupId, request.Name, request.IsActive), ct)).ToHttpResult())
            .WithSummary("Rename a group, or switch it off (its members lose what it grants).");

        groups.MapGet("/{groupId:guid}/members", async (Guid groupId, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new ListGroupMembersQuery(groupId), ct)).ToHttpResult())
            .WithSummary("Every member of the group, active or not.");

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

        // Pickers for USER and GROUP metadata fields. Any signed-in user may look people up by
        // name, as in any company directory; only names and ids are returned, never contact data.
        var directory = endpoints.MapGroup("/api/v1/directory").WithTags("Directory").RequireAuthorization();

        directory.MapGet("/users", async (string? search, string? ids, IUserDirectory users, CancellationToken ct) =>
        {
            var found = ParseIds(ids) is { Count: > 0 } wanted
                ? await users.FindManyAsync(wanted.Select(id => new UserId(id)).ToList(), ct)
                : await users.SearchAsync(search, 20, ct);

            return Results.Ok(found.Select(user => new { id = user.Id.Value, user.DisplayName, user.Username }));
        });

        directory.MapGet("/groups", async (string? search, string? ids, IGroupDirectory groups, CancellationToken ct) =>
        {
            var found = ParseIds(ids) is { Count: > 0 } wanted
                ? await groups.FindManyAsync(wanted.Select(id => new GroupId(id)).ToList(), ct)
                : await groups.SearchAsync(search, 20, ct);

            return Results.Ok(found.Select(group => new { id = group.Id.Value, group.Name, group.Code }));
        });

        return endpoints;
    }

    private static List<Guid> ParseIds(string? ids) =>
        (ids ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => Guid.TryParse(id, out var value) ? value : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(50)
            .ToList();
}
