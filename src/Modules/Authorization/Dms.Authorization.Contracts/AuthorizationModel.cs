using Dms.SharedKernel;

namespace Dms.Authorization.Contracts;

public enum ResourceType
{
    Category,
    Document,
}

public enum SubjectType
{
    User,
    Group,
    Role,
}

public enum PermissionEffect
{
    Allow,
    Deny,
}

/// <summary>Whether the version being accessed has completed its approval workflow (decision D6).</summary>
public enum ContentState
{
    Published,
    Draft,
}

/// <summary>
/// Malware scan gate (decision D9). Content permissions are refused until the scan reports Clean,
/// including for the uploader, who can still see the scan status through metadata endpoints.
/// </summary>
public enum ContentScanState
{
    NotApplicable,
    Pending,
    Clean,
    Infected,
    Failed,
}

public readonly record struct ResourceRef(ResourceType Type, Guid Id)
{
    public static ResourceRef Category(Guid id) => new(ResourceType.Category, id);

    public static ResourceRef Document(Guid id) => new(ResourceType.Document, id);

    public override string ToString() => $"{Type}:{Id}";
}

/// <param name="AncestorCategoryIds">
/// Ordered nearest first. For a document this starts with its own category; for a category it
/// starts with its parent.
/// </param>
/// <param name="VersionId">
/// The version the question is about, when it is about one. Null for questions about the document
/// as a whole, which version-pinned grants (shares) never answer.
/// </param>
public sealed record ResourceDescriptor(
    ResourceRef Resource,
    IReadOnlyList<Guid> AncestorCategoryIds,
    bool IsSoftDeleted = false,
    ContentState ContentState = ContentState.Published,
    ContentScanState ScanState = ContentScanState.NotApplicable,
    UserId? AuthorId = null,
    Guid? VersionId = null);

public sealed record AclEntry(
    ResourceRef Resource,
    SubjectType SubjectType,
    Guid SubjectId,
    string PermissionCode,
    PermissionEffect Effect,
    bool Inherit);

public enum TemporaryGrantKind
{
    Share,
    WorkflowTask,
}

/// <param name="VersionId">
/// Pins the grant to one version: it then answers only questions about that version. A share is
/// always pinned (decision D8); a workflow task grant is not.
/// </param>
/// <param name="GrantedBy">
/// Whose rights the grant was carved out of. The authorizer re-checks that user on every use and
/// drops the grant when they no longer hold the permission themselves (decision D8).
/// </param>
public sealed record TemporaryGrant(
    TemporaryGrantKind Kind,
    ResourceRef Resource,
    string PermissionCode,
    DateTimeOffset? ExpiresAt = null,
    Guid? SourceId = null,
    Guid? VersionId = null,
    UserId? GrantedBy = null);

public sealed record PrincipalSet(
    UserId UserId,
    bool IsActive,
    bool IsSystemAdmin,
    IReadOnlySet<GroupId> GroupIds,
    IReadOnlySet<RoleId> RoleIds,
    IReadOnlySet<string> SystemPermissions)
{
    public static PrincipalSet Anonymous(UserId userId) => new(
        userId,
        IsActive: false,
        IsSystemAdmin: false,
        new HashSet<GroupId>(),
        new HashSet<RoleId>(),
        new HashSet<string>(StringComparer.Ordinal));

    /// <summary>True when the ACL subject matches this user, one of their groups or one of their roles.</summary>
    public bool Matches(SubjectType subjectType, Guid subjectId) => subjectType switch
    {
        SubjectType.User => subjectId == UserId.Value,
        SubjectType.Group => GroupIds.Contains(new GroupId(subjectId)),
        SubjectType.Role => RoleIds.Contains(new RoleId(subjectId)),
        _ => false,
    };
}

public enum DecisionReason
{
    AllowedByAcl,
    AllowedByInheritedAcl,
    AllowedByShare,
    AllowedByWorkflowTask,
    AllowedByAuthorship,
    AllowedBySystemRole,
    AllowedBySystemAdministrator,
    DeniedByDefault,
    DeniedByExplicitDeny,
    DeniedUserInactive,
    DeniedResourceDeleted,
    DeniedRequiresView,
    DeniedDraft,
    DeniedScanIncomplete,
    DeniedUnknownPermission,
    DeniedWrongScope,
    DeniedUnknownResource,
}

public sealed record AuthorizationDecision(bool Allowed, DecisionReason Reason, string Explanation)
{
    public static AuthorizationDecision Allow(DecisionReason reason, string explanation) =>
        new(true, reason, explanation);

    public static AuthorizationDecision Deny(DecisionReason reason, string explanation) =>
        new(false, reason, explanation);
}

/// <summary>
/// The set of resources a user may act on, used to filter listings and search queries without
/// evaluating each row. Produced from exactly the same ACL rules as a single decision.
/// </summary>
public sealed record AccessScope(
    bool IsSystemAdmin,
    IReadOnlySet<Guid> AllowedCategories,
    IReadOnlySet<Guid> DeniedCategories,
    IReadOnlySet<Guid> AllowedResources,
    IReadOnlySet<Guid> DeniedResources)
{
    public static AccessScope Empty { get; } = new(
        false,
        new HashSet<Guid>(),
        new HashSet<Guid>(),
        new HashSet<Guid>(),
        new HashSet<Guid>());

    /// <summary>Mirrors the SQL/OpenSearch filter: allow somewhere, denied nowhere.</summary>
    public bool Includes(Guid resourceId, Guid? categoryId) =>
        !DeniedResources.Contains(resourceId)
        && (categoryId is null || !DeniedCategories.Contains(categoryId.Value))
        && (AllowedResources.Contains(resourceId)
            || (categoryId is not null && AllowedCategories.Contains(categoryId.Value)));
}
