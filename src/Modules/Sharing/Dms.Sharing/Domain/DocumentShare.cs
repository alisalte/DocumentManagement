using Dms.SharedKernel;

namespace Dms.Sharing.Domain;

public readonly record struct ShareId(Guid Value)
{
    public static ShareId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// What a share hands out. Stored as a flag set (section 4.8): a share often grants view and
/// download together. VIEW is always part of it, since nothing else works without it.
/// </summary>
[Flags]
public enum SharePermissions
{
    None = 0,
    View = 1,
    Download = 2,
    Print = 4,
}

/// <summary>
/// An internal share: one user lets another see one version (decision D8). It grants nothing
/// beyond what the sharer holds, and only while they still hold it; the authorizer checks that
/// on every use. Shares are never edited, only revoked; sharing again replaces the old one.
/// </summary>
public sealed class DocumentShare : Entity<ShareId>
{
    public const int MaxMessageLength = 1000;

    private DocumentShare()
    {
    }

    private DocumentShare(
        ShareId id,
        DocumentId documentId,
        Guid versionId,
        UserId sharedBy,
        UserId sharedWith,
        SharePermissions permissions,
        DateTimeOffset? expiresAt,
        string? message,
        DateTimeOffset now)
        : base(id)
    {
        DocumentId = documentId;
        VersionId = versionId;
        SharedBy = sharedBy;
        SharedWith = sharedWith;
        Permissions = permissions;
        ExpiresAt = expiresAt;
        Message = message;
        CreatedAt = now;
    }

    public DocumentId DocumentId { get; private set; }

    public Guid VersionId { get; private set; }

    public UserId SharedBy { get; private set; }

    public UserId SharedWith { get; private set; }

    public SharePermissions Permissions { get; private set; }

    /// <summary>Null means until revoked.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    public string? Message { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public UserId? RevokedBy { get; private set; }

    public static Result<DocumentShare> Create(
        DocumentId documentId,
        Guid versionId,
        UserId sharedBy,
        UserId sharedWith,
        SharePermissions permissions,
        DateTimeOffset? expiresAt,
        string? message,
        DateTimeOffset now)
    {
        if (sharedBy == sharedWith)
        {
            return Result.Failure<DocumentShare>(
                Error.Validation("share.self", "A document cannot be shared with yourself."));
        }

        var checkedPermissions = SharePermissionRules.Validate(permissions);
        if (checkedPermissions.IsFailure)
        {
            return Result.Failure<DocumentShare>(checkedPermissions.Error);
        }

        if (expiresAt is { } expiry && expiry <= now)
        {
            return Result.Failure<DocumentShare>(
                Error.Validation("share.expiry_in_past", "The expiry must be in the future."));
        }

        message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (message is { Length: > MaxMessageLength })
        {
            return Result.Failure<DocumentShare>(
                Error.Validation("share.message_too_long", $"The message is limited to {MaxMessageLength} characters."));
        }

        return Result.Success(new DocumentShare(
            ShareId.New(),
            documentId,
            versionId,
            sharedBy,
            sharedWith,
            permissions,
            expiresAt,
            message,
            now));
    }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    /// <summary>Idempotent: revoking twice keeps the first revocation.</summary>
    public bool Revoke(UserId by, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return false;
        }

        RevokedAt = now;
        RevokedBy = by;
        return true;
    }
}

public static class SharePermissionRules
{
    private const SharePermissions All = SharePermissions.View | SharePermissions.Download | SharePermissions.Print;

    public static Result Validate(SharePermissions permissions)
    {
        if ((permissions & ~All) != 0)
        {
            return Result.Failure(Error.Validation("share.permissions_unknown", "Only view, download and print can be shared."));
        }

        return permissions.HasFlag(SharePermissions.View)
            ? Result.Success()
            : Result.Failure(Error.Validation("share.view_required", "A share always includes viewing."));
    }

    public static IEnumerable<SharePermissions> Each(SharePermissions permissions) =>
        new[] { SharePermissions.View, SharePermissions.Download, SharePermissions.Print }
            .Where(flag => permissions.HasFlag(flag));
}
