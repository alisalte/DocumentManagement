using Dms.Application;
using Dms.Authorization.Contracts;
using Dms.Identity.Contracts;
using Dms.SharedKernel;

namespace Dms.Authorization.Application;

public sealed record RoleDto(Guid Id, string Code, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions);

/// <param name="IsInherited">
/// Set on an ancestor category (with inherit on), so it applies here but is edited there.
/// </param>
public sealed record ResourcePermissionDto(
    Guid Id,
    string ResourceType,
    Guid ResourceId,
    string SubjectType,
    Guid SubjectId,
    string? SubjectName,
    string PermissionCode,
    string Effect,
    bool Inherit,
    string? Reason,
    DateTimeOffset CreatedAt,
    bool IsInherited);

/// <summary>The ACL entry behind a decision, named.</summary>
public sealed record DecisionSourceDto(
    string ResourceType,
    Guid ResourceId,
    string SubjectType,
    Guid SubjectId,
    string? SubjectName,
    string Effect);

public sealed record EffectivePermissionDto(
    string PermissionCode,
    bool Allowed,
    string Reason,
    string Explanation,
    DecisionSourceDto? Source);

/// <param name="UserId">Only the roles this user holds.</param>
public sealed record ListRolesQuery(Guid? UserId = null) : IQuery<Result<IReadOnlyList<RoleDto>>>;

public sealed record RoleMemberDto(Guid Id, string Username, string DisplayName, bool IsActive);

public sealed record ListRoleUsersQuery(Guid RoleId) : IQuery<Result<IReadOnlyList<RoleMemberDto>>>;

/// <param name="IncludeInherited">Also the entries of ancestor categories that reach this resource.</param>
public sealed record GetResourcePermissionsQuery(ResourceType ResourceType, Guid ResourceId, bool IncludeInherited = false)
    : IQuery<Result<IReadOnlyList<ResourcePermissionDto>>>;

/// <summary>
/// Answers "why can this user do this?" for administrators. Explaining the decision is part of the
/// product: a deny-wins model is only usable if the reason is visible.
/// </summary>
public sealed record ExplainPermissionsQuery(Guid UserId, ResourceType ResourceType, Guid ResourceId)
    : IQuery<Result<IReadOnlyList<EffectivePermissionDto>>>;

public sealed record GetMyPermissionsQuery : IQuery<Result<IReadOnlyList<string>>>;

public sealed class ListRolesHandler(IDmsAuthorizer authorizer, IRoleRepository roles, IRoleMembershipReader memberships)
    : IQueryHandler<ListRolesQuery, Result<IReadOnlyList<RoleDto>>>
{
    public async Task<Result<IReadOnlyList<RoleDto>>> HandleAsync(
        ListRolesQuery query,
        CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageRoles, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<IReadOnlyList<RoleDto>>(AuthorizationErrors.Forbidden(decision.Explanation));
        }

        var all = await roles.ListAsync(cancellationToken);
        if (query.UserId is { } userId)
        {
            var held = await memberships.GetRoleIdsAsync(new UserId(userId), cancellationToken);
            all = all.Where(role => held.Contains(role.Id)).ToList();
        }

        IReadOnlyList<RoleDto> result = all
            .Select(role => new RoleDto(
                role.Id.Value,
                role.Code,
                role.Name,
                role.Description,
                role.IsSystem,
                role.Permissions.Select(permission => permission.PermissionCode).OrderBy(code => code).ToList()))
            .ToList();

        return Result.Success(result);
    }
}

public sealed class GetResourcePermissionsHandler(
    AclAdministration administration,
    IResourceHierarchy hierarchy,
    IResourcePermissionRepository repository,
    SubjectNames names)
    : IQueryHandler<GetResourcePermissionsQuery, Result<IReadOnlyList<ResourcePermissionDto>>>
{
    public async Task<Result<IReadOnlyList<ResourcePermissionDto>>> HandleAsync(
        GetResourcePermissionsQuery query,
        CancellationToken cancellationToken)
    {
        var resource = new ResourceRef(query.ResourceType, query.ResourceId);
        var decision = await administration.RequireManageAsync(resource, "list", cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<IReadOnlyList<ResourcePermissionDto>>(
                AuthorizationErrors.Forbidden(decision.Explanation));
        }

        var ancestors = query.IncludeInherited && await hierarchy.DescribeAsync(resource, cancellationToken) is { } descriptor
            ? descriptor.AncestorCategoryIds.Select(ResourceRef.Category).ToList()
            : [];

        var entries = (await repository.GetForResourcesAsync([resource, .. ancestors], cancellationToken))
            .Where(entry => (entry.ResourceType == query.ResourceType && entry.ResourceId == query.ResourceId) || entry.Inherit)
            .ToList();

        var subjectNames = await names.ResolveAsync(entries.Select(entry => (entry.SubjectType, entry.SubjectId)), cancellationToken);
        IReadOnlyList<ResourcePermissionDto> result = entries
            .Select(entry => new ResourcePermissionDto(
                entry.Id.Value,
                entry.ResourceType.ToString(),
                entry.ResourceId,
                entry.SubjectType.ToString(),
                entry.SubjectId,
                subjectNames.GetValueOrDefault((entry.SubjectType, entry.SubjectId)),
                entry.PermissionCode,
                entry.Effect.ToString(),
                entry.Inherit,
                entry.Reason,
                entry.CreatedAt,
                IsInherited: entry.ResourceType != query.ResourceType || entry.ResourceId != query.ResourceId))
            .OrderBy(entry => entry.IsInherited)
            .ThenBy(entry => entry.PermissionCode, StringComparer.Ordinal)
            .ToList();

        return Result.Success(result);
    }
}

