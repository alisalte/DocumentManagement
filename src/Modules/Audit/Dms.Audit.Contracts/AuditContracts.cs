using Dms.SharedKernel;

namespace Dms.Audit.Contracts;

public enum AuditActorType
{
    User,
    ShareLink,
    System,
    Anonymous,
}

public enum AuditOutcome
{
    Success,
    Denied,
    Failed,
}

/// <summary>
/// One audit event. The writer fills in actor, IP, user agent, correlation id and timestamp, so
/// callers only describe what happened.
/// </summary>
public sealed record AuditRecord
{
    public required string Action { get; init; }

    public AuditOutcome Outcome { get; init; } = AuditOutcome.Success;

    public AuditActorType ActorType { get; init; } = AuditActorType.User;

    /// <summary>Overrides the ambient caller; used when the actor is not the authenticated user.</summary>
    public UserId? UserId { get; init; }

    public string? EntityType { get; init; }

    public Guid? EntityId { get; init; }

    public Guid? DocumentId { get; init; }

    public Guid? VersionId { get; init; }

    public Guid? ShareLinkId { get; init; }

    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
}

/// <summary>
/// Appends to the audit log. The write joins the caller's transaction, so an audited change and its
/// audit row commit together. Audit rows can never be updated or deleted by the application role.
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditRecord record, CancellationToken cancellationToken);
}

/// <summary>Action codes. Phase 1 covers identity, authorization and administration events.</summary>
public static class AuditActions
{
    public const string Login = "LOGIN";
    public const string LoginFailed = "LOGIN_FAILED";
    public const string Logout = "LOGOUT";
    public const string TokenRefreshed = "TOKEN_REFRESHED";
    public const string TokenReuseDetected = "TOKEN_REUSE_DETECTED";

    public const string AccessDenied = "ACCESS_DENIED";

    public const string DocumentCreated = "DOCUMENT_CREATED";
    public const string DocumentViewed = "DOCUMENT_VIEWED";
    public const string DocumentDownloaded = "DOCUMENT_DOWNLOADED";
    public const string DocumentUpdated = "DOCUMENT_UPDATED";
    public const string DocumentDeleted = "DOCUMENT_DELETED";
    public const string DocumentRestored = "DOCUMENT_RESTORED";
    public const string DocumentPurged = "DOCUMENT_PURGED";
    public const string DocumentTagsChanged = "DOCUMENT_TAGS_CHANGED";

    public const string RecordDeclared = "RECORD_DECLARED";
    public const string RecordStatusChanged = "RECORD_STATUS_CHANGED";
    public const string RecordClassCreated = "RECORD_CLASS_CREATED";
    public const string RecordClassUpdated = "RECORD_CLASS_UPDATED";
    public const string RecordSeriesCreated = "RECORD_SERIES_CREATED";
    public const string RecordSeriesUpdated = "RECORD_SERIES_UPDATED";

    public const string RetentionPolicyCreated = "RETENTION_POLICY_CREATED";
    public const string RetentionPolicyUpdated = "RETENTION_POLICY_UPDATED";
    public const string RetentionAssigned = "RETENTION_ASSIGNED";
    public const string RetentionExceptionSet = "RETENTION_EXCEPTION_SET";
    public const string RetentionExceptionCleared = "RETENTION_EXCEPTION_CLEARED";

    public const string LegalHoldPlaced = "LEGAL_HOLD_PLACED";
    public const string LegalHoldReleased = "LEGAL_HOLD_RELEASED";

    public const string DispositionRequested = "DISPOSITION_REQUESTED";
    public const string DispositionApproved = "DISPOSITION_APPROVED";
    public const string DispositionRejected = "DISPOSITION_REJECTED";
    public const string DispositionBlockedByLegalHold = "DISPOSITION_BLOCKED_BY_LEGAL_HOLD";
    public const string DestructionAttempted = "DESTRUCTION_ATTEMPTED";
    public const string DestructionBlocked = "DESTRUCTION_BLOCKED";
    public const string RecordDestroyed = "RECORD_DESTROYED";
    public const string CertificateCreated = "CERTIFICATE_CREATED";

    public const string ImportCreated = "IMPORT_CREATED";
    public const string ImportValidationStarted = "IMPORT_VALIDATION_STARTED";
    public const string ImportValidationCompleted = "IMPORT_VALIDATION_COMPLETED";
    public const string ImportStarted = "IMPORT_STARTED";
    public const string ImportItemImported = "IMPORT_ITEM_IMPORTED";
    public const string ImportItemSkipped = "IMPORT_ITEM_SKIPPED";
    public const string ImportItemFailed = "IMPORT_ITEM_FAILED";
    public const string ImportCompleted = "IMPORT_COMPLETED";
    public const string ImportCompletedWithErrors = "IMPORT_COMPLETED_WITH_ERRORS";
    public const string ImportFailed = "IMPORT_FAILED";
    public const string ImportResumed = "IMPORT_RESUMED";
    public const string ImportReportAccessed = "IMPORT_REPORT_ACCESSED";

