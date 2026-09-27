using Dms.Documents.Application;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Dms.Documents.Infrastructure.Persistence;

public sealed class RetentionPolicyRepository(DocumentsDbContext context) : IRetentionPolicyRepository
{
    public Task<RetentionPolicy?> FindAsync(RetentionPolicyId id, CancellationToken cancellationToken) =>
        context.RetentionPolicies.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<RetentionPolicy?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        context.RetentionPolicies.FirstOrDefaultAsync(item => item.Code == code, cancellationToken);

    public async Task<IReadOnlyList<RetentionPolicy>> ListAsync(CancellationToken cancellationToken) =>
        await context.RetentionPolicies.AsNoTracking().ToListAsync(cancellationToken);

    public void Add(RetentionPolicy policy) => context.RetentionPolicies.Add(policy);
}

public sealed class LegalHoldRepository(DocumentsDbContext context) : ILegalHoldRepository
{
    public Task<LegalHold?> FindAsync(LegalHoldId id, CancellationToken cancellationToken) =>
        context.LegalHolds.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task<IReadOnlyList<LegalHold>> ListForDocumentAsync(
        DocumentId documentId,
        bool includeReleased,
        CancellationToken cancellationToken)
    {
        var query = context.LegalHolds.AsNoTracking().Where(item => item.DocumentId == documentId);
        if (!includeReleased)
        {
            query = query.Where(item => item.ReleasedAt == null);
        }

        return await query.OrderByDescending(item => item.CreatedAt).ToListAsync(cancellationToken);
    }

    public Task<bool> HasActiveHoldAsync(DocumentId documentId, CancellationToken cancellationToken) =>
        context.LegalHolds.AnyAsync(
            item => item.DocumentId == documentId && item.ReleasedAt == null,
            cancellationToken);

    public void Add(LegalHold hold) => context.LegalHolds.Add(hold);
}
