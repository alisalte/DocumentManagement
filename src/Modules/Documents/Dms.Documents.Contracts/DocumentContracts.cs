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
