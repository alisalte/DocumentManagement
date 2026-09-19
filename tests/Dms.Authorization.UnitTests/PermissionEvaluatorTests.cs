using Dms.Authorization.Application;
using Dms.Authorization.Contracts;
using Shouldly;
using static Dms.Authorization.UnitTests.Scenario;

namespace Dms.Authorization.UnitTests;

/// <summary>
/// The rule matrix from docs/architecture.md section 5.4. These are the tests the specification
/// calls out as mandatory: inheritance, override, explicit deny, and the separation of view,
/// download and print.
/// </summary>
public sealed class PermissionEvaluatorTests
{
    private static AuthorizationDecision Evaluate(
        PrincipalSet principal,
        string permission,
        ResourceDescriptor resource,
        IEnumerable<AclEntry>? entries = null,
        IEnumerable<TemporaryGrant>? grants = null) =>
        PermissionEvaluator.Evaluate(
            principal,
            permission,
            resource,
            (entries ?? []).ToList(),
            (grants ?? []).ToList(),
            Now);

    [Fact]
    public void Denies_by_default_when_no_rule_applies()
    {
        var decision = Evaluate(User(), PermissionCodes.DocumentView, Document());

        decision.Allowed.ShouldBeFalse();
        decision.Reason.ShouldBe(DecisionReason.DeniedByDefault);
    }

    [Fact]
    public void Allows_through_a_direct_entry_for_the_user()
    {
        var decision = Evaluate(User(), PermissionCodes.DocumentView, Document(), [Allow(PermissionCodes.DocumentView)]);

        decision.Allowed.ShouldBeTrue();
        decision.Reason.ShouldBe(DecisionReason.AllowedByAcl);
    }

