using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Documents.Contracts;
using Dms.DocumentTypes.Contracts;
using Dms.SharedKernel;
using Dms.Sharing.Domain;
using Microsoft.Extensions.Options;

namespace Dms.Sharing.Application;

public sealed class SharingOptions
{
    public const string SectionName = "Dms:Sharing";

    /// <summary>Switches external links off for the whole installation; internal shares are unaffected.</summary>
    public bool ExternalLinksEnabled { get; set; } = true;

    /// <summary>A link must expire, and no later than this.</summary>
    public int MaxLinkLifetimeDays { get; set; } = 90;

    /// <summary>How long one opening of a link lasts before the visitor has to open it again.</summary>
    public int LinkSessionMinutes { get; set; } = 30;

    public int LinkPasswordMaxAttempts { get; set; } = 5;

    public int LinkPasswordLockoutMinutes { get; set; } = 15;
}

internal static class SharingErrors
{
    public static readonly Error Unauthenticated = Error.Unauthorized("auth.unauthenticated", "Not authenticated.");

    /// <summary>A document or version the caller may not see looks exactly like one that does not exist.</summary>
    public static readonly Error DocumentNotFound = Error.NotFound("document.not_found", "The document does not exist.");

    public static readonly Error ShareNotFound = Error.NotFound("share.not_found", "The share does not exist.");

    public static readonly Error LinkNotFound = Error.NotFound("share_link.not_found", "The link does not exist.");

    public static readonly Error VersionNotPublished = Error.Conflict(
        "share.version_not_published",
        "Only published versions can be shared. Drafts stay with their author and reviewers.");

    public static Error ContentBlocked(string explanation) => Error.Conflict("share.content_blocked", explanation);

    public static Error Forbidden(string explanation) => Error.Forbidden("auth.forbidden", explanation);

    public static readonly Error ExceedsOwnRights = Error.Forbidden(
        "share.exceeds_rights",
        "You can only share what you may do yourself.");

    public static readonly Error RecipientNotFound = Error.Validation(
        "share.recipient_not_found",
        "The recipient is not an active user.");

    public static readonly Error ExternalLinksDisabled = Error.Forbidden(
        "share_link.disabled",
        "External links are switched off.");

    public static readonly Error ExternalSharingNotAllowedForType = Error.Forbidden(
        "share_link.not_allowed_for_type",
        "Documents of this type may not be shared outside the organisation.");

    public static readonly Error PasswordTooShort = Error.Validation(
        "share_link.password_too_short",
        $"A link password needs at least {ShareLink.MinPasswordLength} characters.");

    /// <summary>
    /// Unknown, revoked, expired, used up, or no longer backed by its creator's rights: a visitor
    /// learns nothing about which.
    /// </summary>
    public static readonly Error LinkInvalid = Error.NotFound(
        "share_link.invalid",
        "This link is not valid or has expired.");

    public static readonly Error PasswordRequired = Error.Unauthorized(
        "share_link.password_required",
        "This link needs a password.");

    public static readonly Error PasswordIncorrect = Error.Unauthorized(
        "share_link.password_incorrect",
        "The password is not correct.");

    public static Error Locked(DateTimeOffset until) => Error.TooManyRequests(
        "share_link.locked",
        $"Too many wrong passwords. Try again after {until:O}.");

    public static readonly Error SessionExpired = Error.Unauthorized(
        "share_link.session_expired",
        "The link session has ended. Open the link again.");

    public static readonly Error PrintNotStarted = Error.Conflict(
        "share_link.print_not_started",
        "Start printing first.");

    public static readonly Error NotIncluded = Error.Forbidden(
        "share_link.not_included",
        "This link does not include that.");
}

public static class SharePermissionCodes
{
    public static string For(SharePermissions flag) => flag switch
    {
        SharePermissions.View => PermissionCodes.DocumentView,
        SharePermissions.Download => PermissionCodes.DocumentDownload,
        SharePermissions.Print => PermissionCodes.DocumentPrint,
        _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, "Not a single share permission."),
    };

    public static SharePermissions Combine(IEnumerable<SharePermissions>? flags) =>
        (flags ?? []).Aggregate(SharePermissions.None, (all, flag) => all | flag);

    public static IReadOnlyList<SharePermissions> Split(SharePermissions permissions) =>
        SharePermissionRules.Each(permissions).ToList();
}

/// <summary>Why a sharer may or may not hand a version on.</summary>
internal sealed record SharerCheck(VersionSummary? Version, Error? Error)
{
    public bool IsAllowed => Error is null;
}

