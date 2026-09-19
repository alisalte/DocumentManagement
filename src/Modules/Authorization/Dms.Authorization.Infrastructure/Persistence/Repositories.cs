using Dms.Authorization.Application;
using Dms.Authorization.Contracts;
using Dms.Authorization.Domain;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Dms.Authorization.Infrastructure.Persistence;

public sealed class RoleRepository(AuthorizationDbContext context) : IRoleRepository
{
    public Task<Role?> FindAsync(RoleId roleId, CancellationToken cancellationToken) =>
        context.Roles.FirstOrDefaultAsync(role => role.Id == roleId, cancellationToken);

    public Task<Role?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        context.Roles.FirstOrDefaultAsync(role => role.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken) =>
        await context.Roles.AsNoTracking().OrderBy(role => role.Code).ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<string>> GetPermissionCodesAsync(
        IReadOnlyCollection<RoleId> roleIds,
        CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var codes = await context.Roles.AsNoTracking()
            .Where(role => roleIds.Contains(role.Id))
            .SelectMany(role => role.Permissions)
            .Select(permission => permission.PermissionCode)
            .Distinct()
            .ToListAsync(cancellationToken);

        return codes.ToHashSet(StringComparer.Ordinal);
    }

    public void Add(Role role) => context.Roles.Add(role);
}

public sealed class UserRoleRepository(AuthorizationDbContext context) : IUserRoleRepository
{
    public async Task<IReadOnlySet<RoleId>> GetRoleIdsAsync(UserId userId, CancellationToken cancellationToken)
    {
        var ids = await context.UserRoles.AsNoTracking()
            .Where(assignment => assignment.UserId == userId)
            .Select(assignment => assignment.RoleId)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    public Task<UserRoleAssignment?> FindAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken) =>
        context.UserRoles.FirstOrDefaultAsync(
            assignment => assignment.UserId == userId && assignment.RoleId == roleId,
            cancellationToken);

    public void Add(UserRoleAssignment assignment) => context.UserRoles.Add(assignment);

    public void Remove(UserRoleAssignment assignment) => context.UserRoles.Remove(assignment);
}

public sealed class ResourcePermissionRepository(AuthorizationDbContext context) : IResourcePermissionRepository
{
    public async Task<IReadOnlyList<ResourcePermissionEntry>> GetForResourcesAsync(
        IReadOnlyCollection<ResourceRef> resources,
        CancellationToken cancellationToken)
    {
        if (resources.Count == 0)
        {
            return [];
        }

        // Split by type so the query stays sargable on (resource_type, resource_id).
        var documentIds = resources
            .Where(resource => resource.Type == ResourceType.Document)
            .Select(resource => resource.Id)
            .ToArray();

        var categoryIds = resources
            .Where(resource => resource.Type == ResourceType.Category)
            .Select(resource => resource.Id)
            .ToArray();

        return await context.ResourcePermissions.AsNoTracking()
            .Where(acl =>
                (acl.ResourceType == ResourceType.Document && documentIds.Contains(acl.ResourceId))
                || (acl.ResourceType == ResourceType.Category && categoryIds.Contains(acl.ResourceId)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ResourcePermissionEntry>> GetForSubjectsAsync(
        IReadOnlyCollection<SubjectRef> subjects,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        if (subjects.Count == 0)
        {
            return [];
        }

        var userIds = subjects.Where(s => s.Type == SubjectType.User).Select(s => s.Id).ToArray();
        var groupIds = subjects.Where(s => s.Type == SubjectType.Group).Select(s => s.Id).ToArray();
        var roleIds = subjects.Where(s => s.Type == SubjectType.Role).Select(s => s.Id).ToArray();

        return await context.ResourcePermissions.AsNoTracking()
            .Where(acl => acl.PermissionCode == permissionCode)
            .Where(acl =>
                (acl.SubjectType == SubjectType.User && userIds.Contains(acl.SubjectId))
                || (acl.SubjectType == SubjectType.Group && groupIds.Contains(acl.SubjectId))
                || (acl.SubjectType == SubjectType.Role && roleIds.Contains(acl.SubjectId)))
            .ToListAsync(cancellationToken);
    }

    public Task<ResourcePermissionEntry?> FindAsync(AclEntryId id, CancellationToken cancellationToken) =>
        context.ResourcePermissions.FirstOrDefaultAsync(acl => acl.Id == id, cancellationToken);

    public Task<ResourcePermissionEntry?> FindDuplicateAsync(
        ResourceRef resource,
        SubjectType subjectType,
        Guid subjectId,
        string permissionCode,
        CancellationToken cancellationToken) =>
        context.ResourcePermissions.FirstOrDefaultAsync(
            acl => acl.ResourceType == resource.Type
                && acl.ResourceId == resource.Id
                && acl.SubjectType == subjectType
                && acl.SubjectId == subjectId
                && acl.PermissionCode == permissionCode,
            cancellationToken);

    public void Add(ResourcePermissionEntry entry) => context.ResourcePermissions.Add(entry);

    public void Remove(ResourcePermissionEntry entry) => context.ResourcePermissions.Remove(entry);
}
