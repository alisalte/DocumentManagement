using Dms.Authorization.Contracts;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.Documents.Infrastructure.Persistence;
using Dms.SharedKernel;
using Dms.Storage.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Dms.Documents.Infrastructure;

/// <summary>
/// Tells the permission evaluator where a resource sits in the category tree and what state it
/// is in. Replaces the phase 1 flat stand-in.
///
/// Two views of a document, matching the two kinds of question:
/// - <see cref="DescribeAsync"/> answers metadata questions ("may I see this document?"). The
///   malware scan is irrelevant there: the uploader must be able to see why a file is blocked.
/// - <see cref="DescribeVersionAsync"/> answers content questions ("may I download V3?"), and
///   carries that version's draft state and scan result (decisions D6 and D9).
/// </summary>
public sealed class DocumentResourceHierarchy(DocumentsDbContext context, IStorageService storage)
    : IResourceHierarchy
{
    public async Task<ResourceDescriptor?> DescribeAsync(ResourceRef resource, CancellationToken cancellationToken)
    {
        if (resource.Type == ResourceType.Category)
        {
            var category = await context.Categories.AsNoTracking()
                .Where(item => item.Id == new CategoryId(resource.Id))
                .Select(item => new { item.Id, item.Path })
                .FirstOrDefaultAsync(cancellationToken);

            return category is null
                ? null
                : new ResourceDescriptor(resource, Category.ParseAncestors(category.Path, category.Id));
        }

        var document = await FindDocumentAsync(resource.Id, cancellationToken);
        if (document is null)
        {
            return null;
        }

        // What a plain reader sees is the effective version. With none published yet, the
        // document is a draft of its author (decision D6).
        var author = document.EffectiveVersionId is null && document.CurrentVersionId is { } current
            ? await context.DocumentVersions.AsNoTracking()
                .Where(version => version.Id == current)
                .Select(version => (UserId?)version.CreatedBy)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return new ResourceDescriptor(
            resource,
            document.Ancestors,
            document.IsDeleted,
            document.EffectiveVersionId is null && document.CurrentVersionId is not null
                ? ContentState.Draft
                : ContentState.Published,
            ContentScanState.NotApplicable,
            author);
    }

    public async Task<ResourceDescriptor?> DescribeVersionAsync(
        ResourceRef resource,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        if (resource.Type != ResourceType.Document)
        {
            return await DescribeAsync(resource, cancellationToken);
        }

        var document = await FindDocumentAsync(resource.Id, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var version = await context.DocumentVersions.AsNoTracking()
            .Where(item => item.Id == new DocumentVersionId(versionId) && item.DocumentId == new DocumentId(resource.Id))
            .Select(item => new { item.StorageObjectId, item.ApprovalStatus, item.CreatedBy })
            .FirstOrDefaultAsync(cancellationToken);

        // A version of another document is simply unknown here: no cross-document probing.
        if (version is null)
        {
            return null;
        }

        var file = await storage.FindAsync(version.StorageObjectId, cancellationToken);
        var published = version.ApprovalStatus is ApprovalStatus.NotRequired or ApprovalStatus.Approved;

        return new ResourceDescriptor(
            resource,
            document.Ancestors,
            document.IsDeleted,
            published ? ContentState.Published : ContentState.Draft,
            ToScanState(file),
            version.CreatedBy,
            versionId);
    }

    public async Task<IReadOnlyList<CategoryNode>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        var rows = await context.Categories.AsNoTracking()
            .Select(category => new { category.Id, category.ParentId })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new CategoryNode(row.Id.Value, row.ParentId?.Value)).ToList();
    }

    private async Task<DocumentState?> FindDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        // Soft deleted documents are described too: the evaluator decides what they still allow.
        var row = await context.Documents.IgnoreQueryFilters().AsNoTracking()
            .Where(document => document.Id == new DocumentId(documentId))
            .Select(document => new
            {
                document.CategoryId,
                document.CurrentVersionId,
                document.EffectiveVersionId,
                document.DeletedAt,
                CategoryPath = context.Categories
                    .Where(category => category.Id == document.CategoryId)
                    .Select(category => category.Path)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        // Nearest first: the document's own category, then its parents up to the root.
        var ancestors = new List<Guid> { row.CategoryId.Value };
        if (row.CategoryPath is not null)
        {
            ancestors.AddRange(Category.ParseAncestors(row.CategoryPath, row.CategoryId));
        }

        return new DocumentState(ancestors, row.CurrentVersionId, row.EffectiveVersionId, row.DeletedAt is not null);
    }

    private static ContentScanState ToScanState(StorageObjectInfo? file) => file?.ScanStatus switch
    {
        ScanStatus.Clean or ScanStatus.Skipped => ContentScanState.Clean,
        ScanStatus.Pending => ContentScanState.Pending,
        ScanStatus.Infected => ContentScanState.Infected,

        // A missing file row is treated like a failed scan: never serve what cannot be vouched for.
        _ => ContentScanState.Failed,
    };

    private sealed record DocumentState(
        IReadOnlyList<Guid> Ancestors,
        DocumentVersionId? CurrentVersionId,
        DocumentVersionId? EffectiveVersionId,
        bool IsDeleted);
}

/// <summary>Minimal document lookup for other modules (sharing, workflow).</summary>
public sealed class DocumentLocator(DocumentsDbContext context) : IDocumentLocator
{
    public async Task<DocumentRef?> FindAsync(DocumentId id, CancellationToken cancellationToken)
    {
        var document = await context.Documents.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        return document?.ToRef();
    }
}

public sealed class DocumentVersionReader(DocumentsDbContext context) : IDocumentVersionReader
{
    public async Task<Guid?> FindDocumentTypeIdAsync(Guid documentId, CancellationToken cancellationToken) =>
        await context.Documents.IgnoreQueryFilters().AsNoTracking()
            .Where(document => document.Id == new DocumentId(documentId))
            .Select(document => (Guid?)document.DocumentTypeId.Value)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<VersionSummary?> FindAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken)
    {
        var found = await FindManyAsync([versionId], cancellationToken);
        return found.TryGetValue(versionId, out var version) && version.DocumentId == documentId ? version : null;
    }

    public async Task<IReadOnlyDictionary<Guid, VersionSummary>> FindManyAsync(
        IReadOnlyCollection<Guid> versionIds,
        CancellationToken cancellationToken)
    {
        if (versionIds.Count == 0)
        {
            return new Dictionary<Guid, VersionSummary>();
        }

        var ids = versionIds.Distinct().Select(id => new DocumentVersionId(id)).ToList();
        var versions = await context.DocumentVersions.AsNoTracking()
            .Where(version => ids.Contains(version.Id))
            .ToListAsync(cancellationToken);

        var documentIds = versions.Select(version => version.DocumentId).Distinct().ToList();
        var documents = await context.Documents.IgnoreQueryFilters().AsNoTracking()
            .Where(document => documentIds.Contains(document.Id))
            .Select(document => new { document.Id, document.Title, document.DocumentTypeId, document.DeletedAt })
            .ToDictionaryAsync(document => document.Id, cancellationToken);

        return versions
            .Where(version => documents.ContainsKey(version.DocumentId))
            .ToDictionary(
                version => version.Id.Value,
                version =>
                {
                    var document = documents[version.DocumentId];
                    return new VersionSummary(
                        version.DocumentId.Value,
                        document.Title,
                        document.DocumentTypeId.Value,
                        document.DeletedAt is not null,
                        version.Id.Value,
                        version.Label,
                        version.StorageObjectId.Value,
                        version.FileName,
                        version.MimeType,
                        version.FileSize,
                        version.IsPublished);
                });
    }
}
