using System.Security.Cryptography;
using System.Text;
using Dms.Documents.Contracts;
using Dms.SharedKernel;

namespace Dms.Documents.Domain;

/// <summary>
/// Controlled disposition of a Record. Destruction is only allowed after explicit approval and a
/// fresh Legal Hold check; never from PendingDisposal alone.
/// </summary>
public sealed class Disposition : AggregateRoot<DispositionId>
{
    private Disposition()
    {
    }

    private Disposition(
        DispositionId id,
        RecordId recordId,
        DocumentId documentId,
        string? reason,
        UserId requestedBy,
        DateTimeOffset now)
        : base(id)
    {
        RecordId = recordId;
        DocumentId = documentId;
        Status = DispositionStatus.PendingReview;
        RequestReason = reason;
        RequestedBy = requestedBy;
        RequestedAt = now;
        UpdatedAt = now;
    }

    public RecordId RecordId { get; private set; }

    public DocumentId DocumentId { get; private set; }

    public DispositionStatus Status { get; private set; }

    public string? RequestReason { get; private set; }

    public UserId RequestedBy { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public UserId? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public string? DecisionReason { get; private set; }

    public UserId? ApprovedBy { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public UserId? DestroyedBy { get; private set; }

    public DateTimeOffset? DestroyedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>EF concurrency token (PostgreSQL xmin).</summary>
    public uint RowVersion { get; private set; }

    public bool IsActive => Status is DispositionStatus.PendingReview or DispositionStatus.Approved;

    public static Result<Disposition> Request(
        ManagedRecord record,
        string? reason,
        UserId actor,
        DateTimeOffset now)
    {
        if (record.Status != RecordStatus.PendingDisposal)
        {
            return Result.Failure<Disposition>(Error.Conflict(
                "disposition.not_eligible",
                "Only Records in PendingDisposal can enter disposition review."));
        }

        if (record.Status == RecordStatus.Destroyed)
        {
            return Result.Failure<Disposition>(Error.Conflict(
                "disposition.already_destroyed",
                "A destroyed Record cannot enter disposition."));
        }

        return Result.Success(new Disposition(
            DispositionId.New(),
            record.Id,
            record.DocumentId,
            string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            actor,
            now));
    }

    public Result Approve(string? reason, UserId actor, DateTimeOffset now)
    {
        if (Status == DispositionStatus.Approved)
        {
            return Result.Success();
        }

        if (Status != DispositionStatus.PendingReview)
        {
            return Result.Failure(Error.Conflict(
                "disposition.invalid_state",
                $"Cannot approve a disposition in status {Status}."));
        }

        Status = DispositionStatus.Approved;
        ReviewedBy = actor;
        ReviewedAt = now;
        ApprovedBy = actor;
        ApprovedAt = now;
        DecisionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Reject(string reason, UserId actor, DateTimeOffset now)
    {
        if (Status == DispositionStatus.Rejected)
        {
            return Result.Success();
        }

        if (Status != DispositionStatus.PendingReview)
        {
            return Result.Failure(Error.Conflict(
                "disposition.invalid_state",
                $"Cannot reject a disposition in status {Status}."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation(
                "disposition.rejection_reason",
                "A rejection reason is required."));
        }

        Status = DispositionStatus.Rejected;
        ReviewedBy = actor;
        ReviewedAt = now;
        DecisionReason = reason.Trim();
        UpdatedAt = now;
        return Result.Success();
    }

    public Result MarkDestroyed(UserId actor, DateTimeOffset now)
    {
        if (Status == DispositionStatus.Destroyed)
        {
            return Result.Success();
        }

        if (Status != DispositionStatus.Approved)
        {
            return Result.Failure(Error.Conflict(
                "disposition.not_approved",
                "Destruction requires an approved disposition."));
        }

        Status = DispositionStatus.Destroyed;
        DestroyedBy = actor;
        DestroyedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }
}

/// <summary>
/// Immutable evidence that a Record was destroyed under an approved disposition. Survives content
/// removal and is never deleted through document purge.
/// </summary>
public sealed class DestructionCertificate : AggregateRoot<DestructionCertificateId>
{
    private DestructionCertificate()
    {
    }

    private DestructionCertificate(
        DestructionCertificateId id,
        string certificateNumber,
        DispositionId dispositionId,
        RecordId recordId,
        DocumentId documentId,
        DocumentVersionId finalVersionId,
        string finalVersionLabel,
        string recordTitle,
        byte[] contentSha256,
        RetentionPolicyId? retentionPolicyId,
        int? retentionPolicyVersion,
        DateTimeOffset? retentionExpiresAt,
        DateTimeOffset legalHoldCheckedAt,
        UserId approvedBy,
        DateTimeOffset approvedAt,
        UserId destroyedBy,
        DateTimeOffset destroyedAt,
        string? reason,
        byte[] certificateHash,
        DateTimeOffset createdAt)
        : base(id)
    {
        CertificateNumber = certificateNumber;
        DispositionId = dispositionId;
        RecordId = recordId;
        DocumentId = documentId;
        FinalVersionId = finalVersionId;
        FinalVersionLabel = finalVersionLabel;
        RecordTitle = recordTitle;
        ContentSha256 = contentSha256;
        RetentionPolicyId = retentionPolicyId;
        RetentionPolicyVersion = retentionPolicyVersion;
        RetentionExpiresAt = retentionExpiresAt;
        LegalHoldCheckedAt = legalHoldCheckedAt;
        ApprovedBy = approvedBy;
        ApprovedAt = approvedAt;
        DestroyedBy = destroyedBy;
        DestroyedAt = destroyedAt;
        Reason = reason;
        CertificateHash = certificateHash;
        CreatedAt = createdAt;
    }

    public string CertificateNumber { get; private set; } = string.Empty;

    public DispositionId DispositionId { get; private set; }

    public RecordId RecordId { get; private set; }

    public DocumentId DocumentId { get; private set; }

    public DocumentVersionId FinalVersionId { get; private set; }

    public string FinalVersionLabel { get; private set; } = string.Empty;

    public string RecordTitle { get; private set; } = string.Empty;

    public byte[] ContentSha256 { get; private set; } = [];

    public RetentionPolicyId? RetentionPolicyId { get; private set; }

    public int? RetentionPolicyVersion { get; private set; }

    public DateTimeOffset? RetentionExpiresAt { get; private set; }

    public DateTimeOffset LegalHoldCheckedAt { get; private set; }

    public UserId ApprovedBy { get; private set; }

    public DateTimeOffset ApprovedAt { get; private set; }

    public UserId DestroyedBy { get; private set; }

    public DateTimeOffset DestroyedAt { get; private set; }

    public string? Reason { get; private set; }

    public byte[] CertificateHash { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public static DestructionCertificate Create(
        Disposition disposition,
        ManagedRecord record,
        DocumentVersion finalVersion,
        DateTimeOffset legalHoldCheckedAt,
        string? destructionReason,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(disposition.ApprovedBy);
        ArgumentNullException.ThrowIfNull(disposition.ApprovedAt);

        var id = DestructionCertificateId.New();
        var number = $"COD-{now:yyyyMMdd}-{id.Value:N}"[..28].ToUpperInvariant();
        var reason = string.IsNullOrWhiteSpace(destructionReason)
            ? disposition.DecisionReason
            : destructionReason.Trim();

        var hash = ComputeHash(
            number,
            disposition.Id.Value,
            record.Id.Value,
            record.DocumentId.Value,
            finalVersion.Id.Value,
            finalVersion.Label,
            record.Title,
            finalVersion.Sha256,
            record.RetentionPolicyId?.Value,
            record.RetentionPolicyVersion,
            record.RetentionExpiresAt,
            legalHoldCheckedAt,
            disposition.ApprovedBy.Value.Value,
            disposition.ApprovedAt.Value,
            disposition.DestroyedBy?.Value ?? Guid.Empty,
            now,
            reason);

        return new DestructionCertificate(
            id,
            number,
            disposition.Id,
            record.Id,
            record.DocumentId,
            finalVersion.Id,
            finalVersion.Label,
            record.Title,
            finalVersion.Sha256,
            record.RetentionPolicyId,
            record.RetentionPolicyVersion,
            record.RetentionExpiresAt,
            legalHoldCheckedAt,
            disposition.ApprovedBy.Value,
            disposition.ApprovedAt.Value,
            disposition.DestroyedBy ?? disposition.ApprovedBy.Value,
            now,
            reason,
            hash,
            now);
    }

    private static byte[] ComputeHash(
        string number,
        Guid dispositionId,
        Guid recordId,
        Guid documentId,
        Guid versionId,
        string versionLabel,
        string title,
        byte[] contentSha,
        Guid? policyId,
        int? policyVersion,
        DateTimeOffset? expiresAt,
        DateTimeOffset holdCheckedAt,
        Guid approvedBy,
        DateTimeOffset approvedAt,
        Guid destroyedBy,
        DateTimeOffset destroyedAt,
        string? reason)
    {
        var payload = string.Join(
            '\n',
            number,
            dispositionId,
            recordId,
            documentId,
            versionId,
            versionLabel,
            title,
            Convert.ToHexStringLower(contentSha),
            policyId?.ToString() ?? "",
            policyVersion?.ToString() ?? "",
            expiresAt?.ToString("O") ?? "",
            holdCheckedAt.ToString("O"),
            approvedBy,
            approvedAt.ToString("O"),
            destroyedBy,
            destroyedAt.ToString("O"),
            reason ?? "");
        return SHA256.HashData(Encoding.UTF8.GetBytes(payload));
    }
}
