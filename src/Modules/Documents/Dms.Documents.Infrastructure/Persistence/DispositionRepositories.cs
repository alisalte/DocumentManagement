using Dms.Documents.Application;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace Dms.Documents.Infrastructure.Persistence;

public sealed class DispositionRepository(DocumentsDbContext context) : IDispositionRepository
{
    public Task<Disposition?> FindAsync(DispositionId id, CancellationToken cancellationToken) =>
        context.Dispositions.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<Disposition?> FindActiveForRecordAsync(RecordId recordId, CancellationToken cancellationToken) =>
        context.Dispositions.FirstOrDefaultAsync(
            item => item.RecordId == recordId
                && (item.Status == DispositionStatus.PendingReview || item.Status == DispositionStatus.Approved),
            cancellationToken);

    public Task<Disposition?> FindLatestForRecordAsync(RecordId recordId, CancellationToken cancellationToken) =>
        context.Dispositions
            .Where(item => item.RecordId == recordId)
            .OrderByDescending(item => item.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(Disposition disposition) => context.Dispositions.Add(disposition);
}

public sealed class DestructionCertificateRepository(DocumentsDbContext context) : IDestructionCertificateRepository
{
    public Task<DestructionCertificate?> FindAsync(DestructionCertificateId id, CancellationToken cancellationToken) =>
        context.DestructionCertificates.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<DestructionCertificate?> FindByDispositionAsync(
        DispositionId dispositionId,
        CancellationToken cancellationToken) =>
        context.DestructionCertificates.FirstOrDefaultAsync(
            item => item.DispositionId == dispositionId,
            cancellationToken);

    public Task<DestructionCertificate?> FindByRecordAsync(RecordId recordId, CancellationToken cancellationToken) =>
        context.DestructionCertificates.FirstOrDefaultAsync(
            item => item.RecordId == recordId,
            cancellationToken);

    public void Add(DestructionCertificate certificate) => context.DestructionCertificates.Add(certificate);
}

public sealed class DispositionReadModel(DocumentsDbContext context) : IDispositionReadModel
{
    public async Task<DispositionDto?> GetAsync(DispositionId id, CancellationToken cancellationToken)
    {
        var item = await context.Dispositions.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        return item is null ? null : ToDto(item);
    }

    public async Task<DispositionDto?> GetByRecordAsync(RecordId recordId, CancellationToken cancellationToken)
    {
        var item = await context.Dispositions.AsNoTracking()
            .Where(row => row.RecordId == recordId)
            .OrderByDescending(row => row.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return item is null ? null : ToDto(item);
    }

    public async Task<IReadOnlyList<PendingDisposalItemDto>> ListPendingDisposalAsync(CancellationToken cancellationToken)
    {
        var rows = await (
            from record in context.Records.AsNoTracking()
            where record.Status == RecordStatus.PendingDisposal || record.Status == RecordStatus.Destroyed
            join version in context.DocumentVersions.AsNoTracking() on record.FinalVersionId equals version.Id
            select new
            {
                record.Id,
                record.DocumentId,
                record.Title,
                record.Status,
                record.RetentionExpiresAt,
                record.RetentionPolicyId,
                record.RetentionPolicyVersion,
                record.FinalVersionId,
                VersionNumber = version.VersionNumber,
                RevisionNumber = version.RevisionNumber,
            })
            .OrderBy(row => row.RetentionExpiresAt)
            .ThenBy(row => row.Title)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var documentIds = rows.Select(row => row.DocumentId).Distinct().ToList();
        var recordIds = rows.Select(row => row.Id).ToList();

        var heldDocuments = await context.LegalHolds.AsNoTracking()
            .Where(hold => documentIds.Contains(hold.DocumentId) && hold.ReleasedAt == null)
            .Select(hold => hold.DocumentId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var held = heldDocuments.ToHashSet();

        var dispositions = await context.Dispositions.AsNoTracking()
            .Where(item => recordIds.Contains(item.RecordId))
            .OrderByDescending(item => item.RequestedAt)
            .ToListAsync(cancellationToken);
        var latestByRecord = dispositions
            .GroupBy(item => item.RecordId)
            .ToDictionary(group => group.Key, group => group.First());

        return rows.Select(row =>
        {
            latestByRecord.TryGetValue(row.Id, out var disposition);
            var active = disposition is not null
                && disposition.Status is DispositionStatus.PendingReview or DispositionStatus.Approved
                ? disposition
                : null;
            return new PendingDisposalItemDto(
                row.Id.Value,
                row.DocumentId.Value,
                row.Title,
                row.Status.ToString(),
                row.RetentionExpiresAt,
                row.RetentionPolicyId?.Value,
                row.RetentionPolicyVersion,
                held.Contains(row.DocumentId),
                // Only an in-flight disposition blocks a new request; show latest status for context.
                active?.Id.Value,
                active?.Status.ToString() ?? disposition?.Status.ToString(),
                row.FinalVersionId.Value,
                $"V{row.VersionNumber}.{row.RevisionNumber}");
        }).ToList();
    }

    public async Task<DestructionCertificateDto?> GetCertificateAsync(
        DestructionCertificateId id,
        CancellationToken cancellationToken)
    {
        var item = await context.DestructionCertificates.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        return item is null ? null : ToCertificateDto(item);
    }

    public async Task<DestructionCertificateDto?> GetCertificateByRecordAsync(
        RecordId recordId,
        CancellationToken cancellationToken)
    {
        var item = await context.DestructionCertificates.AsNoTracking()
            .FirstOrDefaultAsync(row => row.RecordId == recordId, cancellationToken);
        return item is null ? null : ToCertificateDto(item);
    }

    private static DispositionDto ToDto(Disposition item) => new(
        item.Id.Value,
        item.RecordId.Value,
        item.DocumentId.Value,
        item.Status.ToString(),
        item.RequestReason,
        item.RequestedBy.Value,
        item.RequestedAt,
        item.ReviewedBy?.Value,
        item.ReviewedAt,
        item.DecisionReason,
        item.ApprovedBy?.Value,
        item.ApprovedAt,
        item.DestroyedBy?.Value,
        item.DestroyedAt);

    private static DestructionCertificateDto ToCertificateDto(DestructionCertificate item) => new(
        item.Id.Value,
        item.CertificateNumber,
        item.DispositionId.Value,
        item.RecordId.Value,
        item.DocumentId.Value,
        item.FinalVersionId.Value,
        item.FinalVersionLabel,
        item.RecordTitle,
        Convert.ToHexStringLower(item.ContentSha256),
        item.RetentionPolicyId?.Value,
        item.RetentionPolicyVersion,
        item.RetentionExpiresAt,
        item.LegalHoldCheckedAt,
        item.ApprovedBy.Value,
        item.ApprovedAt,
        item.DestroyedBy.Value,
        item.DestroyedAt,
        item.Reason,
        Convert.ToHexStringLower(item.CertificateHash),
        item.CreatedAt);
}
