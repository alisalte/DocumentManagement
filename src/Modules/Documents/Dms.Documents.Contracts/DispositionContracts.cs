using Dms.SharedKernel;

namespace Dms.Documents.Contracts;

public readonly record struct DispositionId(Guid Value)
{
    public static DispositionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct DestructionCertificateId(Guid Value)
{
    public static DestructionCertificateId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Human review workflow for destroying an eligible Record (phase 10.4).</summary>
public enum DispositionStatus
{
    PendingReview,
    Approved,
    Rejected,
    Destroyed,
}
