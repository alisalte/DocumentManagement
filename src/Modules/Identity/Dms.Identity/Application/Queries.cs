using Dms.Application;
using Dms.Authorization.Contracts;
using Dms.SharedKernel;

namespace Dms.Identity.Application;

public sealed record UserDto(
    Guid Id,
    string Username,
    string DisplayName,
    string? Email,
    bool IsActive,
    bool IsSystemAdmin,
    DateTimeOffset CreatedAt);

public sealed record GroupDto(Guid Id, string Code, string Name, string Kind, bool IsActive);

public sealed record CurrentUserDto(
    Guid Id,
    string Username,
    string DisplayName,
    string? Email,
    bool IsSystemAdmin,
    bool MustChangePassword,
    IReadOnlyList<Guid> GroupIds,
    IReadOnlyList<string> SystemPermissions);

public sealed record GetCurrentUserQuery : IQuery<Result<CurrentUserDto>>;

public sealed record ListUsersQuery(string? Search, int Skip, int Take) : IQuery<Result<IReadOnlyList<UserDto>>>;

public sealed record ListGroupsQuery : IQuery<Result<IReadOnlyList<GroupDto>>>;

public sealed class GetCurrentUserHandler(
    IUserRepository users,
    IDmsAuthorizer authorizer,
    ICurrentUser currentUser) : IQueryHandler<GetCurrentUserQuery, Result<CurrentUserDto>>
{
    public async Task<Result<CurrentUserDto>> HandleAsync(
        GetCurrentUserQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<CurrentUserDto>(IdentityErrors.Unauthenticated);
        }

        var user = await users.FindAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<CurrentUserDto>(IdentityErrors.UserNotFound);
        }

        var principal = await authorizer.GetPrincipalsAsync(userId, cancellationToken);

        // A system administrator holds every system permission implicitly; report the effective
        // set so the UI shows the same thing the server will enforce.
        var systemPermissions = principal.IsSystemAdmin
            ? PermissionCatalog.All
                .Where(definition => definition.Scope != PermissionScope.Resource)
                .Select(definition => definition.Code)
                .ToList()
            : principal.SystemPermissions.OrderBy(code => code).ToList();

        return Result.Success(new CurrentUserDto(
            user.Id.Value,
            user.Username,
            user.DisplayName,
            user.Email,
            user.IsSystemAdmin,
            user.MustChangePassword,
            principal.GroupIds.Select(id => id.Value).ToList(),
            systemPermissions));
    }
}

public sealed class ListUsersHandler(IDmsAuthorizer authorizer, IUserRepository users)
    : IQueryHandler<ListUsersQuery, Result<IReadOnlyList<UserDto>>>
{
    public async Task<Result<IReadOnlyList<UserDto>>> HandleAsync(
        ListUsersQuery query,
        CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageUsers, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<IReadOnlyList<UserDto>>(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var take = Math.Clamp(query.Take, 1, 200);
        var found = await users.ListAsync(query.Search, Math.Max(query.Skip, 0), take, cancellationToken);
        IReadOnlyList<UserDto> result = found
            .Select(user => new UserDto(
                user.Id.Value,
                user.Username,
                user.DisplayName,
                user.Email,
                user.IsActive,
                user.IsSystemAdmin,
                user.CreatedAt))
            .ToList();

        return Result.Success(result);
    }
}

public sealed class ListGroupsHandler(IDmsAuthorizer authorizer, IGroupRepository groups)
    : IQueryHandler<ListGroupsQuery, Result<IReadOnlyList<GroupDto>>>
{
    public async Task<Result<IReadOnlyList<GroupDto>>> HandleAsync(
        ListGroupsQuery query,
        CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageGroups, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<IReadOnlyList<GroupDto>>(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var found = await groups.ListAsync(cancellationToken);
        IReadOnlyList<GroupDto> result = found
            .Select(group => new GroupDto(
                group.Id.Value,
                group.Code,
                group.Name,
                group.Kind.ToString(),
                group.IsActive))
            .ToList();

        return Result.Success(result);
    }
}
