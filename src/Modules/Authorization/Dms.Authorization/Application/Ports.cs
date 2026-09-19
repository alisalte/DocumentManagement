using Dms.Authorization.Contracts;
using Dms.Authorization.Domain;
using Dms.SharedKernel;

namespace Dms.Authorization.Application;

public readonly record struct SubjectRef(SubjectType Type, Guid Id);

public interface IRoleRepository
{
    Task<Role?> FindAsync(RoleId roleId, CancellationToken cancellationToken);

    Task<Role?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlySet<string>> GetPermissionCodesAsync(
        IReadOnlyCollection<RoleId> roleIds,
        CancellationToken cancellationToken);

    void Add(Role role);
}

public interface IUserRoleRepository
{
    Task<IReadOnlySet<RoleId>> GetRoleIdsAsync(UserId userId, CancellationToken cancellationToken);

    Task<UserRoleAssignment?> FindAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken);

    void Add(UserRoleAssignment assignment);

    void Remove(UserRoleAssignment assignment);
}

public interface IResourcePermissionRepository
{
    Task<IReadOnlyList<ResourcePermissionEntry>> GetForResourcesAsync(
        IReadOnlyCollection<ResourceRef> resources,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ResourcePermissionEntry>> GetForSubjectsAsync(
        IReadOnlyCollection<SubjectRef> subjects,
        string permissionCode,
        CancellationToken cancellationToken);

    Task<ResourcePermissionEntry?> FindAsync(AclEntryId id, CancellationToken cancellationToken);

    Task<ResourcePermissionEntry?> FindDuplicateAsync(
        ResourceRef resource,
        SubjectType subjectType,
        Guid subjectId,
        string permissionCode,
        CancellationToken cancellationToken);

    void Add(ResourcePermissionEntry entry);

    void Remove(ResourcePermissionEntry entry);
}
