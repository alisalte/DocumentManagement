using Dms.Documents.Application;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Dms.Documents.Infrastructure.Persistence;

public sealed class RecordRepository(DocumentsDbContext context) : IRecordRepository
{
    public Task<ManagedRecord?> FindAsync(RecordId id, CancellationToken cancellationToken) =>
        context.Records.FirstOrDefaultAsync(record => record.Id == id, cancellationToken);

    public Task<ManagedRecord?> FindByDocumentAsync(DocumentId documentId, CancellationToken cancellationToken) =>
        context.Records.FirstOrDefaultAsync(record => record.DocumentId == documentId, cancellationToken);

    public Task<bool> ExistsForDocumentAsync(DocumentId documentId, CancellationToken cancellationToken) =>
        context.Records.AnyAsync(record => record.DocumentId == documentId, cancellationToken);

    public void Add(ManagedRecord record) => context.Records.Add(record);

    public Task<RecordClass?> FindClassAsync(RecordClassId id, CancellationToken cancellationToken) =>
        context.RecordClasses.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<RecordClass?> FindClassByCodeAsync(string code, CancellationToken cancellationToken) =>
        context.RecordClasses.FirstOrDefaultAsync(item => item.Code == code, cancellationToken);

    public async Task<IReadOnlyList<RecordClass>> ListClassesAsync(CancellationToken cancellationToken) =>
        await context.RecordClasses.AsNoTracking().ToListAsync(cancellationToken);

    public void AddClass(RecordClass recordClass) => context.RecordClasses.Add(recordClass);

    public Task<RecordSeries?> FindSeriesAsync(RecordSeriesId id, CancellationToken cancellationToken) =>
        context.RecordSeries.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<RecordSeries?> FindSeriesByCodeAsync(
        RecordClassId classId,
        string code,
        CancellationToken cancellationToken) =>
        context.RecordSeries.FirstOrDefaultAsync(
            item => item.RecordClassId == classId && item.Code == code,
            cancellationToken);

    public async Task<IReadOnlyList<RecordSeries>> ListSeriesAsync(
        RecordClassId? classId,
        CancellationToken cancellationToken)
    {
        var query = context.RecordSeries.AsNoTracking().AsQueryable();
        if (classId is { } id)
        {
            query = query.Where(item => item.RecordClassId == id);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public void AddSeries(RecordSeries series) => context.RecordSeries.Add(series);
}

public sealed class RecordReadModel(DocumentsDbContext context) : IRecordReadModel
{
    public Task<RecordSummaryDto?> GetByDocumentAsync(DocumentId documentId, CancellationToken cancellationToken) =>
        ProjectAsync(context.Records.AsNoTracking().Where(record => record.DocumentId == documentId), cancellationToken);

    public Task<RecordSummaryDto?> GetAsync(RecordId recordId, CancellationToken cancellationToken) =>
        ProjectAsync(context.Records.AsNoTracking().Where(record => record.Id == recordId), cancellationToken);

    private async Task<RecordSummaryDto?> ProjectAsync(
        IQueryable<ManagedRecord> query,
        CancellationToken cancellationToken)
    {
        var row = await (
            from record in query
            join recordClass in context.RecordClasses.AsNoTracking() on record.RecordClassId equals recordClass.Id
            join version in context.DocumentVersions.AsNoTracking() on record.FinalVersionId equals version.Id
            join series in context.RecordSeries.AsNoTracking() on record.RecordSeriesId equals series.Id into seriesGroup
            from series in seriesGroup.DefaultIfEmpty()
            select new
            {
                record.Id,
                record.DocumentId,
                record.FinalVersionId,
                VersionNumber = version.VersionNumber,
                RevisionNumber = version.RevisionNumber,
                ClassId = recordClass.Id,
                ClassCode = recordClass.Code,
                ClassName = recordClass.Name,
                SeriesId = series != null ? series.Id : (RecordSeriesId?)null,
                SeriesCode = series != null ? series.Code : null,
                record.Status,
                record.DeclaredAt,
                record.DeclaredBy,
                record.MetadataFrozenAt,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new RecordSummaryDto(
            row.Id.Value,
            row.DocumentId.Value,
            row.FinalVersionId.Value,
            $"V{row.VersionNumber}.{row.RevisionNumber}",
            row.ClassId.Value,
            row.ClassCode,
            row.ClassName,
            row.SeriesId?.Value,
            row.SeriesCode,
            row.Status.ToString(),
            row.DeclaredAt,
            row.DeclaredBy.Value,
            row.MetadataFrozenAt);
    }
}