/// <summary>
/// The checks every share and link goes through, when it is made and again whenever it is used
/// (decision D8): the version is published and its file released, the document type allows it,
/// and the sharer holds the share permission and everything being shared through their own
/// rights, never through a share they received.
/// </summary>
public sealed class SharingPolicy(
    IDmsAuthorizer authorizer,
    IDocumentVersionReader versions,
    IDocumentTypeCatalog documentTypes,
    IOptions<SharingOptions> options)
{
    internal async Task<SharerCheck> CheckSharerAsync(
        UserId sharer,
        Guid documentId,
        Guid versionId,
        string sharePermission,
        SharePermissions permissions,
        CancellationToken cancellationToken)
    {
        var version = await versions.FindAsync(documentId, versionId, cancellationToken);
        if (version is null || version.IsDocumentDeleted)
        {
            return new SharerCheck(null, SharingErrors.DocumentNotFound);
        }

        var resource = ResourceRef.Document(documentId);
        var view = await authorizer.AuthorizeOwnRightsAsync(sharer, PermissionCodes.DocumentView, resource, versionId, cancellationToken);
        if (!view.Allowed)
        {
            // A blocked file is worth explaining to someone who can see the document; anything
            // else stays indistinguishable from a document that does not exist.
            return view.Reason == DecisionReason.DeniedScanIncomplete
                ? new SharerCheck(version, SharingErrors.ContentBlocked(view.Explanation))
                : new SharerCheck(version, SharingErrors.DocumentNotFound);
        }

        if (!version.IsPublished)
        {
            return new SharerCheck(version, SharingErrors.VersionNotPublished);
        }

        if (sharePermission == PermissionCodes.DocumentShareExternal)
        {
            if (!options.Value.ExternalLinksEnabled)
            {
                return new SharerCheck(version, SharingErrors.ExternalLinksDisabled);
            }

            var type = await documentTypes.FindAsync(new DocumentTypeId(version.DocumentTypeId), cancellationToken);
            if (type?.Settings.AllowExternalSharing != true)
            {
                return new SharerCheck(version, SharingErrors.ExternalSharingNotAllowedForType);
            }
        }

        var share = await authorizer.AuthorizeOwnRightsAsync(sharer, sharePermission, resource, versionId, cancellationToken);
        if (!share.Allowed)
        {
            return new SharerCheck(version, SharingErrors.Forbidden(share.Explanation));
        }

        foreach (var flag in SharePermissionRules.Each(permissions))
        {
            var held = await authorizer.AuthorizeOwnRightsAsync(
                sharer,
                SharePermissionCodes.For(flag),
                resource,
                versionId,
                cancellationToken);
            if (!held.Allowed)
            {
                return new SharerCheck(version, SharingErrors.ExceedsOwnRights);
            }
        }

        return new SharerCheck(version, null);
    }

    /// <summary>
    /// What a link still gives right now: VIEW, plus download and print where both the link and
    /// its creator still have them. Null when the link no longer works at all.
    /// </summary>
    internal async Task<(VersionSummary Version, SharePermissions Effective)?> CheckLinkAsync(
        ShareLink link,
        CancellationToken cancellationToken)
    {
        var check = await CheckSharerAsync(
            link.CreatedBy,
            link.DocumentId.Value,
            link.VersionId,
            PermissionCodes.DocumentShareExternal,
            SharePermissions.View,
            cancellationToken);
        if (!check.IsAllowed || check.Version is null)
        {
            return null;
        }

        var effective = SharePermissions.View;
        foreach (var flag in new[] { SharePermissions.Download, SharePermissions.Print })
        {
            if (link.Permissions.HasFlag(flag)
                && (await authorizer.AuthorizeOwnRightsAsync(
                    link.CreatedBy,
                    SharePermissionCodes.For(flag),
                    ResourceRef.Document(link.DocumentId.Value),
                    link.VersionId,
                    cancellationToken)).Allowed)
            {
                effective |= flag;
            }
        }

        return (check.Version, effective);
    }
}

/// <summary>Audit helpers: refusals outside a transaction still get recorded (as in Documents).</summary>
public sealed class SharingAudit(IAuditWriter audit, IUnitOfWork unitOfWork)
{
    public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken) => audit.WriteAsync(record, cancellationToken);

    public async Task WriteStandaloneAsync(AuditRecord record, CancellationToken cancellationToken)
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

    public Task DeniedAsync(Guid documentId, Guid? versionId, string permission, string reason, CancellationToken cancellationToken) =>
        WriteStandaloneAsync(
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
                    ["permission"] = permission,
                    ["reason"] = reason,
                },
            },
            cancellationToken);
}
