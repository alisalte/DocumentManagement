namespace Dms.Authorization.Contracts;

public enum PermissionScope
{
    /// <summary>Granted only through ACL entries on a category or document.</summary>
    Resource,

    /// <summary>Granted only through roles.</summary>
    System,

    /// <summary>Grantable both ways: a role grant is global, an ACL grant is scoped to the resource.</summary>
    Both,
}

/// <param name="RequiresView">
/// When true the permission is only effective if DOCUMENT_VIEW is also allowed on the same
/// resource. This is what makes an explicit DENY on VIEW block everything else.
/// </param>
public sealed record PermissionDefinition(
    string Code,
    PermissionScope Scope,
    bool RequiresView,
    string Description);

/// <summary>
/// Permission codes. The catalog is defined in code, not by administrators: a permission that no
/// code path checks would be a lie. The migrator mirrors this list into <c>authz.permissions</c>.
/// </summary>
public static class PermissionCodes
{
    public const string DocumentView = "DOCUMENT_VIEW";
    public const string DocumentViewDraft = "DOCUMENT_VIEW_DRAFT";
    public const string DocumentDownload = "DOCUMENT_DOWNLOAD";
    public const string DocumentPrint = "DOCUMENT_PRINT";
    public const string DocumentCreate = "DOCUMENT_CREATE";
    public const string DocumentEdit = "DOCUMENT_EDIT";
    public const string DocumentDelete = "DOCUMENT_DELETE";
    public const string DocumentRestore = "DOCUMENT_RESTORE";
    public const string DocumentCreateVersion = "DOCUMENT_CREATE_VERSION";
    public const string DocumentShare = "DOCUMENT_SHARE";
    public const string DocumentShareExternal = "DOCUMENT_SHARE_EXTERNAL";
    public const string DocumentManagePermission = "DOCUMENT_MANAGE_PERMISSION";
    public const string DocumentExport = "DOCUMENT_EXPORT";

    public const string WorkflowView = "WORKFLOW_VIEW";
    public const string WorkflowApprove = "WORKFLOW_APPROVE";
    public const string WorkflowReject = "WORKFLOW_REJECT";
    public const string WorkflowReturn = "WORKFLOW_RETURN";
    public const string WorkflowRequestChanges = "WORKFLOW_REQUEST_CHANGES";

    public const string AuditView = "AUDIT_VIEW";
    public const string AuditExport = "AUDIT_EXPORT";

    public const string AdminManageUsers = "ADMIN_MANAGE_USERS";
    public const string AdminManageGroups = "ADMIN_MANAGE_GROUPS";
    public const string AdminManageRoles = "ADMIN_MANAGE_ROLES";
    public const string AdminManageDocumentTypes = "ADMIN_MANAGE_DOCUMENT_TYPES";
    public const string AdminManageWorkflows = "ADMIN_MANAGE_WORKFLOWS";
    public const string AdminManageCategories = "ADMIN_MANAGE_CATEGORIES";
    public const string DocumentPurge = "DOCUMENT_PURGE";
    public const string AdminManageSearch = "ADMIN_MANAGE_SEARCH";
}

public static class PermissionCatalog
{
    private static readonly Dictionary<string, PermissionDefinition> Index;

    static PermissionCatalog()
    {
        All =
        [
            new(PermissionCodes.DocumentView, PermissionScope.Resource, false, "See a document and its metadata."),
            new(PermissionCodes.DocumentViewDraft, PermissionScope.Resource, false, "See versions that are not published yet."),
            new(PermissionCodes.DocumentDownload, PermissionScope.Resource, true, "Download the original file."),
            new(PermissionCodes.DocumentPrint, PermissionScope.Resource, true, "Print through the application."),
            new(PermissionCodes.DocumentCreate, PermissionScope.Resource, false, "Create documents in a category."),
            new(PermissionCodes.DocumentEdit, PermissionScope.Resource, true, "Edit document metadata."),
            new(PermissionCodes.DocumentDelete, PermissionScope.Resource, false, "Soft delete a document."),
            new(PermissionCodes.DocumentRestore, PermissionScope.Resource, false, "Restore a soft deleted document."),
            new(PermissionCodes.DocumentCreateVersion, PermissionScope.Resource, true, "Add a new version."),
            new(PermissionCodes.DocumentShare, PermissionScope.Resource, true, "Share with another user."),
            new(PermissionCodes.DocumentShareExternal, PermissionScope.Resource, true, "Create an external share link."),
            new(PermissionCodes.DocumentManagePermission, PermissionScope.Resource, false, "Manage the ACL of a resource."),
            new(PermissionCodes.DocumentExport, PermissionScope.Resource, true, "Export documents in bulk."),

            new(PermissionCodes.WorkflowView, PermissionScope.Resource, false, "See workflow state and history."),
            new(PermissionCodes.WorkflowApprove, PermissionScope.Resource, true, "Approve a workflow task."),
            new(PermissionCodes.WorkflowReject, PermissionScope.Resource, true, "Reject a workflow task."),
            new(PermissionCodes.WorkflowReturn, PermissionScope.Resource, true, "Return a workflow task to an earlier step."),
            new(PermissionCodes.WorkflowRequestChanges, PermissionScope.Resource, true, "Request changes on a workflow task."),

            new(PermissionCodes.AuditView, PermissionScope.Both, false, "Read audit history."),
            new(PermissionCodes.AuditExport, PermissionScope.System, false, "Export audit history."),

            new(PermissionCodes.AdminManageUsers, PermissionScope.System, false, "Manage users."),
            new(PermissionCodes.AdminManageGroups, PermissionScope.System, false, "Manage groups."),
            new(PermissionCodes.AdminManageRoles, PermissionScope.System, false, "Manage roles and role permissions."),
            new(PermissionCodes.AdminManageDocumentTypes, PermissionScope.System, false, "Manage document types."),
            new(PermissionCodes.AdminManageWorkflows, PermissionScope.System, false, "Manage workflow definitions."),
            new(PermissionCodes.AdminManageCategories, PermissionScope.System, false, "Manage the category tree."),
            new(PermissionCodes.DocumentPurge, PermissionScope.System, false, "Permanently delete documents."),
            new(PermissionCodes.AdminManageSearch, PermissionScope.System, false, "See indexing status and rebuild the search index."),
        ];

        Index = All.ToDictionary(definition => definition.Code, StringComparer.Ordinal);
    }

    public static IReadOnlyList<PermissionDefinition> All { get; }

    /// <summary>
    /// The only resource permission a system administrator may exercise without an ACL entry.
    /// Without it an administrator could be locked out of a resource with no way back in.
    /// Every use is audited (decision D5).
    /// </summary>
    public static string AdminBypassPermission => PermissionCodes.DocumentManagePermission;

    public static PermissionDefinition? Find(string code) =>
        Index.TryGetValue(code, out var definition) ? definition : null;

    public static bool IsSystemGrantable(string code) =>
        Find(code) is { Scope: PermissionScope.System or PermissionScope.Both };

    public static bool IsResourceGrantable(string code) =>
        Find(code) is { Scope: PermissionScope.Resource or PermissionScope.Both };
}