    [Fact]
    public void Allows_through_group_membership()
    {
        var principal = User(groups: [Maintenance]);
        var entry = Allow(PermissionCodes.DocumentView, MaintenanceCategory, SubjectType.Group);

        Evaluate(principal, PermissionCodes.DocumentView, Document(), [entry]).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void Allows_through_a_role_used_as_an_acl_subject()
    {
        var principal = User(roles: [Reviewers]);
        var entry = Allow(PermissionCodes.DocumentView, RootCategory, SubjectType.Role);

        Evaluate(principal, PermissionCodes.DocumentView, Document(), [entry]).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void Inherits_an_allow_from_an_ancestor_category()
    {
        var entry = Allow(PermissionCodes.DocumentView, RootCategory);
        var decision = Evaluate(User(), PermissionCodes.DocumentView, Document(), [entry]);

        decision.Allowed.ShouldBeTrue();
        decision.Reason.ShouldBe(DecisionReason.AllowedByInheritedAcl);
    }

    [Fact]
    public void Does_not_inherit_from_a_distant_ancestor_when_inheritance_is_off()
    {
        var entry = Allow(PermissionCodes.DocumentView, RootCategory, inherit: false);

        Evaluate(User(), PermissionCodes.DocumentView, Document(), [entry]).Allowed.ShouldBeFalse();
    }

    [Fact]
    public void Applies_a_non_inheriting_entry_to_documents_in_that_category()
    {
        // "This folder and its documents" still covers the document directly inside it.
        var entry = Allow(PermissionCodes.DocumentView, ContractsCategory, inherit: false);

        Evaluate(User(), PermissionCodes.DocumentView, Document(), [entry]).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void Document_level_deny_overrides_a_category_allow()
    {
        AclEntry[] entries =
        [
            Allow(PermissionCodes.DocumentDownload, MaintenanceCategory),
            Allow(PermissionCodes.DocumentView, MaintenanceCategory),
            Deny(PermissionCodes.DocumentDownload),
        ];

        var decision = Evaluate(User(), PermissionCodes.DocumentDownload, Document(), entries);

        decision.Allowed.ShouldBeFalse();
        decision.Reason.ShouldBe(DecisionReason.DeniedByExplicitDeny);
    }

    [Fact]
    public void Category_deny_overrides_a_document_allow()
    {
        AclEntry[] entries =
        [
            Deny(PermissionCodes.DocumentView, MaintenanceCategory),
            Allow(PermissionCodes.DocumentView),
        ];

        Evaluate(User(), PermissionCodes.DocumentView, Document(), entries)
            .Reason.ShouldBe(DecisionReason.DeniedByExplicitDeny);
    }

    [Fact]
    public void Deny_for_a_group_beats_allow_for_the_user()
    {
        var principal = User(groups: [Maintenance]);
        AclEntry[] entries =
        [
            Allow(PermissionCodes.DocumentView),
            Deny(PermissionCodes.DocumentView, subjectType: SubjectType.Group),
        ];

        Evaluate(principal, PermissionCodes.DocumentView, Document(), entries)
            .Reason.ShouldBe(DecisionReason.DeniedByExplicitDeny);
    }

    [Fact]
    public void View_does_not_imply_download()
    {
        var entries = new[] { Allow(PermissionCodes.DocumentView) };

        Evaluate(User(), PermissionCodes.DocumentView, Document(), entries).Allowed.ShouldBeTrue();
        Evaluate(User(), PermissionCodes.DocumentDownload, Document(), entries).Allowed.ShouldBeFalse();
    }

    [Fact]
    public void Download_does_not_imply_print()
    {
        AclEntry[] entries =
        [
            Allow(PermissionCodes.DocumentView),
            Allow(PermissionCodes.DocumentDownload),
        ];

        Evaluate(User(), PermissionCodes.DocumentDownload, Document(), entries).Allowed.ShouldBeTrue();
        Evaluate(User(), PermissionCodes.DocumentPrint, Document(), entries).Allowed.ShouldBeFalse();
    }

    [Fact]
    public void Denying_view_blocks_download_even_when_download_is_allowed()
    {
        AclEntry[] entries =
        [
            Deny(PermissionCodes.DocumentView),
            Allow(PermissionCodes.DocumentDownload),
        ];

        var decision = Evaluate(User(), PermissionCodes.DocumentDownload, Document(), entries);

        decision.Allowed.ShouldBeFalse();
        decision.Reason.ShouldBe(DecisionReason.DeniedRequiresView);
    }

    [Fact]
    public void An_active_share_grants_access()
    {
        TemporaryGrant[] grants =
        [
            new(TemporaryGrantKind.Share, ResourceRef.Document(DocumentId), PermissionCodes.DocumentView,
                Now.AddDays(1)),
        ];

        var decision = Evaluate(User(), PermissionCodes.DocumentView, Document(), grants: grants);

        decision.Allowed.ShouldBeTrue();
        decision.Reason.ShouldBe(DecisionReason.AllowedByShare);
    }

    [Fact]
    public void An_expired_share_grants_nothing()
    {
        TemporaryGrant[] grants =
        [
            new(TemporaryGrantKind.Share, ResourceRef.Document(DocumentId), PermissionCodes.DocumentView,
                Now.AddMinutes(-1)),
        ];

        Evaluate(User(), PermissionCodes.DocumentView, Document(), grants: grants).Allowed.ShouldBeFalse();
    }

    [Fact]
    public void An_explicit_deny_outranks_a_share()
    {
        TemporaryGrant[] grants =
        [
            new(TemporaryGrantKind.Share, ResourceRef.Document(DocumentId), PermissionCodes.DocumentView,
                Now.AddDays(1)),
        ];

        Evaluate(User(), PermissionCodes.DocumentView, Document(), [Deny(PermissionCodes.DocumentView)], grants)
            .Reason.ShouldBe(DecisionReason.DeniedByExplicitDeny);
    }

    [Fact]
    public void A_draft_is_hidden_from_someone_who_only_has_view()
    {
        var decision = Evaluate(
            User(),
            PermissionCodes.DocumentView,
            Document(state: ContentState.Draft, author: Bob),
            [Allow(PermissionCodes.DocumentView)]);

        decision.Allowed.ShouldBeFalse();
        decision.Reason.ShouldBe(DecisionReason.DeniedDraft);
    }

    [Fact]
    public void An_author_sees_their_own_draft()
    {
        Evaluate(
                User(),
                PermissionCodes.DocumentView,
                Document(state: ContentState.Draft, author: Alice),
                [Allow(PermissionCodes.DocumentView)])
            .Allowed.ShouldBeTrue();
    }

    [Fact]
    public void View_draft_permission_reveals_a_draft()
    {
        AclEntry[] entries =
        [
            Allow(PermissionCodes.DocumentView),
            Allow(PermissionCodes.DocumentViewDraft),
        ];

        Evaluate(User(), PermissionCodes.DocumentView, Document(state: ContentState.Draft, author: Bob), entries)
            .Allowed.ShouldBeTrue();
    }

    [Fact]
    public void An_assigned_reviewer_sees_the_draft_under_review()
    {
        TemporaryGrant[] grants =
        [
            new(TemporaryGrantKind.WorkflowTask, ResourceRef.Document(DocumentId), PermissionCodes.DocumentView),
        ];

        Evaluate(
                User(),
                PermissionCodes.DocumentView,
                Document(state: ContentState.Draft, author: Bob),
                grants: grants)
            .Allowed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(ContentScanState.Pending)]
    [InlineData(ContentScanState.Infected)]
    [InlineData(ContentScanState.Failed)]
    public void Content_is_refused_until_the_malware_scan_passes(ContentScanState scanState)
    {
        AclEntry[] entries =
        [
            Allow(PermissionCodes.DocumentView),
            Allow(PermissionCodes.DocumentDownload),
        ];

        var resource = Document(scan: scanState, author: Alice);

        // Decision D9: not even the uploader can pull the bytes out before the scan finishes.
        Evaluate(User(), PermissionCodes.DocumentView, resource, entries)
            .Reason.ShouldBe(DecisionReason.DeniedScanIncomplete);
        Evaluate(User(), PermissionCodes.DocumentDownload, resource, entries)
            .Reason.ShouldBe(DecisionReason.DeniedScanIncomplete);
    }

    [Fact]
    public void A_soft_deleted_document_exposes_only_restore_and_audit()
    {
        AclEntry[] entries =
        [
            Allow(PermissionCodes.DocumentView),
            Allow(PermissionCodes.DocumentRestore),
        ];

        var deleted = Document(deleted: true);

        Evaluate(User(), PermissionCodes.DocumentView, deleted, entries)
            .Reason.ShouldBe(DecisionReason.DeniedResourceDeleted);
        Evaluate(User(), PermissionCodes.DocumentRestore, deleted, entries).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void An_inactive_user_is_refused_everything()
    {
        var decision = Evaluate(
            User(isActive: false),
            PermissionCodes.DocumentView,
            Document(),
            [Allow(PermissionCodes.DocumentView)]);

        decision.Reason.ShouldBe(DecisionReason.DeniedUserInactive);
    }

    [Fact]
    public void A_system_administrator_does_not_get_document_content()
    {
        // Decision D5: administration is not the same thing as read access to the archive.
        var decision = Evaluate(User(isSystemAdmin: true), PermissionCodes.DocumentView, Document());

        decision.Allowed.ShouldBeFalse();
        decision.Reason.ShouldBe(DecisionReason.DeniedByDefault);
    }

    [Fact]
    public void A_system_administrator_can_always_manage_permissions()
    {
        // Otherwise a bad ACL could leave a resource with nobody able to fix it.
        var decision = Evaluate(
            User(isSystemAdmin: true),
            PermissionCodes.DocumentManagePermission,
            Document(),
            [Deny(PermissionCodes.DocumentManagePermission)]);

        decision.Allowed.ShouldBeTrue();
        decision.Reason.ShouldBe(DecisionReason.AllowedBySystemAdministrator);
    }

    [Fact]
    public void Unknown_permissions_are_refused()
    {
        Evaluate(User(), "DOCUMENT_TELEPORT", Document()).Reason.ShouldBe(DecisionReason.DeniedUnknownPermission);
    }

    [Fact]
    public void A_system_permission_is_not_evaluated_against_a_resource()
    {
        Evaluate(User(), PermissionCodes.AdminManageUsers, Document()).Reason.ShouldBe(DecisionReason.DeniedWrongScope);
    }
}

public sealed class SystemPermissionTests
{
    [Fact]
    public void A_role_grants_a_system_permission()
    {
        var principal = User(systemPermissions: [PermissionCodes.AdminManageUsers]);

        PermissionEvaluator.EvaluateSystem(principal, PermissionCodes.AdminManageUsers)
            .Reason.ShouldBe(DecisionReason.AllowedBySystemRole);
    }

    [Fact]
    public void Without_the_role_the_system_permission_is_refused()
    {
        PermissionEvaluator.EvaluateSystem(User(), PermissionCodes.AdminManageUsers)
            .Allowed.ShouldBeFalse();
    }

    [Fact]
    public void A_system_administrator_holds_every_system_permission()
    {
        PermissionEvaluator.EvaluateSystem(User(isSystemAdmin: true), PermissionCodes.AdminManageRoles)
            .Reason.ShouldBe(DecisionReason.AllowedBySystemAdministrator);
    }

    [Fact]
    public void A_resource_permission_cannot_be_granted_through_a_role()
    {
        PermissionEvaluator.EvaluateSystem(
                User(systemPermissions: [PermissionCodes.DocumentView]),
                PermissionCodes.DocumentView)
            .Reason.ShouldBe(DecisionReason.DeniedWrongScope);
    }

    [Fact]
    public void An_inactive_administrator_is_still_refused()
    {
        PermissionEvaluator.EvaluateSystem(User(isActive: false, isSystemAdmin: true), PermissionCodes.AdminManageUsers)
            .Reason.ShouldBe(DecisionReason.DeniedUserInactive);
    }
}
