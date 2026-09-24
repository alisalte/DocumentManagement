using Dms.SharedKernel;

namespace Dms.Documents.Contracts;

public readonly record struct DocumentVersionId(Guid Value)
{
    public static DocumentVersionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct TagId(Guid Value)
{
    public static TagId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Approval state of one version-revision. Phase 2 creates everything as <see cref="NotRequired"/>
/// because no workflow is attached yet; phase 4 drives the rest of the values.
/// </summary>
public enum ApprovalStatus
{
    NotRequired,
    Draft,
    InWorkflow,
    Approved,
    Rejected,
    ChangesRequested,
    Cancelled,
}

/// <summary>What changed relative to the previous row (ADR 0001).</summary>
public enum VersionChangeKind
{
    Initial,
    Content,
    Metadata,
    ContentAndMetadata,
}

/// <summary>What the workflow engine needs to know about one version-revision.</summary>
public sealed record VersionForWorkflow(
    Guid DocumentId,
    string DocumentTitle,
    Guid VersionId,
    int VersionNumber,
    int RevisionNumber,
    Guid DocumentTypeId,
    Guid SchemaVersionId,
    string MetadataJson,
    UserId CreatedBy,
    UserId OwnerId,
    ApprovalStatus ApprovalStatus,
    bool IsDocumentDeleted)
{
    /// <summary>Orders rows of one document: V3.2 after V3.1, V4.1 after both.</summary>
    public long SortKey => (VersionNumber * 1_000_000L) + RevisionNumber;

    public string Label => $"V{VersionNumber}.{RevisionNumber}";
}

/// <summary>
/// The Documents side of approval (section 6.7). Workflow reads versions through it and records
/// outcomes on them; approval lives on the version row, never on the document.
/// </summary>
public interface IDocumentApprovalGateway
{
    Task<VersionForWorkflow?> FindVersionAsync(Guid versionId, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the version's approval status. APPROVED also moves effective_version_id to it, but only
    /// forward: an older version approved late never replaces a newer effective one.
    /// </summary>
    Task RecordAsync(Guid documentId, Guid versionId, ApprovalStatus status, CancellationToken cancellationToken);
}

/// <summary>
/// Called by Documents after a version or revision is created, in the same transaction. The
/// Workflow module implements it (AUTO_ON_VERSION starts the instance there); without Workflow
/// a no-op keeps Documents working on its own.
/// </summary>
public interface IVersionCreatedHook
{
    /// <summary>
    /// Checked before anything is written: a type set to start workflows automatically must have a
    /// usable workflow, or the version is refused rather than created without its review.
    /// </summary>
    Task<Result> EnsureReadyAsync(Guid documentTypeId, CancellationToken cancellationToken);

    Task OnVersionCreatedAsync(Guid documentTypeId, Guid documentId, Guid versionId, CancellationToken cancellationToken);
}

public sealed class NoVersionCreatedHook : IVersionCreatedHook
{
    public Task<Result> EnsureReadyAsync(Guid documentTypeId, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success());

    public Task OnVersionCreatedAsync(Guid documentTypeId, Guid documentId, Guid versionId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>Minimal view other modules need of a document.</summary>
public sealed record DocumentRef(
    DocumentId Id,
    CategoryId CategoryId,
    UserId OwnerId,
    bool IsDeleted);

public interface IDocumentLocator
{
    Task<DocumentRef?> FindAsync(DocumentId id, CancellationToken cancellationToken);
}

/// <summary>
/// Told after anything about a document changed (a version, metadata, title, tags, deletion,
/// an approval), in the same transaction. Search implements it to reindex; Documents knows
/// nothing about search.
/// </summary>
public interface IDocumentChangeListener
{
    Task OnDocumentChangedAsync(Guid documentId, CancellationToken cancellationToken);
}

public sealed record VersionIndexData(
    Guid VersionId,
    int VersionNumber,
    int RevisionNumber,
    Guid StorageObjectId,
    string FileName,
    string MimeType,
    Guid SchemaVersionId,
    string MetadataJson,
    ApprovalStatus ApprovalStatus,
    Guid CreatedBy,
    DateTimeOffset CreatedAt)
{
    public string Label => $"V{VersionNumber}.{RevisionNumber}";

    public bool IsPublished => ApprovalStatus is ApprovalStatus.NotRequired or ApprovalStatus.Approved;
}

/// <summary>Everything the search index holds about one document, read from the source of truth.</summary>
public sealed record DocumentIndexData(
    Guid DocumentId,
    string Title,
    string? Description,
    Guid CategoryId,
    IReadOnlyList<Guid> CategoryPath,
    Guid DocumentTypeId,
    Guid OwnerId,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsDeleted,
    Guid? CurrentVersionId,
    Guid? EffectiveVersionId,
    IReadOnlyList<string> Tags,
    IReadOnlyList<VersionIndexData> Versions);

/// <summary>Read access for building and rebuilding the search index (section 8.4).</summary>
public interface IDocumentIndexSource
{
    /// <summary>Null once the document has been purged.</summary>
    Task<DocumentIndexData?> GetAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>Documents with a version on this stored file (content versions and their revisions).</summary>
    Task<IReadOnlyList<Guid>> DocumentsUsingObjectAsync(Guid storageObjectId, CancellationToken cancellationToken);

    /// <summary>All document ids after the given one, in id order, for batched rebuilds.</summary>
    Task<IReadOnlyList<Guid>> ListIdsAsync(Guid? after, int batchSize, CancellationToken cancellationToken);
}

public sealed record TitleHit(Guid DocumentId, string Title, string? VersionLabel, string? FileName, DateTimeOffset UpdatedAt);

/// <summary>
/// Degraded mode (section 8.4): when the search engine is down, titles are still searchable in
/// Postgres, through the same access scope as the document browser.
/// </summary>
public interface IDocumentTitleSearch
{
    Task<(IReadOnlyList<TitleHit> Hits, int Total)> SearchAsync(string? text, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>One version as other modules see it: enough to label it and to serve its file.</summary>
public sealed record VersionSummary(
    Guid DocumentId,
    string DocumentTitle,
    Guid DocumentTypeId,
    bool IsDocumentDeleted,
    Guid VersionId,
    string Label,
    Guid StorageObjectId,
    string FileName,
    string MimeType,
    long FileSize,
    bool IsPublished);

/// <summary>
/// Read access to single versions for Sharing, which pins everything it hands out to one version
/// (decision D8). Deleted documents are included; callers decide what that means for them.
/// </summary>
public interface IDocumentVersionReader
{
    /// <summary>Null when the version does not exist or belongs to another document.</summary>
    Task<VersionSummary?> FindAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, VersionSummary>> FindManyAsync(
        IReadOnlyCollection<Guid> versionIds,
        CancellationToken cancellationToken);

    Task<Guid?> FindDocumentTypeIdAsync(Guid documentId, CancellationToken cancellationToken);
}
