using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;

namespace Dms.Documents.Application;

public interface IImportJobRepository
{
    Task<ImportJob?> FindAsync(ImportJobId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ImportJob>> ListAsync(int take, CancellationToken cancellationToken);

    void Add(ImportJob job);
}

public interface IImportItemRepository
{
    Task<ImportItem?> FindAsync(ImportItemId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ImportItem>> ListByJobAsync(
        ImportJobId jobId,
        ImportItemStatus? status,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ImportItem>> ClaimBatchAsync(ImportJobId jobId, int batchSize, CancellationToken cancellationToken);

    Task<int> CountByStatusAsync(ImportJobId jobId, ImportItemStatus status, CancellationToken cancellationToken);

    void AddRange(IEnumerable<ImportItem> items);
}

public interface IImportMappingRepository
{
    Task<IReadOnlyList<ImportMapping>> ListForJobAsync(
        string sourceSystem,
        ImportJobId? jobId,
        CancellationToken cancellationToken);

    Task<string?> ResolveAsync(
        string sourceSystem,
        ImportJobId? jobId,
        ImportMappingKind kind,
        string sourceKey,
        CancellationToken cancellationToken);

    void Add(ImportMapping mapping);
}

public interface IImportSourceIndexRepository
{
    Task<ImportSourceIndex?> FindAsync(string sourceSystem, string sourceId, CancellationToken cancellationToken);

    void Add(ImportSourceIndex index);
}

public interface IClassificationLevelRepository
{
    Task<ClassificationLevel?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClassificationLevel>> ListAsync(CancellationToken cancellationToken);

    void Add(ClassificationLevel level);

    void Assign(DocumentClassification assignment);

    Task<DocumentClassification?> FindAssignmentAsync(DocumentId documentId, CancellationToken cancellationToken);
}

public sealed record ImportJobDto(
    Guid Id,
    string SourceSystem,
    string Name,
    string Status,
    string FailurePolicy,
    bool CreateMissingCategories,
    bool DryRunOnly,
    int TotalItems,
    int ProcessedItems,
    int SucceededItems,
    int FailedItems,
    int SkippedItems,
    int InvalidItems,
    long BytesProcessed,
    long BytesTotal,
    string? LastError,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record ImportItemDto(
    Guid Id,
    Guid ImportJobId,
    string SourceId,
    string SourcePath,
    string? Title,
    string Status,
    string? ErrorCode,
    string? ErrorMessage,
    string? ExpectedSha256,
    string? ActualSha256,
    long? ExpectedSize,
    long? ActualSize,
    Guid? TargetDocumentId,
    Guid? TargetRecordId,
    Guid? TargetVersionId,
    int RetryCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record ImportValidationIssueDto(
    string Severity,
    string Code,
    string Message,
    string? SourceId,
    string? Path);

public sealed record ImportValidationReportDto(
    Guid JobId,
    string Status,
    int Total,
    int Valid,
    int Invalid,
    int Warnings,
    IReadOnlyList<ImportValidationIssueDto> Issues);

public sealed record ImportReportDto(
    Guid ImportId,
    string SourceSystem,
    string Name,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int Total,
    int Succeeded,
    int Failed,
    int Skipped,
    int Invalid,
    long BytesProcessed,
    long BytesTotal,
    IReadOnlyList<ImportItemDto> Errors);
