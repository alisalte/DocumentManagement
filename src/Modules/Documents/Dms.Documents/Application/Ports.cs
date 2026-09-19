using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;

namespace Dms.Documents.Application;

public interface ICategoryRepository
{
    Task<Category?> FindAsync(CategoryId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken);

    Task<Category?> FindSiblingByCodeAsync(CategoryId? parentId, string code, CancellationToken cancellationToken);

    Task<bool> HasChildrenAsync(CategoryId id, CancellationToken cancellationToken);

    Task<bool> HasDocumentsAsync(CategoryId id, CancellationToken cancellationToken);

    void Add(Category category);

    /// <summary>
    /// Rewrites descendant paths after a move, in one statement. Loading a subtree to fix it row
    /// by row would be slow and could leave the tree half rewritten.
    /// </summary>
    Task RepathDescendantsAsync(string oldPath, string newPath, CancellationToken cancellationToken);
}

public interface IDocumentRepository
{
    Task<Document?> FindAsync(DocumentId id, CancellationToken cancellationToken);

    /// <summary>Loads the document with its versions, for operations that add or inspect them.</summary>
    Task<Document?> FindWithVersionsAsync(DocumentId id, CancellationToken cancellationToken);

    Task<DocumentVersion?> FindVersionAsync(DocumentVersionId versionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentVersion>> ListVersionsAsync(DocumentId id, CancellationToken cancellationToken);

    /// <summary>Other versions still pointing at the same stored file (metadata revisions).</summary>
    Task<int> CountVersionsUsingStorageObjectAsync(Guid storageObjectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentId>> FindByFileHashAsync(byte[] sha256, CancellationToken cancellationToken);

    void Add(Document document);

    void Remove(Document document);
}

public interface ITagRepository
{
    Task<IReadOnlyList<Tag>> FindByNormalizedAsync(
        IReadOnlyCollection<string> normalizedNames,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Tag>> ListAsync(string? search, int limit, CancellationToken cancellationToken);

    void Add(Tag tag);
}
