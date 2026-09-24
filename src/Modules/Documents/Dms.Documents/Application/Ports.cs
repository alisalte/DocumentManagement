using System.Text.Json;
using Dms.Application;
using Dms.Authorization.Contracts;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;

namespace Dms.Documents.Application;

public interface ICategoryRepository
{
    Task<Category?> FindAsync(CategoryId id, CancellationToken cancellationToken);

    /// <summary>The single root seeded by the migrator. Everything else hangs below it.</summary>
    Task<Category?> FindRootAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken);

    Task<Category?> FindSiblingByCodeAsync(CategoryId? parentId, string code, CancellationToken cancellationToken);

    Task<bool> HasChildrenAsync(CategoryId id, CancellationToken cancellationToken);

    Task<bool> HasDocumentsAsync(CategoryId id, CancellationToken cancellationToken);

    /// <summary>Levels below the category, 0 for a leaf. Used to keep a moved subtree within the depth limit.</summary>
    Task<int> GetSubtreeHeightAsync(Category category, CancellationToken cancellationToken);

    void Add(Category category);

    /// <summary>
    /// Rewrites descendant paths after a move, in one statement. Loading a subtree to fix it row
    /// by row would be slow and could leave the tree half rewritten.
    /// </summary>
    Task RepathDescendantsAsync(string oldPath, string newPath, CancellationToken cancellationToken);

    /// <summary>Every document (deleted ones too) filed in the category or anywhere below it.</summary>
    Task<IReadOnlyList<Guid>> ListDocumentIdsInSubtreeAsync(string path, CancellationToken cancellationToken);
}

public interface IDocumentRepository
{
    /// <summary>A live (not soft deleted) document, without its versions.</summary>
    Task<Document?> FindAsync(DocumentId id, CancellationToken cancellationToken);

    /// <summary>Any document, soft deleted or not, with its versions. Used by restore and purge.</summary>
    Task<Document?> FindIncludingDeletedAsync(DocumentId id, CancellationToken cancellationToken);

    /// <summary>
    /// A live document with its versions, row locked (<c>SELECT … FOR UPDATE</c>) until the
    /// transaction ends. Two users adding a version at the same moment are serialised here, so
    /// each gets the next number instead of one of them failing (section 4.11). The unique index
    /// on the version number stays as the final backstop.
    /// </summary>
    Task<Document?> FindForUpdateAsync(DocumentId id, CancellationToken cancellationToken);

    Task<DocumentVersion?> FindVersionAsync(DocumentVersionId versionId, CancellationToken cancellationToken);

    void Add(Document document);

    /// <summary>
    /// Declares that this transaction may rewrite version metadata in place (MetadataEditPolicy
    /// InPlace). Without it the immutability trigger refuses any change to dynamic_data.
    /// </summary>
    Task AllowInPlaceMetadataEditAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the document and its versions. The immutability trigger refuses to delete versions
    /// unless the transaction declared itself a purge first, which this method does.
    /// </summary>
    Task PurgeAsync(Document document, CancellationToken cancellationToken);
}

public interface ITagRepository
{
    Task<IReadOnlyList<Tag>> FindByNormalizedAsync(
        IReadOnlyCollection<string> normalizedNames,
        CancellationToken cancellationToken);

    void Add(Tag tag);
}

public sealed record DocumentListFilter(
    Guid? CategoryId,
    bool IncludeSubcategories,
    string? Search,
    Guid? TagId,
    int Page,
    int PageSize);

/// <summary>
/// Read side of the module: AsNoTracking projections straight to DTOs (CQRS without a library).
/// Every list method takes the caller's access scope and applies it in SQL, so rows the caller may
/// not see never leave the database.
/// </summary>
public interface IDocumentReadModel
{
    Task<PagedResult<DocumentListItemDto>> ListAsync(
        DocumentListFilter filter,
        AccessScope scope,
        UserId viewer,
        CancellationToken cancellationToken);

    /// <param name="scope">Null lists every deleted document (holders of DOCUMENT_PURGE).</param>
    Task<PagedResult<DocumentListItemDto>> ListDeletedAsync(
        AccessScope? scope,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<DocumentDetailsDto?> GetAsync(DocumentId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentVersionDto>> ListVersionsAsync(DocumentId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<TagDto>> ListTagsAsync(string? search, int limit, CancellationToken cancellationToken);

    /// <summary>Live documents with a version whose file has this SHA-256. The caller filters by VIEW.</summary>
    Task<IReadOnlyList<DuplicateCandidate>> FindByFileHashAsync(byte[] sha256, CancellationToken cancellationToken);
}

public sealed record DuplicateCandidate(Guid DocumentId, string Title, Guid CategoryId);

public sealed record CategoryDto(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Code,
    string? Description,
    int Depth,
    bool IsActive,
    int SortOrder);

public sealed record TagDto(Guid Id, string Name);

public sealed record DocumentListItemDto(
    Guid Id,
    string Title,
    Guid CategoryId,
    Guid DocumentTypeId,
    Guid OwnerId,
    string? CurrentVersionLabel,
    string? FileName,
    string? MimeType,
    long? FileSize,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    string? DeleteReason);

public sealed record DocumentDetailsDto(
    Guid Id,
    string Title,
    string? Description,
    Guid CategoryId,
    string CategoryName,
    Guid DocumentTypeId,
    Guid OwnerId,
    Guid? CurrentVersionId,
    Guid? EffectiveVersionId,
    int LatestVersionNumber,
    string Status,
    IReadOnlyList<TagDto> Tags,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    Guid UpdatedBy,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    string? DeleteReason)
{
    /// <summary>
    /// What the caller may do, so the UI can hide buttons that would fail anyway. The server still
    /// checks every request; this is a convenience, never a decision.
    /// </summary>
    public IReadOnlyList<string> AllowedActions { get; init; } = [];

    /// <summary>Metadata of the current version, and the schema version to read it with.</summary>
    public JsonElement? CurrentMetadata { get; init; }

    public Guid? CurrentSchemaVersionId { get; init; }
}

public sealed record DocumentVersionDto(
    Guid Id,
    int VersionNumber,
    int RevisionNumber,
    string Label,
    Guid StorageObjectId,
    string FileName,
    string MimeType,
    long FileSize,
    string Sha256,
    string ChangeKind,
    string? ChangeDescription,
    string ApprovalStatus,
    bool IsPublished,
    Guid CreatedBy,
    DateTimeOffset CreatedAt)
{
    public bool IsCurrent { get; init; }

    public bool IsEffective { get; init; }

    /// <summary>Decision D9: the uploader can always see why content is not available yet.</summary>
    public string ScanStatus { get; init; } = "Pending";

    /// <summary>The schema this row was written against; old rows keep reading with their own.</summary>
    public Guid SchemaVersionId { get; init; }

    public JsonElement? Metadata { get; init; }
}
