using Dms.SharedKernel;

namespace Dms.DocumentTypes.Contracts;

public readonly record struct DocumentTypeId(Guid Value)
{
    public static DocumentTypeId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct DocumentTypeVersionId(Guid Value)
{
    public static DocumentTypeVersionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// How a metadata-only edit behaves for this type (ADR 0001). Phase 3 adds the per-field
/// <c>is_approval_relevant</c> refinement on top.
/// </summary>
public enum MetadataEditPolicy
{
    /// <summary>Governed types: any metadata change produces a new revision of the same file.</summary>
    NewRevision,

    /// <summary>Ungoverned types: the latest revision is updated in place, and audited.</summary>
    InPlace,
}

public sealed record DocumentTypeSettings
{
    /// <summary>Empty means any extension is accepted.</summary>
    public IReadOnlyList<string> AllowedExtensions { get; init; } = [];

    /// <summary>Null falls back to the global storage limit.</summary>
    public long? MaxUploadBytes { get; init; }

    public MetadataEditPolicy MetadataEditPolicy { get; init; } = MetadataEditPolicy.NewRevision;

    public bool AllowExternalSharing { get; init; } = true;
}

public sealed record DocumentTypeSummary(
    DocumentTypeId Id,
    string Code,
    string Name,
    bool IsActive,
    DocumentTypeVersionId? LatestPublishedVersionId,
    DocumentTypeSettings Settings);

/// <summary>
/// What other modules need from the document type registry. Phase 3 adds field definitions and
/// real metadata validation behind the same interface.
/// </summary>
public interface IDocumentTypeCatalog
{
    Task<DocumentTypeSummary?> FindAsync(DocumentTypeId id, CancellationToken cancellationToken);

    /// <summary>The schema version a new document or version binds to.</summary>
    Task<Result<DocumentTypeVersionId>> ResolveVersionForNewDocumentAsync(
        DocumentTypeId id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validates dynamic metadata against the schema. Phase 2 has no fields yet, so this only
    /// rejects values that no schema could accept.
    /// </summary>
    Task<Result> ValidateMetadataAsync(
        DocumentTypeVersionId versionId,
        string metadataJson,
        CancellationToken cancellationToken);
}
