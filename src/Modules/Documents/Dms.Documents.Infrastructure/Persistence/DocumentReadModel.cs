using Dms.Application;
using Dms.Authorization.Contracts;
using Dms.Documents.Application;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Dms.Documents.Infrastructure.Persistence;

public sealed class DocumentReadModel(DocumentsDbContext context) : IDocumentReadModel
{
    public async Task<PagedResult<DocumentListItemDto>> ListAsync(
        DocumentListFilter filter,
        AccessScope scope,
        UserId viewer,
        CancellationToken cancellationToken)
    {
        var query = ApplyScope(context.Documents.AsNoTracking(), scope);

        // Decision D6: a document with nothing published yet is listed only for its author.
        // VIEW_DRAFT holders and reviewers get the finer rule when workflows land (phase 4).
        query = query.Where(document => document.EffectiveVersionId != null || document.CreatedBy == viewer);

        if (filter.CategoryId is { } categoryId)
        {
            if (filter.IncludeSubcategories)
            {
                var root = await context.Categories.AsNoTracking()
                    .Where(category => category.Id == new CategoryId(categoryId))
                    .Select(category => category.Path)
                    .FirstOrDefaultAsync(cancellationToken);

                if (root is null)
                {
                    return new PagedResult<DocumentListItemDto>([], 0, filter.Page, filter.PageSize);
                }

                var subtree = context.Categories
                    .FromSql($"SELECT * FROM documents.categories WHERE path <@ {root}::ltree")
                    .Select(category => category.Id);

                query = query.Where(document => subtree.Contains(document.CategoryId));
            }
            else
            {
                query = query.Where(document => document.CategoryId == new CategoryId(categoryId));
            }
        }

        if (filter.Search is { } search)
        {
            var pattern = $"%{EscapeLike(search)}%";
            query = query.Where(document => EF.Functions.ILike(document.Title, pattern, "\\"));
        }

        if (filter.TagId is { } tagId)
        {
            query = query.Where(document => document.Tags.Any(tag => tag.TagId == new TagId(tagId)));
        }

        return await PageAsync(query.OrderByDescending(document => document.UpdatedAt), filter.Page, filter.PageSize, cancellationToken);
    }

    public async Task<PagedResult<DocumentListItemDto>> ListDeletedAsync(
        AccessScope? scope,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = context.Documents.IgnoreQueryFilters().AsNoTracking()
            .Where(document => document.DeletedAt != null);

        if (scope is not null)
        {
            query = ApplyScope(query, scope);
        }

        return await PageAsync(query.OrderByDescending(document => document.DeletedAt), page, pageSize, cancellationToken);
    }

    public async Task<DocumentDetailsDto?> GetAsync(DocumentId id, CancellationToken cancellationToken)
    {
        var row = await context.Documents.AsNoTracking()
            .Where(document => document.Id == id)
            .Select(document => new
            {
                Document = document,
                CategoryName = context.Categories
                    .Where(category => category.Id == document.CategoryId)
                    .Select(category => category.Name)
                    .FirstOrDefault(),
                Tags = document.Tags
                    .Join(context.Tags, link => link.TagId, tag => tag.Id, (link, tag) => new { tag.Id, tag.Name })
                    .ToList(),
            })
            .AsSplitQuery()
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var document = row.Document;
        return new DocumentDetailsDto(
            document.Id.Value,
            document.Title,
            document.Description,
            document.CategoryId.Value,
            row.CategoryName ?? string.Empty,
            document.DocumentTypeId.Value,
            document.OwnerId.Value,
            document.CurrentVersionId?.Value,
            document.EffectiveVersionId?.Value,
            document.LatestVersionNumber,
            document.Status.ToString(),
            row.Tags.OrderBy(tag => tag.Name).Select(tag => new TagDto(tag.Id.Value, tag.Name)).ToList(),
            document.CreatedBy.Value,
            document.CreatedAt,
            document.UpdatedBy.Value,
            document.UpdatedAt,
            document.DeletedAt,
            document.DeleteReason);
    }

