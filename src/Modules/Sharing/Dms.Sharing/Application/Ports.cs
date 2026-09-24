using Dms.SharedKernel;
using Dms.Sharing.Domain;

namespace Dms.Sharing.Application;

public interface IShareRepository
{
    Task<DocumentShare?> FindAsync(ShareId id, CancellationToken cancellationToken);

    /// <summary>The one unrevoked share of a version with a user, expired or not (a unique index allows only one).</summary>
    Task<DocumentShare?> FindUnrevokedAsync(Guid versionId, UserId sharedWith, CancellationToken cancellationToken);

    /// <summary>Unrevoked, unexpired shares to the user, optionally of one document only.</summary>
    Task<IReadOnlyList<DocumentShare>> ListActiveForRecipientAsync(
        UserId sharedWith,
        DocumentId? documentId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Every share of the document, newest first, revoked and expired ones included.</summary>
    /// <param name="sharedBy">Only the shares this user made; null for all of them.</param>
    Task<IReadOnlyList<DocumentShare>> ListForDocumentAsync(
        DocumentId documentId,
        UserId? sharedBy,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Serialises changes to the shares of one version with one user until the transaction ends,
    /// so two requests replacing the same share cannot both insert a live row.
    /// </summary>
    Task LockAsync(Guid versionId, UserId sharedWith, CancellationToken cancellationToken);

    void Add(DocumentShare share);

    /// <summary>
    /// Writes pending changes now, inside the current transaction. Replacing a share revokes the
    /// old row first, and the partial unique index must see that before the new row arrives.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken);
}

public interface IShareLinkRepository
{
    Task<ShareLink?> FindAsync(ShareLinkId id, CancellationToken cancellationToken);

    /// <summary>Untracked: the public path changes a link only through the atomic statements below.</summary>
    Task<ShareLink?> FindByTokenHashAsync(byte[] tokenHash, CancellationToken cancellationToken);

    /// <param name="createdBy">Only the links this user made; null for all of them.</param>
    Task<IReadOnlyList<ShareLink>> ListForDocumentAsync(
        DocumentId documentId,
        UserId? createdBy,
        int limit,
        CancellationToken cancellationToken);

    void Add(ShareLink link);

    /// <summary>
    /// Counts one opening in a single statement (section 4.11), and only while the link is not
    /// revoked, not expired and has openings left. False when any of that no longer holds, so two
    /// visitors racing for the last opening cannot both get in.
    /// </summary>
    Task<bool> TryConsumeOpeningAsync(ShareLinkId id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Counts a wrong password, locking the link once the limit is reached. Returns the new count.</summary>
    Task<int> RegisterFailedPasswordAsync(
        ShareLinkId id,
        int maxAttempts,
        DateTimeOffset lockUntil,
        CancellationToken cancellationToken);

    Task ResetFailedPasswordsAsync(ShareLinkId id, CancellationToken cancellationToken);

    void AddSession(ShareLinkSession session);

    Task<ShareLinkSession?> FindSessionAsync(byte[] tokenHash, CancellationToken cancellationToken);

    /// <summary>Records that printing started in the session; later calls keep the first time.</summary>
    Task MarkPrintStartedAsync(ShareLinkSessionId id, DateTimeOffset now, CancellationToken cancellationToken);

    Task<int> DeleteExpiredSessionsAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
