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
    DateTimeOffset CreatedAt,
    bool MustChangePassword,
    Guid? ManagerId,
    DateTimeOffset? LastLoginAt);

/// <summary>One user for the administration screen: the list row plus manager and groups.</summary>
public sealed record UserDetailsDto(UserDto User, string? ManagerName, IReadOnlyList<GroupDto> Groups);

public sealed record GroupMemberDto(Guid Id, string Username, string DisplayName, bool IsActive);

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

public sealed record GetUserQuery(Guid UserId) : IQuery<Result<UserDetailsDto>>;

public sealed record ListGroupMembersQuery(Guid GroupId) : IQuery<Result<IReadOnlyList<GroupMemberDto>>>;

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
        IReadOnlyList<UserDto> result = found.Select(ToDto).ToList();

        return Result.Success(result);
    }

    internal static UserDto ToDto(Domain.User user) => new(
        user.Id.Value,
        user.Username,
        user.DisplayName,
        user.Email,
        user.IsActive,
        user.IsSystemAdmin,
        user.CreatedAt,
        user.MustChangePassword,
        user.ManagerId?.Value,
        user.LastLoginAt);
}

public sealed class GetUserHandler(
    IDmsAuthorizer authorizer,
    IUserRepository users,
    IGroupRepository groups,
    IMembershipRepository memberships) : IQueryHandler<GetUserQuery, Result<UserDetailsDto>>
{
    public async Task<Result<UserDetailsDto>> HandleAsync(GetUserQuery query, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageUsers, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<UserDetailsDto>(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var user = await users.FindAsync(new UserId(query.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserDetailsDto>(IdentityErrors.UserNotFound);
        }

        var manager = user.ManagerId is { } managerId ? await users.FindAsync(managerId, cancellationToken) : null;
        var groupIds = (await memberships.ListGroupIdsAsync(user.Id, cancellationToken)).ToHashSet();
        var userGroups = (await groups.ListAsync(cancellationToken))
            .Where(group => groupIds.Contains(group.Id))
            .Select(group => new GroupDto(group.Id.Value, group.Code, group.Name, group.Kind.ToString(), group.IsActive))
            .ToList();

        return Result.Success(new UserDetailsDto(ListUsersHandler.ToDto(user), manager?.DisplayName, userGroups));
    }
}

public sealed class ListGroupMembersHandler(
    IDmsAuthorizer authorizer,
    IGroupRepository groups,
    IMembershipRepository memberships,
    IUserRepository users) : IQueryHandler<ListGroupMembersQuery, Result<IReadOnlyList<GroupMemberDto>>>
{
    public async Task<Result<IReadOnlyList<GroupMemberDto>>> HandleAsync(ListGroupMembersQuery query, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageGroups, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<IReadOnlyList<GroupMemberDto>>(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var groupId = new GroupId(query.GroupId);
        if (await groups.FindAsync(groupId, cancellationToken) is null)
        {
            return Result.Failure<IReadOnlyList<GroupMemberDto>>(Error.NotFound("group.not_found", "The group does not exist."));
        }

        var ids = await memberships.ListMemberIdsAsync(groupId, cancellationToken);
        IReadOnlyList<GroupMemberDto> members = (await users.FindManyAsync(ids, cancellationToken))
            .OrderBy(user => user.DisplayName, StringComparer.CurrentCulture)
            .Select(user => new GroupMemberDto(user.Id.Value, user.Username, user.DisplayName, user.IsActive))
            .ToList();

        return Result.Success(members);
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
