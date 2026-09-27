using Dms.Documents.Contracts;
using Dms.SharedKernel;

namespace Dms.Documents.Domain;

/// <summary>Classification of records for filing plans (e.g. Contracts, HR, Finance).</summary>
public sealed class RecordClass : AggregateRoot<RecordClassId>
{
    private RecordClass()
    {
    }

    private RecordClass(
        RecordClassId id,
        string code,
        string name,
        string? description,
        UserId createdBy,
        DateTimeOffset now)
        : base(id)
    {
        Code = code;
        Name = name;
        Description = description;
        IsActive = true;
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<RecordClass> Create(
        string code,
        string name,
        string? description,
        UserId createdBy,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 64)
        {
            return Result.Failure<RecordClass>(Error.Validation("record_class.code", "Code is required (max 64)."));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return Result.Failure<RecordClass>(Error.Validation("record_class.name", "Name is required (max 200)."));
        }

        return Result.Success(new RecordClass(
            RecordClassId.New(),
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            createdBy,
            now));
    }

    public Result Update(string name, string? description, bool isActive, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return Result.Failure(Error.Validation("record_class.name", "Name is required (max 200)."));
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsActive = isActive;
        UpdatedAt = now;
        return Result.Success();
    }
}

/// <summary>A series within a record class (file plan node).</summary>
public sealed class RecordSeries : AggregateRoot<RecordSeriesId>
{
    private RecordSeries()
    {
    }

    private RecordSeries(
        RecordSeriesId id,
        RecordClassId recordClassId,
        string code,
        string name,
        string? description,
        UserId createdBy,
        DateTimeOffset now)
        : base(id)
    {
        RecordClassId = recordClassId;
        Code = code;
        Name = name;
        Description = description;
        IsActive = true;
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public RecordClassId RecordClassId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<RecordSeries> Create(
        RecordClassId recordClassId,
        string code,
        string name,
        string? description,
        UserId createdBy,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 64)
        {
            return Result.Failure<RecordSeries>(Error.Validation("record_series.code", "Code is required (max 64)."));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return Result.Failure<RecordSeries>(Error.Validation("record_series.name", "Name is required (max 200)."));
        }

        return Result.Success(new RecordSeries(
            RecordSeriesId.New(),
            recordClassId,
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            createdBy,
            now));
    }

    public Result Update(string name, string? description, bool isActive, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return Result.Failure(Error.Validation("record_series.name", "Name is required (max 200)."));
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsActive = isActive;
        UpdatedAt = now;
        return Result.Success();
    }
}

/// <summary>
/// A declared Record: distinct from <see cref="Document"/>. Pins a final version, freezes
/// metadata, and follows a retention/disposal lifecycle. Normal document APIs must not mutate
/// the underlying document while this entity exists in a non-destroyed state.
/// </summary>
public sealed class ManagedRecord : AggregateRoot<RecordId>
{
    private ManagedRecord()
    {
    }

    private ManagedRecord(
        RecordId id,
        DocumentId documentId,
        DocumentVersionId finalVersionId,
        RecordClassId recordClassId,
        RecordSeriesId? recordSeriesId,
        string title,
        UserId declaredBy,
        DateTimeOffset now)
        : base(id)
    {
        DocumentId = documentId;
        FinalVersionId = finalVersionId;
        RecordClassId = recordClassId;
        RecordSeriesId = recordSeriesId;
        Title = title;
        Status = RecordStatus.Active;
        DeclaredBy = declaredBy;
        DeclaredAt = now;
        MetadataFrozenAt = now;
        UpdatedAt = now;
        UpdatedBy = declaredBy;
    }

    public DocumentId DocumentId { get; private set; }

    /// <summary>The version that constitutes the authoritative Record content.</summary>
    public DocumentVersionId FinalVersionId { get; private set; }

    public RecordClassId RecordClassId { get; private set; }

    public RecordSeriesId? RecordSeriesId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public RecordStatus Status { get; private set; }

    public UserId DeclaredBy { get; private set; }

    public DateTimeOffset DeclaredAt { get; private set; }

    /// <summary>When metadata became immutable (declaration time).</summary>
    public DateTimeOffset MetadataFrozenAt { get; private set; }

    public UserId UpdatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public string? LastTransitionReason { get; private set; }

    public bool IsImmutable => Status != RecordStatus.Destroyed;

    public static Result<ManagedRecord> Declare(
        Document document,
        DocumentVersionId finalVersionId,
        RecordClassId recordClassId,
        RecordSeriesId? recordSeriesId,
        UserId declaredBy,
        DateTimeOffset now)
    {
        if (document.IsDeleted)
        {
            return Result.Failure<ManagedRecord>(Error.Conflict(
                "record.document_deleted",
                "A deleted document cannot be declared as a Record."));
        }

        var version = document.Versions.FirstOrDefault(candidate => candidate.Id == finalVersionId);
        if (version is null)
        {
            return Result.Failure<ManagedRecord>(Error.Validation(
                "record.final_version",
                "The final Record version must belong to the document."));
        }

        return Result.Success(new ManagedRecord(
            RecordId.New(),
            document.Id,
            finalVersionId,
            recordClassId,
            recordSeriesId,
            document.Title,
            declaredBy,
            now));
    }

    public Result TransitionTo(RecordStatus next, string? reason, UserId actor, DateTimeOffset now)
    {
        if (Status == next)
        {
            return Result.Success();
        }

        if (!IsAllowedTransition(Status, next))
        {
            return Result.Failure(Error.Conflict(
                "record.invalid_transition",
                $"Cannot move a Record from {Status} to {next}."));
        }

        Status = next;
        LastTransitionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedBy = actor;
        UpdatedAt = now;
        return Result.Success();
    }

    private static bool IsAllowedTransition(RecordStatus from, RecordStatus to) => (from, to) switch
    {
        (RecordStatus.Active, RecordStatus.UnderRetention) => true,
        (RecordStatus.Active, RecordStatus.Expired) => true,
        (RecordStatus.UnderRetention, RecordStatus.Expired) => true,
        (RecordStatus.Expired, RecordStatus.PendingDisposal) => true,
        (RecordStatus.PendingDisposal, RecordStatus.Expired) => true, // rejection of disposal review
        (RecordStatus.PendingDisposal, RecordStatus.Destroyed) => true,
        _ => false,
    };
}
