using Dms.Application;
using Dms.Documents.Application;
using Dms.Documents.Contracts;
using Dms.Documents.Infrastructure.Persistence;
using Dms.SharedKernel;
using Dms.Storage.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Dms.Documents.Infrastructure;

public sealed class DocumentIndexSource(DocumentsDbContext context) : IDocumentIndexSource
{
    public async Task<DocumentIndexData?> GetAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var id = new DocumentId(documentId);
        var document = await context.Documents.IgnoreQueryFilters().AsNoTracking()
            .Include(candidate => candidate.Versions)
            .Include(candidate => candidate.Tags)
            .AsSplitQuery()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (document is null)
        {
            return null;
        }

        var tagIds = document.Tags.Select(tag => tag.TagId).ToArray();
        var tags = await context.Tags.AsNoTracking()
            .Where(tag => tagIds.Contains(tag.Id))
            .Select(tag => tag.Name)
            .ToListAsync(cancellationToken);

        // The category and every ancestor, so "this folder and below" is one term filter.
        var path = await context.Categories.AsNoTracking()
            .Where(category => category.Id == document.CategoryId)
            .Select(category => category.Path)
            .FirstOrDefaultAsync(cancellationToken);
        var categoryPath = new List<Guid> { document.CategoryId.Value };
        if (path is not null)
        {
            categoryPath.AddRange(Domain.Category.ParseAncestors(path, document.CategoryId));
        }

        return new DocumentIndexData(
            document.Id.Value,
            document.Title,
            document.Description,
            document.CategoryId.Value,
            categoryPath,
            document.DocumentTypeId.Value,
            document.OwnerId.Value,
            document.CreatedBy.Value,
            document.CreatedAt,
            document.UpdatedAt,
            document.IsDeleted,
            document.CurrentVersionId?.Value,
            document.EffectiveVersionId?.Value,
            tags,
            document.Versions
                .OrderBy(version => version.VersionNumber)
                .ThenBy(version => version.RevisionNumber)
                .Select(version => new VersionIndexData(
                    version.Id.Value,
                    version.VersionNumber,
                    version.RevisionNumber,
                    version.StorageObjectId.Value,
                    version.FileName,
                    version.MimeType,
                    version.DocumentTypeVersionId.Value,
                    version.DynamicData,
                    version.ApprovalStatus,
                    version.CreatedBy.Value,
                    version.CreatedAt))
                .ToList());
    }

    public async Task<IReadOnlyList<Guid>> DocumentsUsingObjectAsync(Guid storageObjectId, CancellationToken cancellationToken)
    {
        var id = new StorageObjectId(storageObjectId);
        var ids = await context.DocumentVersions.AsNoTracking()
            .Where(version => version.StorageObjectId == id)
            .Select(version => version.DocumentId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ids.Select(document => document.Value).ToList();
    }

    public async Task<IReadOnlyList<Guid>> ListIdsAsync(Guid? after, int batchSize, CancellationToken cancellationToken)
    {
        // Keyset paging on the uuid primary key; UUIDv7 keeps this roughly in creation order.
        var cursor = after ?? Guid.Empty;
        return await context.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM documents.documents WHERE id > {cursor} ORDER BY id LIMIT {batchSize}")
            .ToListAsync(cancellationToken);
    }
}

/// <summary>Degraded search over titles, with exactly the browser's access rules.</summary>
public sealed class DocumentTitleSearch(IDispatcher dispatcher) : IDocumentTitleSearch
{
    public async Task<(IReadOnlyList<TitleHit> Hits, int Total)> SearchAsync(string? text, int page, int pageSize, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(
            new ListDocumentsQuery(null, false, text, null, page, pageSize),
            cancellationToken);

        if (result.IsFailure)
        {
            return ([], 0);
        }

        return (result.Value.Items
            .Select(item => new TitleHit(item.Id, item.Title, item.CurrentVersionLabel, item.FileName, item.UpdatedAt))
            .ToList(), result.Value.Total);
    }
}
