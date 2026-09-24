using Dms.Documents.Application;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Dms.Documents.Infrastructure.Persistence;

public sealed class CategoryRepository(DocumentsDbContext context) : ICategoryRepository
{
    public Task<Category?> FindAsync(CategoryId id, CancellationToken cancellationToken) =>
        context.Categories.FirstOrDefaultAsync(category => category.Id == id, cancellationToken);

    public Task<Category?> FindRootAsync(CancellationToken cancellationToken) =>
        context.Categories
            .OrderBy(category => category.CreatedAt)
            .FirstOrDefaultAsync(category => category.ParentId == null, cancellationToken);

    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken) =>
        await context.Categories.AsNoTracking()
            .OrderBy(category => category.Depth)
            .ThenBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken);

    public Task<Category?> FindSiblingByCodeAsync(
        CategoryId? parentId,
        string code,
        CancellationToken cancellationToken) =>
        context.Categories.FirstOrDefaultAsync(
            category => category.ParentId == parentId && category.Code == code,
            cancellationToken);

    public Task<bool> HasChildrenAsync(CategoryId id, CancellationToken cancellationToken) =>
        context.Categories.AnyAsync(category => category.ParentId == id, cancellationToken);

    public Task<bool> HasDocumentsAsync(CategoryId id, CancellationToken cancellationToken) =>
        context.Documents.IgnoreQueryFilters().AnyAsync(document => document.CategoryId == id, cancellationToken);

    public async Task<int> GetSubtreeHeightAsync(Category category, CancellationToken cancellationToken)
    {
        // nlevel counts labels, so the deepest descendant minus this category's own level.
        var deepest = await context.Database
            .SqlQuery<int>($"""
                SELECT coalesce(max(nlevel(path)), 0) AS "Value"
                  FROM documents.categories
                 WHERE path <@ {category.Path}::ltree
                """)
            .SingleAsync(cancellationToken);

        return Math.Max(deepest - (category.Depth + 1), 0);
    }

    public void Add(Category category) => context.Categories.Add(category);

    public async Task RepathDescendantsAsync(string oldPath, string newPath, CancellationToken cancellationToken)
    {
        // One statement for the whole subtree. The moved category itself is saved by EF; only its
        // descendants (path strictly below the old path) are rewritten here.
        await context.Database.ExecuteSqlAsync(
            $"""
            UPDATE documents.categories
               SET path = {newPath}::ltree || subpath(path, nlevel({oldPath}::ltree)),
                   depth = nlevel({newPath}::ltree || subpath(path, nlevel({oldPath}::ltree))) - 1
             WHERE path <@ {oldPath}::ltree
               AND path <> {oldPath}::ltree
            """,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListDocumentIdsInSubtreeAsync(string path, CancellationToken cancellationToken) =>
        await context.Database
            .SqlQuery<Guid>($"""
                SELECT d.id AS "Value"
                  FROM documents.documents d
                  JOIN documents.categories c ON c.id = d.category_id
                 WHERE c.path <@ {path}::ltree
                """)
            .ToListAsync(cancellationToken);
}

public sealed class DocumentRepository(DocumentsDbContext context) : IDocumentRepository
{
    public Task<Document?> FindAsync(DocumentId id, CancellationToken cancellationToken) =>
        context.Documents
            .Include(document => document.Tags)
            .FirstOrDefaultAsync(document => document.Id == id, cancellationToken);

    public Task<Document?> FindIncludingDeletedAsync(DocumentId id, CancellationToken cancellationToken) =>
        context.Documents.IgnoreQueryFilters()
            .Include(document => document.Versions)
            .Include(document => document.Tags)
            .AsSplitQuery()
            .FirstOrDefaultAsync(document => document.Id == id, cancellationToken);

    public async Task<Document?> FindForUpdateAsync(DocumentId id, CancellationToken cancellationToken)
    {
        // Take the row lock before EF reads, so the version counter we load is the one we will
        // increment. A second writer blocks here until the first commits, then reads its result.
        var locked = await context.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value"
                  FROM documents.documents
                 WHERE id = {id.Value} AND deleted_at IS NULL
                   FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (locked.Count == 0)
        {
            return null;
        }

        return await context.Documents
            .Include(document => document.Versions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(document => document.Id == id, cancellationToken);
    }

    public Task<DocumentVersion?> FindVersionAsync(DocumentVersionId versionId, CancellationToken cancellationToken) =>
        context.DocumentVersions.AsNoTracking()
            .FirstOrDefaultAsync(version => version.Id == versionId, cancellationToken);

    public void Add(Document document) => context.Documents.Add(document);

    public Task AllowInPlaceMetadataEditAsync(CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlRawAsync("SELECT set_config('dms.metadata_in_place', 'on', true)", cancellationToken);

    public async Task PurgeAsync(Document document, CancellationToken cancellationToken)
    {
        // Transaction-local switch read by the immutability trigger. Outside a purge, no code path
        // can delete a version row, bug or not.
        await context.Database.ExecuteSqlRawAsync(
            "SELECT set_config('dms.purge', 'on', true)",
            cancellationToken);

        context.DocumentVersions.RemoveRange(document.Versions);
        context.Documents.Remove(document);

        // Flushed now, while the switch is on and before anything else joins the transaction.
        await context.SaveChangesAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "SELECT set_config('dms.purge', 'off', true)",
            cancellationToken);
    }
}

public sealed class TagRepository(DocumentsDbContext context) : ITagRepository
{
    public async Task<IReadOnlyList<Tag>> FindByNormalizedAsync(
        IReadOnlyCollection<string> normalizedNames,
        CancellationToken cancellationToken)
    {
        if (normalizedNames.Count == 0)
        {
            return [];
        }

        var names = normalizedNames.ToArray();
        return await context.Tags
            .Where(tag => names.Contains(tag.NormalizedName))
            .ToListAsync(cancellationToken);
    }

    public void Add(Tag tag) => context.Tags.Add(tag);
}
