using Dms.Authorization.Contracts;

namespace Dms.Authorization.Application;

/// <summary>
/// The deterministic permission resolution rules. Pure: no database, no clock, no services, so the
/// whole rule set is unit testable and there is exactly one place where the rules live.
///
/// Order (documented in docs/architecture.md section 5.4):
///   1. unknown permission / inactive user
///   2. system scope is answered by roles only
///   3. soft deleted resources expose only restore, purge and audit
///   4. explicit DENY anywhere beats every ALLOW, share and task grant
///   5. ALLOW on the resource or an inherited ALLOW from an ancestor category
///   6. otherwise share / workflow task grants
///   7. otherwise deny
///   8. dependent permissions additionally require VIEW
///   9. drafts require authorship, VIEW_DRAFT or a workflow task
///  10. content is refused until the malware scan reports clean
/// </summary>
public static class PermissionEvaluator
{
    /// <summary>Permissions that expose file content and therefore carry the draft and scan gates.</summary>
    private static readonly HashSet<string> ContentPermissions = new(StringComparer.Ordinal)
    {
        PermissionCodes.DocumentView,
        PermissionCodes.DocumentDownload,
        PermissionCodes.DocumentPrint,
        PermissionCodes.DocumentExport,
    };

    /// <summary>The only permissions that still apply to a soft deleted document.</summary>
    private static readonly HashSet<string> DeletedResourcePermissions = new(StringComparer.Ordinal)
    {
        PermissionCodes.DocumentRestore,
        PermissionCodes.DocumentPurge,
        PermissionCodes.AuditView,
    };

    public static AuthorizationDecision EvaluateSystem(PrincipalSet principal, string permissionCode)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (PermissionCatalog.Find(permissionCode) is null)
        {
            return AuthorizationDecision.Deny(
                DecisionReason.DeniedUnknownPermission,
                $"'{permissionCode}' is not a known permission.");
        }

        if (!principal.IsActive)
        {
            return AuthorizationDecision.Deny(DecisionReason.DeniedUserInactive, "The user account is not active.");
        }

        if (!PermissionCatalog.IsSystemGrantable(permissionCode))
        {
            return AuthorizationDecision.Deny(
                DecisionReason.DeniedWrongScope,
                $"'{permissionCode}' is a resource permission and cannot be granted through a role.");
        }

        if (principal.IsSystemAdmin)
        {
            return AuthorizationDecision.Allow(
                DecisionReason.AllowedBySystemAdministrator,
                "The user is a system administrator.");
        }

