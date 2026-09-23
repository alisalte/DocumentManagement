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
