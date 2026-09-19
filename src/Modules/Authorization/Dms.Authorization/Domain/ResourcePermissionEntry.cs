using Dms.Authorization.Contracts;
using Dms.SharedKernel;

namespace Dms.Authorization.Domain;

/// <summary>
/// One ACL entry: a subject (user, group or role) is allowed or denied one permission on one
/// resource. Revoking deletes the row; the audit log keeps the history.
/// </summary>
public sealed class ResourcePermissionEntry : AggregateRoot<AclEntryId>
{
    private ResourcePermissionEntry()
    {
    }

    private ResourcePermissionEntry(
        AclEntryId id,
        ResourceType resourceType,
        Guid resourceId,
        SubjectType subjectType,
        Guid subjectId,
        string permissionCode,
        PermissionEffect effect,
        bool inherit,
        string? reason,
        UserId createdBy,
        DateTimeOffset now)
        : base(id)
    {
        ResourceType = resourceType;
        ResourceId = resourceId;
        SubjectType = subjectType;
        SubjectId = subjectId;
        PermissionCode = permissionCode;
        Effect = effect;
        Inherit = inherit;
        Reason = reason;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public ResourceType ResourceType { get; private set; }

    public Guid ResourceId { get; private set; }

    public SubjectType SubjectType { get; private set; }

    public Guid SubjectId { get; private set; }

    public string PermissionCode { get; private set; } = string.Empty;

    public PermissionEffect Effect { get; private set; }

    /// <summary>Only meaningful on a category: extends the entry to sub-categories and their documents.</summary>
    public bool Inherit { get; private set; }

    public string? Reason { get; private set; }

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<ResourcePermissionEntry> Create(
        ResourceRef resource,
        SubjectType subjectType,
        Guid subjectId,
        string permissionCode,
        PermissionEffect effect,
        bool inherit,
        string? reason,
        UserId createdBy,
        DateTimeOffset now)
    {
        if (PermissionCatalog.Find(permissionCode) is null)
        {
            return Result.Failure<ResourcePermissionEntry>(
                Error.Validation("permission.unknown", $"Unknown permission '{permissionCode}'."));
        }

        if (!PermissionCatalog.IsResourceGrantable(permissionCode))
        {
            return Result.Failure<ResourcePermissionEntry>(Error.Validation(
                "permission.not_resource_scoped",
                $"'{permissionCode}' is a system permission and must be granted through a role."));
        }

        if (inherit && resource.Type != ResourceType.Category)
        {
            return Result.Failure<ResourcePermissionEntry>(Error.Validation(
                "permission.inherit_not_allowed",
                "Inheritance can only be set on a category entry."));
        }

        if (subjectId == Guid.Empty)
        {
            return Result.Failure<ResourcePermissionEntry>(
                Error.Validation("permission.subject_required", "A subject is required."));
        }

        return new ResourcePermissionEntry(
            AclEntryId.New(),
            resource.Type,
            resource.Id,
            subjectType,
            subjectId,
            permissionCode,
            effect,
            inherit,
            reason,
            createdBy,
            now);
    }

    public AclEntry ToAclEntry() => new(
        new ResourceRef(ResourceType, ResourceId),
        SubjectType,
        SubjectId,
        PermissionCode,
        Effect,
        Inherit);
}
