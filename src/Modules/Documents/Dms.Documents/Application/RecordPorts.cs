using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;

namespace Dms.Documents.Application;

/// <summary>Phase 10.1: refuse document mutation when a live Record exists for the document.</summary>
public static class RecordGate
{
    public static readonly Error Immutable = Error.Conflict(
        "record.immutable",
        "This document is a declared Record and cannot be modified through document APIs.");

    public static Result EnsureMutable(bool isImmutableRecord) =>
        isImmutableRecord ? Result.Failure(Immutable) : Result.Success();
}

public sealed record RecordSummaryDto(
    Guid RecordId,
    Guid DocumentId,
    Guid FinalVersionId,
    string FinalVersionLabel,
    Guid RecordClassId,
    string RecordClassCode,
    string RecordClassName,
    Guid? RecordSeriesId,
    string? RecordSeriesCode,
    string Status,
    DateTimeOffset DeclaredAt,
    Guid DeclaredBy,
    DateTimeOffset MetadataFrozenAt);

public sealed record RecordClassDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record RecordSeriesDto(
    Guid Id,
    Guid RecordClassId,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt);

public interface IRecordRepository
{
    Task<ManagedRecord?> FindAsync(RecordId id, CancellationToken cancellationToken);

    Task<ManagedRecord?> FindByDocumentAsync(DocumentId documentId, CancellationToken cancellationToken);

    Task<bool> ExistsForDocumentAsync(DocumentId documentId, CancellationToken cancellationToken);

    void Add(ManagedRecord record);

    Task<RecordClass?> FindClassAsync(RecordClassId id, CancellationToken cancellationToken);

    Task<RecordClass?> FindClassByCodeAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecordClass>> ListClassesAsync(CancellationToken cancellationToken);

    void AddClass(RecordClass recordClass);

    Task<RecordSeries?> FindSeriesAsync(RecordSeriesId id, CancellationToken cancellationToken);

    Task<RecordSeries?> FindSeriesByCodeAsync(RecordClassId classId, string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecordSeries>> ListSeriesAsync(RecordClassId? classId, CancellationToken cancellationToken);

    void AddSeries(RecordSeries series);
}

public interface IRecordReadModel
{
    Task<RecordSummaryDto?> GetByDocumentAsync(DocumentId documentId, CancellationToken cancellationToken);

    Task<RecordSummaryDto?> GetAsync(RecordId recordId, CancellationToken cancellationToken);
}
