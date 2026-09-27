using Dms.SharedKernel;

namespace Dms.Documents.Contracts;

public readonly record struct RetentionPolicyId(Guid Value)
{
    public static RetentionPolicyId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct LegalHoldId(Guid Value)
{
    public static LegalHoldId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>When the retention clock starts for a Record (phase 10.2).</summary>
public enum RetentionStartEvent
{
    /// <summary>Clock starts when the Record is declared.</summary>
    Declaration,

    /// <summary>Clock starts from the underlying document's created_at.</summary>
    DocumentCreated,
}

/// <summary>
/// Legal Hold and Record immutability checks that every deletion / purge path must call.
/// </summary>
public interface ILegalHoldGuard
{
    Task<Result> EnsureNotOnHoldAsync(DocumentId documentId, CancellationToken cancellationToken);

    Task<bool> IsOnHoldAsync(DocumentId documentId, CancellationToken cancellationToken);
}