    /// <summary>A new file version, V(n+1).1 (ADR 0001).</summary>
    public const string VersionCreated = "VERSION_CREATED";

    /// <summary>A metadata-only revision, V(n).(r+1), reusing the same file (ADR 0001).</summary>
    public const string RevisionCreated = "REVISION_CREATED";

    public const string DocumentTypeCreated = "DOCUMENT_TYPE_CREATED";
    public const string DocumentTypeUpdated = "DOCUMENT_TYPE_UPDATED";
    public const string DocumentTypeDraftSaved = "DOCUMENT_TYPE_DRAFT_SAVED";
    public const string DocumentTypePublished = "DOCUMENT_TYPE_PUBLISHED";

    /// <summary>A metadata edit applied to the latest revision in place (MetadataEditPolicy.InPlace), with a diff.</summary>
    public const string MetadataUpdatedInPlace = "METADATA_UPDATED_IN_PLACE";

    public const string WorkflowCreated = "WORKFLOW_CREATED";
    public const string WorkflowDraftSaved = "WORKFLOW_DRAFT_SAVED";
    public const string WorkflowPublished = "WORKFLOW_PUBLISHED";
    public const string WorkflowStarted = "WORKFLOW_STARTED";
    public const string WorkflowApproved = "WORKFLOW_APPROVED";
    public const string WorkflowRejected = "WORKFLOW_REJECTED";
    public const string WorkflowReturned = "WORKFLOW_RETURNED";
    public const string WorkflowRequestedChanges = "WORKFLOW_REQUESTED_CHANGES";
    public const string WorkflowForwarded = "WORKFLOW_FORWARDED";
    public const string WorkflowCancelled = "WORKFLOW_CANCELLED";
    public const string WorkflowCompleted = "WORKFLOW_COMPLETED";
    public const string WorkflowNeedsAttention = "WORKFLOW_NEEDS_ATTENTION";
    public const string WorkflowTaskOverdue = "WORKFLOW_TASK_OVERDUE";

    public const string DocumentPrinted = "DOCUMENT_PRINTED";
    public const string FileInfected = "FILE_INFECTED";
    public const string SearchReindexStarted = "SEARCH_REINDEX_STARTED";

    public const string CategoryCreated = "CATEGORY_CREATED";
    public const string CategoryUpdated = "CATEGORY_UPDATED";
    public const string CategoryMoved = "CATEGORY_MOVED";

    public const string UserCreated = "USER_CREATED";
    public const string UserUpdated = "USER_UPDATED";
    public const string UserActivated = "USER_ACTIVATED";
    public const string UserDeactivated = "USER_DEACTIVATED";
    public const string UserPasswordChanged = "USER_PASSWORD_CHANGED";

    /// <summary>An administrator set someone else's password.</summary>
    public const string UserPasswordReset = "USER_PASSWORD_RESET";
    public const string UserAdminGranted = "USER_ADMIN_GRANTED";
    public const string UserAdminRevoked = "USER_ADMIN_REVOKED";

    public const string GroupCreated = "GROUP_CREATED";
    public const string GroupUpdated = "GROUP_UPDATED";
    public const string GroupMemberAdded = "GROUP_MEMBER_ADDED";
    public const string GroupMemberRemoved = "GROUP_MEMBER_REMOVED";

    public const string RoleCreated = "ROLE_CREATED";
    public const string RoleUpdated = "ROLE_UPDATED";
    public const string RolePermissionsChanged = "ROLE_PERMISSIONS_CHANGED";
    public const string RoleAssigned = "ROLE_ASSIGNED";
    public const string RoleUnassigned = "ROLE_UNASSIGNED";

    /// <summary>An internal share or an external link was created (the metadata says which).</summary>
    public const string DocumentShared = "DOCUMENT_SHARED";
    public const string ShareRevoked = "SHARE_REVOKED";

    /// <summary>An external link was opened (or refused, with outcome DENIED); the actor is the link.</summary>
    public const string ShareLinkAccessed = "SHARE_LINK_ACCESSED";
    public const string ShareLinkPasswordFailed = "SHARE_LINK_PASSWORD_FAILED";

    public const string PermissionGranted = "PERMISSION_GRANTED";
    public const string PermissionRevoked = "PERMISSION_REVOKED";

    /// <summary>The seal chain was checked; outcome FAILED means tampering (or a lost key) was found.</summary>
    public const string AuditSealsVerified = "AUDIT_SEALS_VERIFIED";

    /// <summary>Audit rows were exported; the metadata holds the filters and the format.</summary>
    public const string AuditExported = "AUDIT_EXPORTED";

    /// <summary>Written whenever a system administrator uses the ACL bypass (decision D5).</summary>
    public const string AdminPermissionOverride = "ADMIN_PERMISSION_OVERRIDE";
}
