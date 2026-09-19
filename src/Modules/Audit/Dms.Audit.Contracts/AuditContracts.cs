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

    public const string UserCreated = "USER_CREATED";
    public const string UserUpdated = "USER_UPDATED";
    public const string UserActivated = "USER_ACTIVATED";
    public const string UserDeactivated = "USER_DEACTIVATED";
    public const string UserPasswordChanged = "USER_PASSWORD_CHANGED";

    public const string GroupCreated = "GROUP_CREATED";
    public const string GroupMemberAdded = "GROUP_MEMBER_ADDED";
    public const string GroupMemberRemoved = "GROUP_MEMBER_REMOVED";

    public const string RoleCreated = "ROLE_CREATED";
    public const string RolePermissionsChanged = "ROLE_PERMISSIONS_CHANGED";
    public const string RoleAssigned = "ROLE_ASSIGNED";
    public const string RoleUnassigned = "ROLE_UNASSIGNED";

    public const string PermissionGranted = "PERMISSION_GRANTED";
    public const string PermissionRevoked = "PERMISSION_REVOKED";

    /// <summary>Written whenever a system administrator uses the ACL bypass (decision D5).</summary>
    public const string AdminPermissionOverride = "ADMIN_PERMISSION_OVERRIDE";
}
