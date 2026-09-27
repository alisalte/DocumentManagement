using Dms.Documents.Contracts;
using Dms.SharedKernel;

namespace Dms.Documents.Domain;

/// <summary>Batch import from a legacy DMS manifest (phase 10.6 / D11).</summary>
public sealed class ImportJob : AggregateRoot<ImportJobId>
{
    private ImportJob()
    {
    }

    private ImportJob(
        ImportJobId id,
        string sourceSystem,
        string name,
        string? filesRoot,
        string manifestSha256,
        ImportFailurePolicy failurePolicy,
        bool createMissingCategories,
        bool dryRunOnly,
        UserId createdBy,
        DateTimeOffset now)
        : base(id)
    {
        SourceSystem = sourceSystem;
        Name = name;
        FilesRoot = filesRoot;
        ManifestSha256 = manifestSha256;
        FailurePolicy = failurePolicy;
        CreateMissingCategories = createMissingCategories;
        DryRunOnly = dryRunOnly;
        Status = ImportJobStatus.Created;
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string SourceSystem { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>Trusted server-side root for relative manifest file paths. Never client-chosen absolute FS targets.</summary>
    public string? FilesRoot { get; private set; }

    public string ManifestSha256 { get; private set; } = string.Empty;

    public ImportJobStatus Status { get; private set; }

    public ImportFailurePolicy FailurePolicy { get; private set; }

    public bool CreateMissingCategories { get; private set; }

    public bool DryRunOnly { get; private set; }

    public int TotalItems { get; private set; }

    public int ProcessedItems { get; private set; }

    public int SucceededItems { get; private set; }

    public int FailedItems { get; private set; }

    public int SkippedItems { get; private set; }

    public int InvalidItems { get; private set; }

    public long BytesProcessed { get; private set; }

    public long BytesTotal { get; private set; }

    public string? LastError { get; private set; }

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public static Result<ImportJob> Create(
        string sourceSystem,
        string name,
        string? filesRoot,
        string manifestSha256,
        ImportFailurePolicy failurePolicy,
        bool createMissingCategories,
        bool dryRunOnly,
        UserId actor,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sourceSystem))
        {
            return Result.Failure<ImportJob>(Error.Validation("import.source", "Source system is required."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<ImportJob>(Error.Validation("import.name", "Import name is required."));
        }

        if (string.IsNullOrWhiteSpace(manifestSha256) || manifestSha256.Length != 64)
        {
            return Result.Failure<ImportJob>(Error.Validation("import.manifest_hash", "Manifest SHA-256 is required."));
        }

        return Result.Success(new ImportJob(
            ImportJobId.New(),
            sourceSystem.Trim(),
            name.Trim(),
            string.IsNullOrWhiteSpace(filesRoot) ? null : filesRoot.Trim(),
            manifestSha256.ToLowerInvariant(),
            failurePolicy,
            createMissingCategories,
            dryRunOnly,
            actor,
            now));
    }

    public Result BeginValidation(DateTimeOffset now)
    {
        if (Status is not (ImportJobStatus.Created or ImportJobStatus.ValidationFailed or ImportJobStatus.Ready))
        {
            return Result.Failure(Error.Conflict("import.invalid_state", $"Cannot validate from status {Status}."));
        }

        Status = ImportJobStatus.Validating;
        UpdatedAt = now;
        LastError = null;
        return Result.Success();
    }

    public void CompleteValidation(bool ok, int total, int invalid, long bytesTotal, string? error, DateTimeOffset now)
    {
        TotalItems = total;
        InvalidItems = invalid;
        BytesTotal = bytesTotal;
        Status = ok ? ImportJobStatus.Ready : ImportJobStatus.ValidationFailed;
        LastError = error;
        UpdatedAt = now;
    }

    public Result Start(DateTimeOffset now)
    {
        if (DryRunOnly)
        {
            return Result.Failure(Error.Conflict("import.dry_run_only", "This job was created as dry-run only."));
        }

        if (Status is not (
            ImportJobStatus.Ready
            or ImportJobStatus.Paused
            or ImportJobStatus.Failed
            or ImportJobStatus.CompletedWithErrors))
        {
            return Result.Failure(Error.Conflict("import.invalid_state", $"Cannot start from status {Status}."));
        }

        Status = ImportJobStatus.Running;
        StartedAt ??= now;
        CompletedAt = null;
        UpdatedAt = now;
        LastError = null;
        return Result.Success();
    }

    /// <summary>Adjust counters when a previously failed/retryable item is re-queued.</summary>
    public void UncountFailure(long bytes, DateTimeOffset now)
    {
        FailedItems = Math.Max(0, FailedItems - 1);
        ProcessedItems = Math.Max(0, ProcessedItems - 1);
        BytesProcessed = Math.Max(0, BytesProcessed - Math.Max(0, bytes));
        UpdatedAt = now;
    }

    public Result Pause(DateTimeOffset now)
    {
        if (Status != ImportJobStatus.Running)
        {
            return Result.Failure(Error.Conflict("import.invalid_state", $"Cannot pause from status {Status}."));
        }

        Status = ImportJobStatus.Paused;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Resume(DateTimeOffset now) => Start(now);

    public void RecordItemOutcome(ImportItemStatus status, long bytes, DateTimeOffset now)
    {
        ProcessedItems++;
        BytesProcessed += Math.Max(0, bytes);
        switch (status)
        {
            case ImportItemStatus.Succeeded:
                SucceededItems++;
                break;
            case ImportItemStatus.Failed:
            case ImportItemStatus.Retryable:
                FailedItems++;
                break;
            case ImportItemStatus.Skipped:
                SkippedItems++;
                break;
        }

        UpdatedAt = now;
    }

    public void Finish(bool stopRequested, DateTimeOffset now)
    {
        if (Status == ImportJobStatus.Paused)
        {
            UpdatedAt = now;
            return;
        }

        if (stopRequested)
        {
            Status = ImportJobStatus.Failed;
            LastError ??= "Stopped on error per failure policy.";
        }
        else if (FailedItems > 0 || InvalidItems > 0)
        {
            Status = ImportJobStatus.CompletedWithErrors;
        }
        else
        {
            Status = ImportJobStatus.Completed;
        }

        CompletedAt = now;
        UpdatedAt = now;
    }

    public void MarkFailed(string error, DateTimeOffset now)
    {
        Status = ImportJobStatus.Failed;
        LastError = error;
        CompletedAt = now;
        UpdatedAt = now;
    }
}

public sealed class ImportItem : AggregateRoot<ImportItemId>
{
    private ImportItem()
    {
    }

    private ImportItem(
        ImportItemId id,
        ImportJobId jobId,
        string sourceId,
        string sourcePath,
        string? title,
        string entryJson,
        string? expectedSha256,
        long? expectedSize,
        DateTimeOffset now)
        : base(id)
    {
        ImportJobId = jobId;
        SourceId = sourceId;
        SourcePath = sourcePath;
        Title = title;
        EntryJson = entryJson;
        ExpectedSha256 = expectedSha256;
        ExpectedSize = expectedSize;
        Status = ImportItemStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public ImportJobId ImportJobId { get; private set; }

    public string SourceId { get; private set; } = string.Empty;

    public string SourcePath { get; private set; } = string.Empty;

    public string? Title { get; private set; }

    public string EntryJson { get; private set; } = string.Empty;

    public ImportItemStatus Status { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? ExpectedSha256 { get; private set; }

    public string? ActualSha256 { get; private set; }

    public long? ExpectedSize { get; private set; }

    public long? ActualSize { get; private set; }

    public DocumentId? TargetDocumentId { get; private set; }

    public RecordId? TargetRecordId { get; private set; }

    public DocumentVersionId? TargetVersionId { get; private set; }

    public int RetryCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public static ImportItem Create(
        ImportJobId jobId,
        string sourceId,
        string sourcePath,
        string? title,
        string entryJson,
        string? expectedSha256,
        long? expectedSize,
        DateTimeOffset now) =>
        new(
            ImportItemId.New(),
            jobId,
            sourceId,
            sourcePath,
            title,
            entryJson,
            expectedSha256,
            expectedSize,
            now);

    public void MarkValid(DateTimeOffset now)
    {
        Status = ImportItemStatus.Valid;
        ErrorCode = null;
        ErrorMessage = null;
        UpdatedAt = now;
    }

    public void MarkInvalid(string code, string message, DateTimeOffset now)
    {
        Status = ImportItemStatus.Invalid;
        ErrorCode = code;
        ErrorMessage = message;
        UpdatedAt = now;
        CompletedAt = now;
    }

    public void MarkReady(DateTimeOffset now)
    {
        if (Status is ImportItemStatus.Valid
            or ImportItemStatus.Retryable
            or ImportItemStatus.Failed
            or ImportItemStatus.Running)
        {
            Status = ImportItemStatus.Ready;
            ErrorCode = null;
            ErrorMessage = null;
            CompletedAt = null;
            UpdatedAt = now;
        }
    }

    public Result TryClaim(DateTimeOffset now)
    {
        if (Status is not (ImportItemStatus.Ready or ImportItemStatus.Retryable))
        {
            return Result.Failure(Error.Conflict("import.item_busy", "Item is not claimable."));
        }

        Status = ImportItemStatus.Running;
        StartedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    public void MarkSucceeded(
        DocumentId documentId,
        DocumentVersionId versionId,
        RecordId? recordId,
        string actualSha256,
        long actualSize,
        DateTimeOffset now)
    {
        Status = ImportItemStatus.Succeeded;
        TargetDocumentId = documentId;
        TargetVersionId = versionId;
        TargetRecordId = recordId;
        ActualSha256 = actualSha256;
        ActualSize = actualSize;
        ErrorCode = null;
        ErrorMessage = null;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void MarkSkipped(string code, string message, DocumentId? existingDocumentId, DateTimeOffset now)
    {
        Status = ImportItemStatus.Skipped;
        ErrorCode = code;
        ErrorMessage = message;
        TargetDocumentId = existingDocumentId;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void MarkFailed(string code, string message, bool retryable, string? actualSha256, DateTimeOffset now)
    {
        RetryCount++;
        Status = retryable && RetryCount < 5 ? ImportItemStatus.Retryable : ImportItemStatus.Failed;
        ErrorCode = code;
        ErrorMessage = message;
        ActualSha256 = actualSha256 ?? ActualSha256;
        CompletedAt = now;
        UpdatedAt = now;
    }
}

public sealed class ImportMapping : AggregateRoot<Guid>
{
    private ImportMapping()
    {
    }

    private ImportMapping(
        Guid id,
        ImportJobId? jobId,
        string sourceSystem,
        ImportMappingKind kind,
        string sourceKey,
        string targetKey,
        UserId createdBy,
        DateTimeOffset now)
        : base(id)
    {
        ImportJobId = jobId;
        SourceSystem = sourceSystem;
        Kind = kind;
        SourceKey = sourceKey;
        TargetKey = targetKey;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public ImportJobId? ImportJobId { get; private set; }

    public string SourceSystem { get; private set; } = string.Empty;

    public ImportMappingKind Kind { get; private set; }

    public string SourceKey { get; private set; } = string.Empty;

    public string TargetKey { get; private set; } = string.Empty;

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ImportMapping Create(
        ImportJobId? jobId,
        string sourceSystem,
        ImportMappingKind kind,
        string sourceKey,
        string targetKey,
        UserId actor,
        DateTimeOffset now) =>
        new(Guid.CreateVersion7(), jobId, sourceSystem.Trim(), kind, sourceKey.Trim(), targetKey.Trim(), actor, now);
}

/// <summary>Minimal classification level catalog for import mapping (10.5 seam).</summary>
public sealed class ClassificationLevel : AggregateRoot<ClassificationLevelId>
{
    private ClassificationLevel()
    {
    }

    private ClassificationLevel(
        ClassificationLevelId id,
        string code,
        string name,
        int rank,
        UserId actor,
        DateTimeOffset now)
        : base(id)
    {
        Code = code;
        Name = name;
        Rank = rank;
        IsActive = true;
        CreatedBy = actor;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public int Rank { get; private set; }

    public bool IsActive { get; private set; }

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<ClassificationLevel> Create(string code, string name, int rank, UserId actor, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<ClassificationLevel>(Error.Validation(
                "classification.required",
                "Classification code and name are required."));
        }

        return Result.Success(new ClassificationLevel(
            ClassificationLevelId.New(),
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            rank,
            actor,
            now));
    }
}

public sealed class DocumentClassification
{
    private DocumentClassification()
    {
    }

    public DocumentClassification(DocumentId documentId, ClassificationLevelId levelId, UserId assignedBy, DateTimeOffset now)
    {
        DocumentId = documentId;
        LevelId = levelId;
        AssignedBy = assignedBy;
        AssignedAt = now;
    }

    public DocumentId DocumentId { get; private set; }

    public ClassificationLevelId LevelId { get; private set; }

    public UserId AssignedBy { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }
}

/// <summary>Global idempotency key for a successfully imported legacy source object.</summary>
public sealed class ImportSourceIndex
{
    private ImportSourceIndex()
    {
    }

    public ImportSourceIndex(
        string sourceSystem,
        string sourceId,
        DocumentId documentId,
        ImportJobId importJobId,
        ImportItemId importItemId,
        DateTimeOffset importedAt)
    {
        SourceSystem = sourceSystem;
        SourceId = sourceId;
        DocumentId = documentId;
        ImportJobId = importJobId;
        ImportItemId = importItemId;
        ImportedAt = importedAt;
    }

    public string SourceSystem { get; private set; } = string.Empty;

    public string SourceId { get; private set; } = string.Empty;

    public DocumentId DocumentId { get; private set; }

    public ImportJobId ImportJobId { get; private set; }

    public ImportItemId ImportItemId { get; private set; }

    public DateTimeOffset ImportedAt { get; private set; }
}
