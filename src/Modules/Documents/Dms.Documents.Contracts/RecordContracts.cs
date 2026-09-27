using Dms.SharedKernel;

namespace Dms.Documents.Contracts;

public readonly record struct RecordId(Guid Value)
{
    public static RecordId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct RecordClassId(Guid Value)
{
    public static RecordClassId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct RecordSeriesId(Guid Value)
{
    public static RecordSeriesId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Lifecycle of a declared Record (phase 10.1–10.4). Distinct from <see cref="DocumentStatus"/>:
/// a Document may become a Record; once declared, normal document mutation APIs refuse changes.
/// </summary>
public enum RecordStatus
{
    /// <summary>Declared and immutable; retention has not started or is not yet configured.</summary>
    Active,

    /// <summary>Retention clock is running (phase 10.2).</summary>
    UnderRetention,

    /// <summary>Retention period ended; eligible for disposal review, not destroyed.</summary>
    Expired,

    /// <summary>Queued for disposal review / approval (phase 10.4).</summary>
    PendingDisposal,

    /// <summary>Destroyed through the disposition lifecycle; certificate may exist.</summary>
    Destroyed,
}

/// <summary>
/// Read-side check used by document mutation handlers so Records stay immutable even if a new
/// command forgets to call the dedicated gate.
/// </summary>
public interface IRecordImmutabilityGuard
{
    /// <summary>
    /// Success when the document is not a Record (or the Record is already Destroyed and only
    /// metadata reads remain). Failure with <c>record.immutable</c> when mutation is forbidden.
    /// </summary>
    Task<Result> EnsureMutableAsync(DocumentId documentId, CancellationToken cancellationToken);

    Task<bool> IsRecordAsync(DocumentId documentId, CancellationToken cancellationToken);
}
