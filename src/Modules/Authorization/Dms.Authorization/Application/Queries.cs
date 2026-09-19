using Dms.Application;
using Dms.Authorization.Contracts;
using Dms.SharedKernel;

namespace Dms.Authorization.Application;

public sealed record RoleDto(Guid Id, string Code, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions);

public sealed record ResourcePermissionDto(
    Guid Id,
    string ResourceType,
    Guid ResourceId,
    string SubjectType,
    Guid SubjectId,
    string PermissionCode,
    string Effect,
    bool Inherit,
    string? Reason,
    DateTimeOffset CreatedAt);

public sealed record EffectivePermissionDto(string PermissionCode, bool Allowed, string Reason, string Explanation);

public sealed record ListRolesQuery : IQuery<Result<IReadOnlyList<RoleDto>>>;

public sealed record GetResourcePermissionsQuery(ResourceType ResourceType, Guid ResourceId)
    : IQuery<Result<IReadOnlyList<ResourcePermissionDto>>>;

/// <summary>
/// Answers "why can this user do this?" for administrators. Explaining the decision is part of the
/// product: a deny-wins model is only usable if the reason is visible.
/// </summary>
public sealed record ExplainPermissionsQuery(Guid UserId, ResourceType ResourceType, Guid ResourceId)
    : IQuery<Result<IReadOnlyList<EffectivePermissionDto>>>;

public sealed record GetMyPermissionsQuery : IQuery<Result<IReadOnlyList<string>>>;

public sealed class ListRolesHandler(IDmsAuthorizer authorizer, IRoleRepository roles)
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
    IDmsAuthorizer authorizer,
    IResourcePermissionRepository repository)
    : IQueryHandler<GetResourcePermissionsQuery, Result<IReadOnlyList<ResourcePermissionDto>>>
{
    public async Task<Result<IReadOnlyList<ResourcePermissionDto>>> HandleAsync(
        GetResourcePermissionsQuery query,
        CancellationToken cancellationToken)
    {
        var resource = new ResourceRef(query.ResourceType, query.ResourceId);
        var decision = await authorizer.AuthorizeAsync(
            PermissionCodes.DocumentManagePermission,
            resource,
            cancellationToken);

        if (!decision.Allowed)
        {
            return Result.Failure<IReadOnlyList<ResourcePermissionDto>>(
                AuthorizationErrors.Forbidden(decision.Explanation));
        }

        var entries = await repository.GetForResourcesAsync([resource], cancellationToken);
        IReadOnlyList<ResourcePermissionDto> result = entries
            .Where(entry => entry.ResourceId == query.ResourceId && entry.ResourceType == query.ResourceType)
            .Select(entry => new ResourcePermissionDto(
                entry.Id.Value,
                entry.ResourceType.ToString(),
                entry.ResourceId,
                entry.SubjectType.ToString(),
                entry.SubjectId,
                entry.PermissionCode,
                entry.Effect.ToString(),
                entry.Inherit,
                entry.Reason,
                entry.CreatedAt))
            .ToList();

        return Result.Success(result);
    }
}

public sealed class ExplainPermissionsHandler(IDmsAuthorizer authorizer)
    : IQueryHandler<ExplainPermissionsQuery, Result<IReadOnlyList<EffectivePermissionDto>>>
{
    public async Task<Result<IReadOnlyList<EffectivePermissionDto>>> HandleAsync(
        ExplainPermissionsQuery query,
        CancellationToken cancellationToken)
    {
        var resource = new ResourceRef(query.ResourceType, query.ResourceId);
        var decision = await authorizer.AuthorizeAsync(
            PermissionCodes.DocumentManagePermission,
            resource,
            cancellationToken);

        if (!decision.Allowed)
        {
            return Result.Failure<IReadOnlyList<EffectivePermissionDto>>(
                AuthorizationErrors.Forbidden(decision.Explanation));
        }

        var results = new List<EffectivePermissionDto>();
        foreach (var definition in PermissionCatalog.All.Where(p => p.Scope != PermissionScope.System))
        {
            var evaluated = await authorizer.AuthorizeAsync(
                new UserId(query.UserId),
                definition.Code,
                resource,
                cancellationToken);

            results.Add(new EffectivePermissionDto(
                definition.Code,
                evaluated.Allowed,
                evaluated.Reason.ToString(),
                evaluated.Explanation));
        }

        return Result.Success<IReadOnlyList<EffectivePermissionDto>>(results);
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