public sealed class ListRoleUsersHandler(
    IDmsAuthorizer authorizer,
    IRoleRepository roles,
    IRoleMembershipReader memberships,
    IUserDirectory users) : IQueryHandler<ListRoleUsersQuery, Result<IReadOnlyList<RoleMemberDto>>>
{
    public async Task<Result<IReadOnlyList<RoleMemberDto>>> HandleAsync(ListRoleUsersQuery query, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageRoles, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<IReadOnlyList<RoleMemberDto>>(AuthorizationErrors.Forbidden(decision.Explanation));
        }

        var roleId = new RoleId(query.RoleId);
        if (await roles.FindAsync(roleId, cancellationToken) is null)
        {
            return Result.Failure<IReadOnlyList<RoleMemberDto>>(AuthorizationErrors.RoleNotFound);
        }

        var ids = await memberships.GetUserIdsAsync(roleId, cancellationToken);
        IReadOnlyList<RoleMemberDto> members = ids.Count == 0
            ? []
            : (await users.FindManyAsync([.. ids], cancellationToken))
                .OrderBy(user => user.DisplayName, StringComparer.CurrentCulture)
                .Select(user => new RoleMemberDto(user.Id.Value, user.Username, user.DisplayName, user.IsActive))
                .ToList();

        return Result.Success(members);
    }
}

public sealed class ExplainPermissionsHandler(IDmsAuthorizer authorizer, AclAdministration administration, SubjectNames names)
    : IQueryHandler<ExplainPermissionsQuery, Result<IReadOnlyList<EffectivePermissionDto>>>
{
    public async Task<Result<IReadOnlyList<EffectivePermissionDto>>> HandleAsync(
        ExplainPermissionsQuery query,
        CancellationToken cancellationToken)
    {
        var resource = new ResourceRef(query.ResourceType, query.ResourceId);
        var decision = await administration.RequireManageAsync(resource, "explain", cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<IReadOnlyList<EffectivePermissionDto>>(
                AuthorizationErrors.Forbidden(decision.Explanation));
        }

        var evaluated = new List<(string Code, AuthorizationDecision Decision)>();
        foreach (var definition in PermissionCatalog.All.Where(p => p.Scope != PermissionScope.System))
        {
            evaluated.Add((definition.Code, await authorizer.AuthorizeAsync(
                new UserId(query.UserId),
                definition.Code,
                resource,
                cancellationToken)));
        }

        var subjectNames = await names.ResolveAsync(
            evaluated.Where(item => item.Decision.Source is not null).Select(item => (item.Decision.Source!.SubjectType, item.Decision.Source.SubjectId)),
            cancellationToken);

        IReadOnlyList<EffectivePermissionDto> results = evaluated
            .Select(item => new EffectivePermissionDto(
                item.Code,
                item.Decision.Allowed,
                item.Decision.Reason.ToString(),
                item.Decision.Explanation,
                item.Decision.Source is { } source
                    ? new DecisionSourceDto(
                        source.Resource.Type.ToString(),
                        source.Resource.Id,
                        source.SubjectType.ToString(),
                        source.SubjectId,
                        subjectNames.GetValueOrDefault((source.SubjectType, source.SubjectId)),
                        source.Effect.ToString())
                    : null))
            .ToList();

        return Result.Success(results);
    }
}

public sealed class GetMyPermissionsHandler(IDmsAuthorizer authorizer, ICurrentUser currentUser)
    : IQueryHandler<GetMyPermissionsQuery, Result<IReadOnlyList<string>>>
{
    public async Task<Result<IReadOnlyList<string>>> HandleAsync(
        GetMyPermissionsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<IReadOnlyList<string>>(AuthorizationErrors.Unauthenticated);
        }

        var principal = await authorizer.GetPrincipalsAsync(userId, cancellationToken);
        IReadOnlyList<string> permissions = principal.IsSystemAdmin
            ? PermissionCatalog.All
                .Where(definition => definition.Scope != PermissionScope.Resource)
                .Select(definition => definition.Code)
                .ToList()
            : principal.SystemPermissions.OrderBy(code => code).ToList();

        return Result.Success(permissions);
    }
}
