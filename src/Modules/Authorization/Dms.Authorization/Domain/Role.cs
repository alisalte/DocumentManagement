using Dms.Authorization.Contracts;
using Dms.SharedKernel;

namespace Dms.Authorization.Domain;

/// <summary>
/// A named bundle of system permissions. Resource access is never granted here: a role becomes
/// relevant to a document or category only by being the subject of an ACL entry.
/// </summary>
public sealed class Role : AggregateRoot<RoleId>
{
    private readonly List<RolePermission> _permissions = [];

    private Role()
    {
    }

    private Role(RoleId id, string code, string name, string? description, bool isSystem, DateTimeOffset now)
        : base(id)
    {
        Code = code;
        Name = name;
        Description = description;
        IsSystem = isSystem;
        CreatedAt = now;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Built in roles cannot be deleted; they may still be edited by an administrator.</summary>
    public bool IsSystem { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<RolePermission> Permissions => _permissions;

    public static Role Create(string code, string name, string? description, bool isSystem, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Role(RoleId.New(), code.Trim().ToUpperInvariant(), name.Trim(), description, isSystem, now);
    }

    /// <summary>
    /// Roles carry system permissions only. Granting DOCUMENT_VIEW globally through a role would
    /// quietly defeat the ACL, so it is rejected here rather than in a controller.
    /// </summary>
    public Result Grant(string permissionCode)
    {
        if (PermissionCatalog.Find(permissionCode) is null)
        {
            return Result.Failure(Error.Validation("permission.unknown", $"Unknown permission '{permissionCode}'."));
        }

        if (!PermissionCatalog.IsSystemGrantable(permissionCode))
        {
            return Result.Failure(Error.Validation(
                "permission.not_system_scoped",
                $"'{permissionCode}' is a resource permission and must be granted through an ACL entry."));
        }

        if (_permissions.All(permission => permission.PermissionCode != permissionCode))
        {
            _permissions.Add(new RolePermission(Id, permissionCode));
        }

        return Result.Success();
    }

    public void Revoke(string permissionCode) =>
        _permissions.RemoveAll(permission => permission.PermissionCode == permissionCode);

    public void Rename(string name, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Description = description;
    }
}

public sealed class RolePermission
{
    private RolePermission()
    {
    }

    internal RolePermission(RoleId roleId, string permissionCode)
    {
        RoleId = roleId;
        PermissionCode = permissionCode;
    }

    public RoleId RoleId { get; private set; }

    public string PermissionCode { get; private set; } = string.Empty;
}

/// <summary>Assignment of a role to a user. Group-to-role mapping is deferred (decision D10).</summary>
public sealed class UserRoleAssignment
{
    private UserRoleAssignment()
    {
    }

    public UserRoleAssignment(UserId userId, RoleId roleId, UserId grantedBy, DateTimeOffset now)
    {
        UserId = userId;
        RoleId = roleId;
        GrantedBy = grantedBy;
        GrantedAt = now;
    }

    public UserId UserId { get; private set; }

    public RoleId RoleId { get; private set; }

    public UserId GrantedBy { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }
}
