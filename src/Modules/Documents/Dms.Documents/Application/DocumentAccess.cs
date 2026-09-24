using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.SharedKernel;

namespace Dms.Documents.Application;

/// <summary>
/// The one place where document handlers ask permission questions, so every handler answers
/// them the same way (section 5.8):
///
/// - no VIEW on the document: <b>404</b>, exactly as if it did not exist;
/// - VIEW but not the permission asked for: <b>403</b>;
/// - every refusal is written to the audit log with outcome DENIED.
///
/// Metadata questions are asked about the document; content questions (download) are asked about
/// the specific version, because the draft and malware-scan gates belong to a version.
///
/// A question about one version is also let in by VIEW on that version alone: that is how a share
/// recipient, whose share is pinned to one version (decision D8), reaches it without seeing the
/// rest of the document.
/// </summary>
public sealed class DocumentAccess(IDmsAuthorizer authorizer, IAuditWriter audit, IUnitOfWork unitOfWork)
{
    public Task<Result> RequireAsync(Guid documentId, string permissionCode, CancellationToken cancellationToken) =>
        RequireAsync(documentId, permissionCode, versionId: null, cancellationToken);

    public async Task<Result> RequireAsync(
        Guid documentId,
        string permissionCode,
        Guid? versionId,
        CancellationToken cancellationToken)
    {
        var resource = ResourceRef.Document(documentId);

        var view = await authorizer.AuthorizeAsync(PermissionCodes.DocumentView, resource, cancellationToken);
        if (!view.Allowed && versionId is { } pinned)
        {
            var versionView = await authorizer.AuthorizeVersionAsync(PermissionCodes.DocumentView, resource, pinned, cancellationToken);
            if (versionView.Allowed)
            {
                view = versionView;
            }
        }

        if (!view.Allowed)
        {
            await AuditDeniedAsync(documentId, versionId, PermissionCodes.DocumentView, view, cancellationToken);
            return Result.Failure(DocumentErrors.DocumentNotFound);
        }

        if (permissionCode == PermissionCodes.DocumentView && versionId is null)
        {
            return Result.Success();
        }

        var decision = versionId is { } version
            ? await authorizer.AuthorizeVersionAsync(permissionCode, resource, version, cancellationToken)
            : await authorizer.AuthorizeAsync(permissionCode, resource, cancellationToken);

        if (decision.Allowed)
        {
            return Result.Success();
        }

        await AuditDeniedAsync(documentId, versionId, permissionCode, decision, cancellationToken);

        // A version the caller may not even see (someone else's draft) is hidden, not refused.
        return decision.Reason is DecisionReason.DeniedDraft or DecisionReason.DeniedUnknownResource
            ? Result.Failure(DocumentErrors.VersionNotFound)
            : Result.Failure(DocumentErrors.Forbidden(decision.Explanation));
    }

    /// <summary>
    /// For the recycle bin. A soft deleted document answers only RESTORE, PURGE and AUDIT_VIEW
    /// (section 5.4 step 3), so VIEW cannot be the gate here; a refusal is still a 404.
    /// </summary>
    public async Task<Result> RequireOnDeletedAsync(
        Guid documentId,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeAsync(
            permissionCode,
            ResourceRef.Document(documentId),
            cancellationToken);

        if (decision.Allowed)
        {
            return Result.Success();
        }

        await AuditDeniedAsync(documentId, null, permissionCode, decision, cancellationToken);
        return Result.Failure(DocumentErrors.DocumentNotFound);
    }

    /// <summary>Permission on a category, for example DOCUMENT_CREATE. Categories are not secret: 403.</summary>
    public async Task<Result> RequireOnCategoryAsync(
        Guid categoryId,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeAsync(
            permissionCode,
            ResourceRef.Category(categoryId),
            cancellationToken);

        if (decision.Allowed)
        {
            return Result.Success();
        }

        await WriteDeniedAsync(
            new AuditRecord
            {
                Action = AuditActions.AccessDenied,
                Outcome = AuditOutcome.Denied,
                EntityType = "Category",
                EntityId = categoryId,
                Metadata = new Dictionary<string, object?>
                {
                    ["permission"] = permissionCode,
                    ["reason"] = decision.Reason.ToString(),
                },
            },
            cancellationToken);

        return Result.Failure(DocumentErrors.Forbidden(decision.Explanation));
    }

    public async Task<bool> IsAllowedAsync(Guid documentId, string permissionCode, CancellationToken cancellationToken) =>
        (await authorizer.AuthorizeAsync(permissionCode, ResourceRef.Document(documentId), cancellationToken)).Allowed;

    /// <summary>A content permission on one version, gates included, without auditing a refusal. For UI hints.</summary>
    public async Task<bool> IsVersionAllowedAsync(Guid documentId, string permissionCode, Guid versionId, CancellationToken cancellationToken) =>
        (await authorizer.AuthorizeVersionAsync(permissionCode, ResourceRef.Document(documentId), versionId, cancellationToken)).Allowed;

    public async Task<bool> IsSystemAllowedAsync(string permissionCode, CancellationToken cancellationToken) =>
        (await authorizer.AuthorizeSystemAsync(permissionCode, cancellationToken)).Allowed;

    private Task AuditDeniedAsync(
        Guid documentId,
        Guid? versionId,
        string permissionCode,
        AuthorizationDecision decision,
        CancellationToken cancellationToken) =>
        WriteDeniedAsync(
            new AuditRecord
            {
                Action = AuditActions.AccessDenied,
                Outcome = AuditOutcome.Denied,
                EntityType = "Document",
                EntityId = documentId,
                DocumentId = documentId,
                VersionId = versionId,
                Metadata = new Dictionary<string, object?>
                {
                    ["permission"] = permissionCode,
                    ["reason"] = decision.Reason.ToString(),
                },
            },
            cancellationToken);

    /// <summary>
    /// Commands already run in a transaction that commits even on an expected failure. Queries do
    /// not, so the refusal gets a short transaction of its own; otherwise it would be lost.
    /// </summary>
    private async Task WriteDeniedAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        if (unitOfWork.HasActiveTransaction)
        {
            await audit.WriteAsync(record, cancellationToken);
            return;
        }

        await unitOfWork.BeginAsync(cancellationToken);
        try
        {
            await audit.WriteAsync(record, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