        return principal.SystemPermissions.Contains(permissionCode)
            ? AuthorizationDecision.Allow(DecisionReason.AllowedBySystemRole, $"A role grants '{permissionCode}'.")
            : AuthorizationDecision.Deny(DecisionReason.DeniedByDefault, $"No role grants '{permissionCode}'.");
    }

    public static AuthorizationDecision Evaluate(
        PrincipalSet principal,
        string permissionCode,
        ResourceDescriptor resource,
        IReadOnlyCollection<AclEntry> entries,
        IReadOnlyCollection<TemporaryGrant> grants,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(grants);

        var definition = PermissionCatalog.Find(permissionCode);
        if (definition is null)
        {
            return AuthorizationDecision.Deny(
                DecisionReason.DeniedUnknownPermission,
                $"'{permissionCode}' is not a known permission.");
        }

        if (!principal.IsActive)
        {
            return AuthorizationDecision.Deny(DecisionReason.DeniedUserInactive, "The user account is not active.");
        }

        if (!PermissionCatalog.IsResourceGrantable(permissionCode))
        {
            return AuthorizationDecision.Deny(
                DecisionReason.DeniedWrongScope,
                $"'{permissionCode}' is a system permission and is not evaluated against a resource.");
        }

        if (resource.IsSoftDeleted && !DeletedResourcePermissions.Contains(permissionCode))
        {
            return AuthorizationDecision.Deny(
                DecisionReason.DeniedResourceDeleted,
                "The resource is in the recycle bin.");
        }

        var decision = EvaluateSingle(principal, permissionCode, resource, entries, grants, now);
        if (!decision.Allowed)
        {
            return decision;
        }

        // Dependent permissions are useless without VIEW, and this is what makes a DENY on VIEW
        // block downloading, printing, editing and workflow actions in one stroke.
        if (definition.RequiresView)
        {
            var view = EvaluateSingle(principal, PermissionCodes.DocumentView, resource, entries, grants, now);
            if (!view.Allowed)
            {
                return AuthorizationDecision.Deny(
                    DecisionReason.DeniedRequiresView,
                    $"'{permissionCode}' also requires DOCUMENT_VIEW, which resolved to deny: {view.Explanation}");
            }
        }

        if (ContentPermissions.Contains(permissionCode))
        {
            var draftGate = EvaluateDraftGate(principal, resource, entries, grants, now);
            if (draftGate is not null)
            {
                return draftGate;
            }

            var scanGate = EvaluateScanGate(resource);
            if (scanGate is not null)
            {
                return scanGate;
            }
        }

        return decision;
    }

    private static AuthorizationDecision EvaluateSingle(
        PrincipalSet principal,
        string permissionCode,
        ResourceDescriptor resource,
        IReadOnlyCollection<AclEntry> entries,
        IReadOnlyCollection<TemporaryGrant> grants,
        DateTimeOffset now)
    {
        // A system administrator must never be locked out of permission management, otherwise a bad
        // ACL could make a resource unadministrable. This is the only ACL bypass and it is audited.
        if (principal.IsSystemAdmin && permissionCode == PermissionCatalog.AdminBypassPermission)
        {
            return AuthorizationDecision.Allow(
                DecisionReason.AllowedBySystemAdministrator,
                "System administrators may always manage permissions (audited).");
        }

        AclEntry? directAllow = null;
        AclEntry? inheritedAllow = null;

        foreach (var entry in entries)
        {
            if (!string.Equals(entry.PermissionCode, permissionCode, StringComparison.Ordinal))
            {
                continue;
            }

            if (!principal.Matches(entry.SubjectType, entry.SubjectId))
            {
                continue;
            }

            var applicability = GetApplicability(entry, resource);
            if (applicability == Applicability.NotApplicable)
            {
                continue;
            }

            if (entry.Effect == PermissionEffect.Deny)
            {
                return AuthorizationDecision.Deny(
                    DecisionReason.DeniedByExplicitDeny,
                    $"An explicit DENY of '{permissionCode}' applies from {entry.Resource} " +
                    $"for {entry.SubjectType}:{entry.SubjectId}.");
            }

            if (applicability == Applicability.Direct)
            {
                directAllow ??= entry;
            }
            else
            {
                inheritedAllow ??= entry;
            }
        }

        if (directAllow is not null)
        {
            return AuthorizationDecision.Allow(
                DecisionReason.AllowedByAcl,
                $"ALLOW of '{permissionCode}' on {directAllow.Resource} for " +
                $"{directAllow.SubjectType}:{directAllow.SubjectId}.");
        }

        if (inheritedAllow is not null)
        {
            return AuthorizationDecision.Allow(
                DecisionReason.AllowedByInheritedAcl,
                $"Inherited ALLOW of '{permissionCode}' from {inheritedAllow.Resource} for " +
                $"{inheritedAllow.SubjectType}:{inheritedAllow.SubjectId}.");
        }

        foreach (var grant in grants)
        {
            if (!string.Equals(grant.PermissionCode, permissionCode, StringComparison.Ordinal))
            {
                continue;
            }

            if (grant.Resource != resource.Resource || (grant.ExpiresAt is not null && grant.ExpiresAt <= now))
            {
                continue;
            }

            return grant.Kind == TemporaryGrantKind.Share
                ? AuthorizationDecision.Allow(DecisionReason.AllowedByShare, $"An active share grants '{permissionCode}'.")
                : AuthorizationDecision.Allow(
                    DecisionReason.AllowedByWorkflowTask,
                    $"An open workflow task grants '{permissionCode}'.");
        }

        return AuthorizationDecision.Deny(
            DecisionReason.DeniedByDefault,
            $"No rule grants '{permissionCode}' on {resource.Resource}.");
    }

    /// <summary>Decision D6: who may see a version that has not been published yet.</summary>
    private static AuthorizationDecision? EvaluateDraftGate(
        PrincipalSet principal,
        ResourceDescriptor resource,
        IReadOnlyCollection<AclEntry> entries,
        IReadOnlyCollection<TemporaryGrant> grants,
        DateTimeOffset now)
    {
        if (resource.ContentState != ContentState.Draft)
        {
            return null;
        }

        if (resource.AuthorId == principal.UserId)
        {
            return null;
        }

        if (EvaluateSingle(principal, PermissionCodes.DocumentViewDraft, resource, entries, grants, now).Allowed)
        {
            return null;
        }

        var hasTask = grants.Any(grant =>
            grant.Kind == TemporaryGrantKind.WorkflowTask
            && grant.Resource == resource.Resource
            && (grant.ExpiresAt is null || grant.ExpiresAt > now));

        return hasTask
            ? null
            : AuthorizationDecision.Deny(
                DecisionReason.DeniedDraft,
                "The version is a draft. Only its author, an assigned reviewer or a holder of " +
                "DOCUMENT_VIEW_DRAFT may see it.");
    }

    /// <summary>Decision D9: no content leaves the system before the malware scan passes.</summary>
    private static AuthorizationDecision? EvaluateScanGate(ResourceDescriptor resource) => resource.ScanState switch
    {
        ContentScanState.NotApplicable or ContentScanState.Clean => null,
        ContentScanState.Pending => AuthorizationDecision.Deny(
            DecisionReason.DeniedScanIncomplete,
            "The malware scan has not finished yet. The scan status is visible on the document."),
        ContentScanState.Infected => AuthorizationDecision.Deny(
            DecisionReason.DeniedScanIncomplete,
            "The file was quarantined by the malware scanner."),
        _ => AuthorizationDecision.Deny(
            DecisionReason.DeniedScanIncomplete,
            "The malware scan failed, so the file cannot be released."),
    };

    private static Applicability GetApplicability(AclEntry entry, ResourceDescriptor resource)
    {
        if (entry.Resource == resource.Resource)
        {
            return Applicability.Direct;
        }

        if (entry.Resource.Type != ResourceType.Category)
        {
            return Applicability.NotApplicable;
        }

        var index = IndexOf(resource.AncestorCategoryIds, entry.Resource.Id);
        if (index < 0)
        {
            return Applicability.NotApplicable;
        }

        // inherit = false means "this folder and the documents directly in it". For a document the
        // nearest ancestor is its own category, so such an entry still applies there; for anything
        // deeper, or for a sub-category, inheritance must be switched on.
        var isOwnCategoryOfDocument = index == 0 && resource.Resource.Type == ResourceType.Document;
        if (!entry.Inherit && !isOwnCategoryOfDocument)
        {
            return Applicability.NotApplicable;
        }

        return isOwnCategoryOfDocument && !entry.Inherit ? Applicability.Direct : Applicability.Inherited;
    }

    private static int IndexOf(IReadOnlyList<Guid> ancestors, Guid value)
    {
        for (var i = 0; i < ancestors.Count; i++)
        {
            if (ancestors[i] == value)
            {
                return i;
            }
        }

        return -1;
    }

    private enum Applicability
    {
        NotApplicable,
        Direct,
        Inherited,
    }
}
