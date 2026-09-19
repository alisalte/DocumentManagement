using Dms.Authorization.Contracts;
using Dms.SharedKernel;

namespace Dms.Authorization.UnitTests;

/// <summary>Builders that keep the permission tests readable.</summary>
internal static class Scenario
{
    public static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    public static readonly UserId Alice = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"));
    public static readonly UserId Bob = new(Guid.Parse("00000000-0000-0000-0000-0000000000b2"));
    public static readonly GroupId Maintenance = new(Guid.Parse("00000000-0000-0000-0000-0000000000c3"));
    public static readonly RoleId Reviewers = new(Guid.Parse("00000000-0000-0000-0000-0000000000d4"));

    public static readonly Guid RootCategory = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid MaintenanceCategory = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public static readonly Guid ContractsCategory = Guid.Parse("00000000-0000-0000-0000-000000000003");
    public static readonly Guid DocumentId = Guid.Parse("00000000-0000-0000-0000-000000000009");

    public static PrincipalSet User(
        bool isActive = true,
        bool isSystemAdmin = false,
        IEnumerable<GroupId>? groups = null,
        IEnumerable<RoleId>? roles = null,
        IEnumerable<string>? systemPermissions = null,
        UserId? userId = null) =>
        new(
            userId ?? Alice,
            isActive,
            isSystemAdmin,
            (groups ?? []).ToHashSet(),
            (roles ?? []).ToHashSet(),
            (systemPermissions ?? []).ToHashSet(StringComparer.Ordinal));

    /// <summary>A document inside Contracts &lt; Maintenance &lt; Root.</summary>
    public static ResourceDescriptor Document(
        bool deleted = false,
        ContentState state = ContentState.Published,
        ContentScanState scan = ContentScanState.Clean,
        UserId? author = null) =>
        new(
            ResourceRef.Document(DocumentId),
            [ContractsCategory, MaintenanceCategory, RootCategory],
            deleted,
            state,
            scan,
            author);

    public static AclEntry Allow(
        string permission,
        Guid? categoryId = null,
        SubjectType subjectType = SubjectType.User,
        Guid? subjectId = null,
        bool inherit = true) =>
        Entry(permission, PermissionEffect.Allow, categoryId, subjectType, subjectId, inherit);

    public static AclEntry Deny(
        string permission,
        Guid? categoryId = null,
        SubjectType subjectType = SubjectType.User,
        Guid? subjectId = null,
        bool inherit = true) =>
        Entry(permission, PermissionEffect.Deny, categoryId, subjectType, subjectId, inherit);

    private static AclEntry Entry(
        string permission,
        PermissionEffect effect,
        Guid? categoryId,
        SubjectType subjectType,
        Guid? subjectId,
        bool inherit)
    {
        var resource = categoryId is { } id ? ResourceRef.Category(id) : ResourceRef.Document(DocumentId);
        var subject = subjectId ?? subjectType switch
        {
            SubjectType.User => Alice.Value,
            SubjectType.Group => Maintenance.Value,
            _ => Reviewers.Value,
        };

        return new AclEntry(resource, subjectType, subject, permission, effect, categoryId is not null && inherit);
    }
}
