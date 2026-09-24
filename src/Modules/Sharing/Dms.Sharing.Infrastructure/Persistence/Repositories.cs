using Dms.SharedKernel;
using Dms.Sharing.Application;
using Dms.Sharing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Dms.Sharing.Infrastructure.Persistence;

public sealed class ShareRepository(SharingDbContext context) : IShareRepository
{
    public Task<DocumentShare?> FindAsync(ShareId id, CancellationToken cancellationToken) =>
        context.Shares.FirstOrDefaultAsync(share => share.Id == id, cancellationToken);

    public Task<DocumentShare?> FindUnrevokedAsync(Guid versionId, UserId sharedWith, CancellationToken cancellationToken) =>
        context.Shares.FirstOrDefaultAsync(
            share => share.VersionId == versionId && share.SharedWith == sharedWith && share.RevokedAt == null,
            cancellationToken);

    public async Task<IReadOnlyList<DocumentShare>> ListActiveForRecipientAsync(
        UserId sharedWith,
        DocumentId? documentId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var query = context.Shares.AsNoTracking()
            .Where(share => share.SharedWith == sharedWith
                && share.RevokedAt == null
                && (share.ExpiresAt == null || share.ExpiresAt > now));

        if (documentId is { } document)
        {
            query = query.Where(share => share.DocumentId == document);
        }

        return await query.OrderByDescending(share => share.CreatedAt).Take(500).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentShare>> ListForDocumentAsync(
        DocumentId documentId,
        UserId? sharedBy,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = context.Shares.AsNoTracking().Where(share => share.DocumentId == documentId);
        if (sharedBy is { } sharer)
        {
            query = query.Where(share => share.SharedBy == sharer);
        }

        return await query.OrderByDescending(share => share.CreatedAt).Take(limit).ToListAsync(cancellationToken);
    }

    public Task LockAsync(Guid versionId, UserId sharedWith, CancellationToken cancellationToken)
    {
        // Released at commit or rollback. Needs the command's transaction, which every command has.
        var key = $"sharing.share:{versionId}:{sharedWith.Value}";
        return context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))",
            cancellationToken);
    }

    public void Add(DocumentShare share) => context.Shares.Add(share);

    public Task FlushAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}

public sealed class ShareLinkRepository(SharingDbContext context) : IShareLinkRepository
{
    public Task<ShareLink?> FindAsync(ShareLinkId id, CancellationToken cancellationToken) =>
        context.Links.FirstOrDefaultAsync(link => link.Id == id, cancellationToken);

    public Task<ShareLink?> FindByTokenHashAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
        context.Links.AsNoTracking().FirstOrDefaultAsync(link => link.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<ShareLink>> ListForDocumentAsync(
        DocumentId documentId,
        UserId? createdBy,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = context.Links.AsNoTracking().Where(link => link.DocumentId == documentId);
        if (createdBy is { } creator)
        {
            query = query.Where(link => link.CreatedBy == creator);
        }

        return await query.OrderByDescending(link => link.CreatedAt).Take(limit).ToListAsync(cancellationToken);
    }

    public void Add(ShareLink link) => context.Links.Add(link);

    public async Task<bool> TryConsumeOpeningAsync(ShareLinkId id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // One statement: the row lock serialises racing visitors, and each re-reads the count
        // after the one before it committed (section 4.11).
        var updated = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE sharing.share_links
               SET access_count = access_count + 1, last_accessed_at = {now}
             WHERE id = {id.Value}
               AND revoked_at IS NULL
               AND expires_at > {now}
               AND (max_access_count IS NULL OR access_count < max_access_count)
            """,
            cancellationToken);

        return updated == 1;
    }

    public async Task<int> RegisterFailedPasswordAsync(
        ShareLinkId id,
        int maxAttempts,
        DateTimeOffset lockUntil,
        CancellationToken cancellationToken)
    {
        var attempts = await context.Database.SqlQuery<int>(
                $"""
                UPDATE sharing.share_links
                   SET failed_attempts = failed_attempts + 1,
                       locked_until = CASE WHEN failed_attempts + 1 >= {maxAttempts} THEN {lockUntil} ELSE locked_until END
                 WHERE id = {id.Value}
                RETURNING failed_attempts AS "Value"
                """)
            .ToListAsync(cancellationToken);

        // A lock is a pause, not a verdict: the counter starts again once it has been applied.
        if (attempts is [var count] && count >= maxAttempts)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE sharing.share_links SET failed_attempts = 0 WHERE id = {id.Value}",
                cancellationToken);
        }

        return attempts is [var value] ? value : 0;
    }

    public Task ResetFailedPasswordsAsync(ShareLinkId id, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sharing.share_links SET failed_attempts = 0, locked_until = NULL WHERE id = {id.Value}",
            cancellationToken);

    public void AddSession(ShareLinkSession session) => context.LinkSessions.Add(session);

    public Task<ShareLinkSession?> FindSessionAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
        context.LinkSessions.AsNoTracking().FirstOrDefaultAsync(session => session.TokenHash == tokenHash, cancellationToken);

    public Task MarkPrintStartedAsync(ShareLinkSessionId id, DateTimeOffset now, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sharing.share_link_sessions SET print_started_at = {now} WHERE id = {id.Value} AND print_started_at IS NULL",
            cancellationToken);

    public Task<int> DeleteExpiredSessionsAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        context.LinkSessions.Where(session => session.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
}
