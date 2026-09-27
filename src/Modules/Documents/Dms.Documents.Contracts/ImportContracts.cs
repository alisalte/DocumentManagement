using Dms.SharedKernel;

namespace Dms.Documents.Contracts;

public readonly record struct ImportJobId(Guid Value)
{
    public static ImportJobId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct ImportItemId(Guid Value)
{
    public static ImportItemId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct ClassificationLevelId(Guid Value)
{
    public static ClassificationLevelId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Lifecycle of a legacy import batch (phase 10.6).</summary>
public enum ImportJobStatus
{
    Created,
    Validating,
    ValidationFailed,
    Ready,
    Running,
    Paused,
    Completed,
    CompletedWithErrors,
    Failed,
}

/// <summary>Per-manifest-entry state.</summary>
public enum ImportItemStatus
{
    Pending,
    Valid,
    Invalid,
    Ready,
    Running,
    Succeeded,
    Failed,
    Skipped,
    Retryable,
}

public enum ImportMappingKind
{
    User,
    Group,
    Folder,
    DocumentType,
    Classification,
    RecordClass,
    RecordSeries,
    RetentionPolicy,
    Permission,
    MetadataField,
}

public enum ImportFailurePolicy
{
    ContinueOnError,
    StopOnError,
}

/// <summary>
/// Optional classification catalog used by import mapping. Phase 10.5 full clearance model may
/// replace this seam; Import never invents a second authorization path.
/// </summary>
public interface IClassificationCatalog
{
    Task<ClassificationLevelInfo?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    Task<Result> AssignAsync(DocumentId documentId, string code, UserId actor, CancellationToken cancellationToken);
}

public sealed record ClassificationLevelInfo(Guid Id, string Code, string Name, bool IsActive);