    public async Task<IReadOnlyList<DocumentVersionDto>> ListVersionsAsync(
        DocumentId id,
        CancellationToken cancellationToken)
    {
        var versions = await context.DocumentVersions.AsNoTracking()
            .Where(version => version.DocumentId == id)
            .OrderByDescending(version => version.VersionNumber)
            .ThenByDescending(version => version.RevisionNumber)
            .ToListAsync(cancellationToken);

        return versions
            .Select(version => new DocumentVersionDto(
                version.Id.Value,
                version.VersionNumber,
                version.RevisionNumber,
                version.Label,
                version.StorageObjectId.Value,
                version.FileName,
                version.MimeType,
                version.FileSize,
                Convert.ToHexStringLower(version.Sha256),
                version.ChangeKind.ToString(),
                version.ChangeDescription,
                version.ApprovalStatus.ToString(),
                version.IsPublished,
                version.CreatedBy.Value,
                version.CreatedAt))
            .ToList();
    }

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken cancellationToken) =>
        await context.Categories.AsNoTracking()
            .OrderBy(category => category.Depth)
            .ThenBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new CategoryDto(
                category.Id.Value,
                category.ParentId == null ? null : category.ParentId.Value.Value,
                category.Name,
                category.Code,
                category.Description,
                category.Depth,
                category.IsActive,
                category.SortOrder))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TagDto>> ListTagsAsync(string? search, int limit, CancellationToken cancellationToken)
    {
        var query = context.Tags.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(TextNormalizer.Normalize(search))}%";
            query = query.Where(tag => EF.Functions.Like(tag.NormalizedName, pattern, "\\"));
        }

        return await query
            .OrderBy(tag => tag.Name)
            .Take(limit)
            .Select(tag => new TagDto(tag.Id.Value, tag.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DuplicateCandidate>> FindByFileHashAsync(
        byte[] sha256,
        CancellationToken cancellationToken) =>
        await context.Documents.AsNoTracking()
            .Where(document => document.Versions.Any(version => version.Sha256 == sha256))
            .OrderByDescending(document => document.UpdatedAt)
            .Take(20)
            .Select(document => new DuplicateCandidate(document.Id.Value, document.Title, document.CategoryId.Value))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Section 5.7, as SQL: allowed through a category or directly, and denied through neither.
    /// Exactly <see cref="AccessScope.Includes"/>; the two are kept side by side on purpose.
    /// </summary>
    private static IQueryable<Document> ApplyScope(IQueryable<Document> query, AccessScope scope)
    {
        var allowedCategories = scope.AllowedCategories.Select(id => new CategoryId(id)).ToArray();
        var deniedCategories = scope.DeniedCategories.Select(id => new CategoryId(id)).ToArray();
        var allowedDocuments = scope.AllowedResources.Select(id => new DocumentId(id)).ToArray();
        var deniedDocuments = scope.DeniedResources.Select(id => new DocumentId(id)).ToArray();

        return query.Where(document =>
            (allowedCategories.Contains(document.CategoryId) || allowedDocuments.Contains(document.Id))
            && !deniedCategories.Contains(document.CategoryId)
            && !deniedDocuments.Contains(document.Id));
    }

    private async Task<PagedResult<DocumentListItemDto>> PageAsync(
        IQueryable<Document> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(document => new
            {
                Document = document,
                Current = context.DocumentVersions
                    .Where(version => version.Id == document.CurrentVersionId)
                    .Select(version => new { version.VersionNumber, version.RevisionNumber, version.FileName, version.MimeType, version.FileSize })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<DocumentListItemDto>(
            items.Select(item => new DocumentListItemDto(
                item.Document.Id.Value,
                item.Document.Title,
                item.Document.CategoryId.Value,
                item.Document.DocumentTypeId.Value,
                item.Document.OwnerId.Value,
                item.Current is null ? null : $"V{item.Current.VersionNumber}.{item.Current.RevisionNumber}",
                item.Current?.FileName,
                item.Current?.MimeType,
                item.Current?.FileSize,
                item.Document.UpdatedAt,
                item.Document.DeletedAt,
                item.Document.DeleteReason)).ToList(),
            total,
            page,
            pageSize);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
