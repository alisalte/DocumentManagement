using Dms.SharedKernel;

namespace Dms.Sharing.Domain;

public readonly record struct ShareLinkId(Guid Value)
{
    public static ShareLinkId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// An external link to one version (section 4.8, decision D8). The token is shown to its creator
/// once and stored only as a SHA-256 digest; the first characters are kept so the creator can tell
/// links apart. A link always expires, may be limited to a number of openings and may need a
/// password, which locks the link for a while after repeated wrong guesses.
///
/// Opening a link and counting it is one atomic UPDATE in the repository, not a method here: two
/// visitors racing for the last opening must not both get in.
/// </summary>
public sealed class ShareLink : Entity<ShareLinkId>
{
    public const int PrefixLength = 8;
    public const int MaxLabelLength = 200;
    public const int MinPasswordLength = 6;

    private ShareLink()
    {
    }

    private ShareLink(
        ShareLinkId id,
        DocumentId documentId,
        Guid versionId,
        byte[] tokenHash,
        string tokenPrefix,
        SharePermissions permissions,
        string? passwordHash,
        DateTimeOffset expiresAt,
        int? maxAccessCount,
        string? label,
        UserId createdBy,
        DateTimeOffset now)
        : base(id)
    {
        DocumentId = documentId;
        VersionId = versionId;
        TokenHash = tokenHash;
        TokenPrefix = tokenPrefix;
        Permissions = permissions;
        PasswordHash = passwordHash;
        ExpiresAt = expiresAt;
        MaxAccessCount = maxAccessCount;
        Label = label;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public DocumentId DocumentId { get; private set; }

    public Guid VersionId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public string TokenPrefix { get; private set; } = string.Empty;

    public SharePermissions Permissions { get; private set; }

    public string? PasswordHash { get; private set; }

    public bool RequiresPassword => PasswordHash is not null;

    public DateTimeOffset ExpiresAt { get; private set; }

    public int? MaxAccessCount { get; private set; }

    public int AccessCount { get; private set; }

    public int FailedAttempts { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public string? Label { get; private set; }

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public UserId? RevokedBy { get; private set; }

    public DateTimeOffset? LastAccessedAt { get; private set; }

    /// <param name="token">The raw token; only its prefix is kept.</param>
    public static Result<ShareLink> Create(
        DocumentId documentId,
        Guid versionId,
        string token,
        byte[] tokenHash,
        SharePermissions permissions,
        string? passwordHash,
        DateTimeOffset expiresAt,
        int? maxAccessCount,
        string? label,
        UserId createdBy,
        DateTimeOffset now,
        TimeSpan maxLifetime)
    {
        var checkedPermissions = SharePermissionRules.Validate(permissions);
        if (checkedPermissions.IsFailure)
        {
            return Result.Failure<ShareLink>(checkedPermissions.Error);
        }

        if (expiresAt <= now)
        {
            return Result.Failure<ShareLink>(
                Error.Validation("share.expiry_in_past", "The expiry must be in the future."));
        }

        if (expiresAt > now + maxLifetime)
        {
            return Result.Failure<ShareLink>(
                Error.Validation("share_link.expiry_too_far", $"A link may live at most {maxLifetime.TotalDays:0} days."));
        }

        if (maxAccessCount is < 1)
        {
            return Result.Failure<ShareLink>(
                Error.Validation("share_link.max_count", "The number of openings must be at least one."));
        }

        label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        if (label is { Length: > MaxLabelLength })
        {
            return Result.Failure<ShareLink>(
                Error.Validation("share_link.label_too_long", $"The label is limited to {MaxLabelLength} characters."));
        }

        return Result.Success(new ShareLink(
            ShareLinkId.New(),
            documentId,
            versionId,
            tokenHash,
            token[..PrefixLength],
            permissions,
            passwordHash,
            expiresAt,
            maxAccessCount,
            label,
            createdBy,
            now));
    }

    /// <summary>Not revoked, not expired, openings left. The count is re-checked atomically on opening.</summary>
    public bool IsUsable(DateTimeOffset now) =>
        RevokedAt is null && ExpiresAt > now && (MaxAccessCount is null || AccessCount < MaxAccessCount);

    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } until && until > now;

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

public readonly record struct ShareLinkSessionId(Guid Value)
{
    public static ShareLinkSessionId New() => new(Guid.CreateVersion7());
}

/// <summary>
/// One opening of a link. The visitor gets a short-lived session token so the pages and the file
/// can be fetched without spending another opening each time; the link itself is still re-checked
/// on every request (revoked, expired, the creator's rights).
/// </summary>
public sealed class ShareLinkSession : Entity<ShareLinkSessionId>
{
    private ShareLinkSession()
    {
    }

    public ShareLinkSession(ShareLinkId linkId, byte[] tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
        : base(ShareLinkSessionId.New())
    {
        LinkId = linkId;
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public ShareLinkId LinkId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// When the visitor started printing (and it was audited). Print pages are served only after
    /// that, so a print cannot skip the DOCUMENT_PRINTED record.
    /// </summary>
    public DateTimeOffset? PrintStartedAt { get; private set; }
}
