using Dms.Documents.Application;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Dms.Documents.Infrastructure.Persistence;

public sealed class ImportJobRepository(DocumentsDbContext context) : IImportJobRepository
{
    public Task<ImportJob?> FindAsync(ImportJobId id, CancellationToken cancellationToken) =>
        context.ImportJobs.FirstOrDefaultAsync(job => job.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ImportJob>> ListAsync(int take, CancellationToken cancellationToken) =>
        await context.ImportJobs.AsNoTracking()
            .OrderByDescending(job => job.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);

    public void Add(ImportJob job) => context.ImportJobs.Add(job);
}

public sealed class ImportItemRepository(DocumentsDbContext context) : IImportItemRepository
{
    public Task<ImportItem?> FindAsync(ImportItemId id, CancellationToken cancellationToken) =>
        context.ImportItems.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ImportItem>> ListByJobAsync(
        ImportJobId jobId,
        ImportItemStatus? status,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = context.ImportItems.AsQueryable().Where(item => item.ImportJobId == jobId);
        if (status is { } filter)
        {
            query = query.Where(item => item.Status == filter);
        }

        return await query
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, 10_000))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ImportItem>> ClaimBatchAsync(
        ImportJobId jobId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        // SKIP LOCKED: concurrent workers cannot claim the same Ready item.
        var size = Math.Clamp(batchSize, 1, 50);
        var ids = await context.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value"
                  FROM documents.import_items
                 WHERE import_job_id = {jobId.Value}
                   AND status IN ('Ready', 'Retryable')
                 ORDER BY created_at, id
                 FOR UPDATE SKIP LOCKED
                 LIMIT {size}
                """)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return [];
        }

        var claimed = new List<ImportItem>(ids.Count);
        foreach (var id in ids)
        {
            var item = await context.ImportItems
                .FirstOrDefaultAsync(candidate => candidate.Id == new ImportItemId(id), cancellationToken);
            if (item is not null)
            {
                claimed.Add(item);
            }
        }

        return claimed;
    }

    public Task<int> CountByStatusAsync(ImportJobId jobId, ImportItemStatus status, CancellationToken cancellationToken) =>
        context.ImportItems.CountAsync(
            item => item.ImportJobId == jobId && item.Status == status,
            cancellationToken);

    public void AddRange(IEnumerable<ImportItem> items) => context.ImportItems.AddRange(items);
}

public sealed class ImportMappingRepository(DocumentsDbContext context) : IImportMappingRepository
{
    public async Task<IReadOnlyList<ImportMapping>> ListForJobAsync(
        string sourceSystem,
        ImportJobId? jobId,
        CancellationToken cancellationToken) =>
        await context.ImportMappings.AsNoTracking()
            .Where(mapping => mapping.SourceSystem == sourceSystem
                && (mapping.ImportJobId == null || mapping.ImportJobId == jobId))
            .ToListAsync(cancellationToken);

    public async Task<string?> ResolveAsync(
        string sourceSystem,
        ImportJobId? jobId,
        ImportMappingKind kind,
        string sourceKey,
        CancellationToken cancellationToken)
    {
        var key = sourceKey.Trim();
        // Job-scoped mapping wins over source-system-wide mapping.
        var jobMapped = jobId is null
            ? null
            : await context.ImportMappings.AsNoTracking()
                .Where(mapping => mapping.SourceSystem == sourceSystem
                    && mapping.ImportJobId == jobId
                    && mapping.Kind == kind
                    && mapping.SourceKey == key)
                .Select(mapping => mapping.TargetKey)
                .FirstOrDefaultAsync(cancellationToken);
        if (jobMapped is not null)
        {
            return jobMapped;
        }

        return await context.ImportMappings.AsNoTracking()
            .Where(mapping => mapping.SourceSystem == sourceSystem
                && mapping.ImportJobId == null
                && mapping.Kind == kind
                && mapping.SourceKey == key)
            .Select(mapping => mapping.TargetKey)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public void Add(ImportMapping mapping) => context.ImportMappings.Add(mapping);
}

public sealed class ImportSourceIndexRepository(DocumentsDbContext context) : IImportSourceIndexRepository
{
    public Task<ImportSourceIndex?> FindAsync(
        string sourceSystem,
        string sourceId,
        CancellationToken cancellationToken) =>
        context.ImportSourceIndexes.FirstOrDefaultAsync(
            row => row.SourceSystem == sourceSystem && row.SourceId == sourceId,
            cancellationToken);

    public void Add(ImportSourceIndex index) => context.ImportSourceIndexes.Add(index);
}

public sealed class ClassificationLevelRepository(DocumentsDbContext context) : IClassificationLevelRepository
{
    public Task<ClassificationLevel?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        context.ClassificationLevels.FirstOrDefaultAsync(
            level => level.Code == code,
            cancellationToken);

    public async Task<IReadOnlyList<ClassificationLevel>> ListAsync(CancellationToken cancellationToken) =>
        await context.ClassificationLevels.AsNoTracking()
            .OrderBy(level => level.Rank)
            .ThenBy(level => level.Code)
            .ToListAsync(cancellationToken);

    public void Add(ClassificationLevel level) => context.ClassificationLevels.Add(level);

    public void Assign(DocumentClassification assignment)
    {
        var existing = context.DocumentClassifications
            .FirstOrDefault(row => row.DocumentId == assignment.DocumentId);
        if (existing is not null)
        {
            context.DocumentClassifications.Remove(existing);
        }

        context.DocumentClassifications.Add(assignment);
    }

    public Task<DocumentClassification?> FindAssignmentAsync(DocumentId documentId, CancellationToken cancellationToken) =>
        context.DocumentClassifications.FirstOrDefaultAsync(
            row => row.DocumentId == documentId,
            cancellationToken);
}
