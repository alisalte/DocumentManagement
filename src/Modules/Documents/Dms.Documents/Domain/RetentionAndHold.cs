using Dms.Documents.Contracts;
using Dms.SharedKernel;

namespace Dms.Documents.Domain;

/// <summary>Versioned retention policy (phase 10.2). Applying a policy snapshots the current version onto the Record.</summary>
public sealed class RetentionPolicy : AggregateRoot<RetentionPolicyId>
{
    private RetentionPolicy()
    {
    }

    private RetentionPolicy(
        RetentionPolicyId id,
        string code,
        string name,
        string? description,
        int retentionPeriodDays,
        RetentionStartEvent startEvent,
        UserId createdBy,
        DateTimeOffset now)
        : base(id)
    {
        Code = code;
        Name = name;
        Description = description;
        RetentionPeriodDays = retentionPeriodDays;
        StartEvent = startEvent;
        VersionNumber = 1;
        IsActive = true;
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Days after the start event until the Record expires (becomes a disposal candidate).</summary>
    public int RetentionPeriodDays { get; private set; }

    public RetentionStartEvent StartEvent { get; private set; }

    /// <summary>Monotonic policy version; bumped when period or start event changes.</summary>
    public int VersionNumber { get; private set; }

    public bool IsActive { get; private set; }

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<RetentionPolicy> Create(
        string code,
        string name,
        string? description,
        int retentionPeriodDays,
        RetentionStartEvent startEvent,
        UserId createdBy,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 64)
        {
            return Result.Failure<RetentionPolicy>(Error.Validation("retention.code", "Code is required (max 64)."));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return Result.Failure<RetentionPolicy>(Error.Validation("retention.name", "Name is required (max 200)."));
        }

        if (retentionPeriodDays <= 0)
        {
            return Result.Failure<RetentionPolicy>(Error.Validation(
                "retention.period",
                "Retention period must be at least one day."));
        }

        return Result.Success(new RetentionPolicy(
            RetentionPolicyId.New(),
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            retentionPeriodDays,
            startEvent,
            createdBy,
            now));
    }

    public Result Update(
        string name,
        string? description,
        int retentionPeriodDays,
        RetentionStartEvent startEvent,
        bool isActive,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return Result.Failure(Error.Validation("retention.name", "Name is required (max 200)."));
        }

        if (retentionPeriodDays <= 0)
        {
            return Result.Failure(Error.Validation("retention.period", "Retention period must be at least one day."));
        }

        var versionBump = retentionPeriodDays != RetentionPeriodDays || startEvent != StartEvent;
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        RetentionPeriodDays = retentionPeriodDays;
        StartEvent = startEvent;
        IsActive = isActive;
        if (versionBump)
        {
            VersionNumber++;
        }

        UpdatedAt = now;
        return Result.Success();
    }
}

/// <summary>Active or released legal hold on a Document (and its Record, if any). Multiple holds may stack.</summary>
public sealed class LegalHold : AggregateRoot<LegalHoldId>
{
    private LegalHold()
    {
    }

    private LegalHold(
        LegalHoldId id,
        DocumentId documentId,
        string reason,
        UserId createdBy,
        DateTimeOffset now)
        : base(id)
    {
        DocumentId = documentId;
        Reason = reason;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public DocumentId DocumentId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReleasedAt { get; private set; }

    public UserId? ReleasedBy { get; private set; }

    public string? ReleaseReason { get; private set; }

    public bool IsActive => ReleasedAt is null;

    public static Result<LegalHold> Place(DocumentId documentId, string reason, UserId createdBy, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000)
        {
            return Result.Failure<LegalHold>(Error.Validation("legal_hold.reason", "A reason is required (max 2000)."));
        }

        return Result.Success(new LegalHold(LegalHoldId.New(), documentId, reason.Trim(), createdBy, now));
    }

    public Result Release(string? reason, UserId actor, DateTimeOffset now)
    {
        if (!IsActive)
        {
            return Result.Success();
        }

        ReleasedAt = now;
        ReleasedBy = actor;
        ReleaseReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        return Result.Success();
    }
}
