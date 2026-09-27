using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;

namespace Dms.Documents.Application;

public sealed record CreateRecordClassCommand(string Code, string Name, string? Description)
    : ICommand<Result<Guid>>;

public sealed record UpdateRecordClassCommand(Guid Id, string Name, string? Description, bool IsActive)
    : ICommand<Result>;

public sealed record CreateRecordSeriesCommand(Guid RecordClassId, string Code, string Name, string? Description)
    : ICommand<Result<Guid>>;

public sealed record UpdateRecordSeriesCommand(Guid Id, string Name, string? Description, bool IsActive)
    : ICommand<Result>;

public sealed record DeclareRecordCommand(
    Guid DocumentId,
    Guid RecordClassId,
    Guid? RecordSeriesId,
    Guid? FinalVersionId,
    string? Reason) : ICommand<Result<Guid>>;

public sealed record TransitionRecordCommand(Guid RecordId, RecordStatus Status, string? Reason)
    : ICommand<Result>;

public sealed record GetRecordByDocumentQuery(Guid DocumentId) : IQuery<Result<RecordSummaryDto>>;

public sealed record GetRecordQuery(Guid RecordId) : IQuery<Result<RecordSummaryDto>>;

public sealed record ListRecordClassesQuery : IQuery<Result<IReadOnlyList<RecordClassDto>>>;

public sealed record ListRecordSeriesQuery(Guid? RecordClassId) : IQuery<Result<IReadOnlyList<RecordSeriesDto>>>;

public sealed class RecordImmutabilityGuard(IRecordRepository records) : IRecordImmutabilityGuard
{
    public async Task<Result> EnsureMutableAsync(DocumentId documentId, CancellationToken cancellationToken)
    {
        var record = await records.FindByDocumentAsync(documentId, cancellationToken);
        return RecordGate.EnsureMutable(record is { IsImmutable: true });
    }

    public async Task<bool> IsRecordAsync(DocumentId documentId, CancellationToken cancellationToken)
    {
        var record = await records.FindByDocumentAsync(documentId, cancellationToken);
        return record is { IsImmutable: true };
    }
}

public sealed class CreateRecordClassHandler(
    IDmsAuthorizer authorizer,
    IRecordRepository records,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<CreateRecordClassCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateRecordClassCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<Guid>(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageRecords, cancellationToken)).Allowed)
        {
            return Result.Failure<Guid>(DocumentErrors.Forbidden("Managing record classes requires ADMIN_MANAGE_RECORDS."));
        }

        if (await records.FindClassByCodeAsync(command.Code.Trim().ToUpperInvariant(), cancellationToken) is not null)
        {
            return Result.Failure<Guid>(Error.Conflict("record_class.code_taken", "A record class with this code already exists."));
        }

        var created = RecordClass.Create(command.Code, command.Name, command.Description, actor, timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        records.AddClass(created.Value);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.RecordClassCreated,
                EntityType = "RecordClass",
                EntityId = created.Value.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["code"] = created.Value.Code,
                    ["name"] = created.Value.Name,
                },
            },
            cancellationToken);

        return Result.Success(created.Value.Id.Value);
    }
}

public sealed class UpdateRecordClassHandler(
    IDmsAuthorizer authorizer,
    IRecordRepository records,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<UpdateRecordClassCommand, Result>
{
    public async Task<Result> HandleAsync(UpdateRecordClassCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        if ((!(await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageRecords, cancellationToken)).Allowed))
        {
            return Result.Failure(DocumentErrors.Forbidden("Managing record classes requires ADMIN_MANAGE_RECORDS."));
        }

        var entity = await records.FindClassAsync(new RecordClassId(command.Id), cancellationToken);
        if (entity is null)
        {
            return Result.Failure(Error.NotFound("record_class.not_found", "The record class does not exist."));
        }

        var updated = entity.Update(command.Name, command.Description, command.IsActive, timeProvider.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated;
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.RecordClassUpdated,
                EntityType = "RecordClass",
                EntityId = entity.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["name"] = entity.Name,
                    ["isActive"] = entity.IsActive,
                },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class CreateRecordSeriesHandler(
    IDmsAuthorizer authorizer,
    IRecordRepository records,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<CreateRecordSeriesCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateRecordSeriesCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<Guid>(DocumentErrors.Unauthenticated);
        }

        if ((!(await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageRecords, cancellationToken)).Allowed))
        {
            return Result.Failure<Guid>(DocumentErrors.Forbidden("Managing record series requires ADMIN_MANAGE_RECORDS."));
        }

        var recordClass = await records.FindClassAsync(new RecordClassId(command.RecordClassId), cancellationToken);
        if (recordClass is null || !recordClass.IsActive)
        {
            return Result.Failure<Guid>(Error.NotFound("record_class.not_found", "The record class does not exist."));
        }

        if (await records.FindSeriesByCodeAsync(recordClass.Id, command.Code.Trim().ToUpperInvariant(), cancellationToken) is not null)
        {
            return Result.Failure<Guid>(Error.Conflict("record_series.code_taken", "A series with this code already exists in the class."));
        }

        var created = RecordSeries.Create(
            recordClass.Id,
            command.Code,
            command.Name,
            command.Description,
            actor,
            timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        records.AddSeries(created.Value);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.RecordSeriesCreated,
                EntityType = "RecordSeries",
                EntityId = created.Value.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["code"] = created.Value.Code,
                    ["recordClassId"] = recordClass.Id.Value,
                },
            },
            cancellationToken);

        return Result.Success(created.Value.Id.Value);
    }
}

public sealed class UpdateRecordSeriesHandler(
    IDmsAuthorizer authorizer,
    IRecordRepository records,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<UpdateRecordSeriesCommand, Result>
{
    public async Task<Result> HandleAsync(UpdateRecordSeriesCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        if ((!(await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageRecords, cancellationToken)).Allowed))
        {
            return Result.Failure(DocumentErrors.Forbidden("Managing record series requires ADMIN_MANAGE_RECORDS."));
        }

        var entity = await records.FindSeriesAsync(new RecordSeriesId(command.Id), cancellationToken);
        if (entity is null)
        {
            return Result.Failure(Error.NotFound("record_series.not_found", "The record series does not exist."));
        }

        var updated = entity.Update(command.Name, command.Description, command.IsActive, timeProvider.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated;
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.RecordSeriesUpdated,
                EntityType = "RecordSeries",
                EntityId = entity.Id.Value,
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class DeclareRecordHandler(
    DocumentAccess access,
    IDocumentRepository documents,
    IRecordRepository records,
    IAuditWriter audit,
    DocumentChanges changes,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<DeclareRecordCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(DeclareRecordCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<Guid>(DocumentErrors.Unauthenticated);
        }

        var allowed = await access.RequireAsync(command.DocumentId, PermissionCodes.RecordDeclare, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<Guid>(allowed.Error);
        }

        var document = await documents.FindForUpdateAsync(new DocumentId(command.DocumentId), cancellationToken);
        if (document is null)
        {
            return Result.Failure<Guid>(DocumentErrors.DocumentNotFound);
        }

        if (await records.ExistsForDocumentAsync(document.Id, cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict(
                "record.already_declared",
                "This document is already declared as a Record."));
        }

        var recordClass = await records.FindClassAsync(new RecordClassId(command.RecordClassId), cancellationToken);
        if (recordClass is null || !recordClass.IsActive)
        {
            return Result.Failure<Guid>(Error.NotFound("record_class.not_found", "The record class does not exist."));
        }

        RecordSeriesId? seriesId = null;
        if (command.RecordSeriesId is { } seriesGuid)
        {
            var series = await records.FindSeriesAsync(new RecordSeriesId(seriesGuid), cancellationToken);
            if (series is null || !series.IsActive || series.RecordClassId != recordClass.Id)
            {
                return Result.Failure<Guid>(Error.Validation(
                    "record.series",
                    "The record series must belong to the selected class."));
            }

            seriesId = series.Id;
        }

        var finalVersionId = command.FinalVersionId is { } explicitId
            ? new DocumentVersionId(explicitId)
            : document.EffectiveVersionId ?? document.CurrentVersionId;

        if (finalVersionId is null)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "record.no_version",
                "A Record needs a final version; the document has none."));
        }

        var declared = ManagedRecord.Declare(
            document,
            finalVersionId.Value,
            recordClass.Id,
            seriesId,
            actor,
            timeProvider.GetUtcNow());
        if (declared.IsFailure)
        {
            return Result.Failure<Guid>(declared.Error);
        }

        records.Add(declared.Value);

        var final = document.Versions.First(version => version.Id == declared.Value.FinalVersionId);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.RecordDeclared,
                EntityType = "Record",
                EntityId = declared.Value.Id.Value,
                DocumentId = document.Id.Value,
                VersionId = final.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["recordClassId"] = recordClass.Id.Value,
                    ["recordClassCode"] = recordClass.Code,
                    ["recordSeriesId"] = seriesId?.Value,
                    ["finalVersion"] = final.Label,
                    ["reason"] = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim(),
                    ["metadataFrozenAt"] = declared.Value.MetadataFrozenAt,
                },
            },
            cancellationToken);

        await changes.NotifyAsync(document.Id.Value, cancellationToken);
        return Result.Success(declared.Value.Id.Value);
    }
}

public sealed class TransitionRecordHandler(
    IDmsAuthorizer authorizer,
    DocumentAccess access,
    IRecordRepository records,
    IAuditWriter audit,
    DocumentChanges changes,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<TransitionRecordCommand, Result>
{
    public async Task<Result> HandleAsync(TransitionRecordCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        var record = await records.FindAsync(new RecordId(command.RecordId), cancellationToken);
        if (record is null)
        {
            return Result.Failure(Error.NotFound("record.not_found", "The Record does not exist."));
        }

        var canManage = (await authorizer.AuthorizeSystemAsync(
            PermissionCodes.AdminManageRecords,
            cancellationToken)).Allowed;
        if (!canManage)
        {
            var onDocument = await access.RequireAsync(
                record.DocumentId.Value,
                PermissionCodes.RecordDeclare,
                cancellationToken);
            if (onDocument.IsFailure)
            {
                return onDocument;
            }
        }

        var from = record.Status;
        var transitioned = record.TransitionTo(command.Status, command.Reason, actor, timeProvider.GetUtcNow());
        if (transitioned.IsFailure)
        {
            return transitioned;
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.RecordStatusChanged,
                EntityType = "Record",
                EntityId = record.Id.Value,
                DocumentId = record.DocumentId.Value,
                VersionId = record.FinalVersionId.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["from"] = from.ToString(),
                    ["to"] = record.Status.ToString(),
                    ["reason"] = record.LastTransitionReason,
                },
            },
            cancellationToken);

        await changes.NotifyAsync(record.DocumentId.Value, cancellationToken);
        return Result.Success();
    }
}

public sealed class GetRecordByDocumentHandler(
    DocumentAccess access,
    IRecordReadModel readModel) : IQueryHandler<GetRecordByDocumentQuery, Result<RecordSummaryDto>>
{
    public async Task<Result<RecordSummaryDto>> HandleAsync(
        GetRecordByDocumentQuery query,
        CancellationToken cancellationToken)
    {
        var visible = await access.RequireAsync(query.DocumentId, PermissionCodes.DocumentView, cancellationToken);
        if (visible.IsFailure)
        {
            return Result.Failure<RecordSummaryDto>(visible.Error);
        }

        var record = await readModel.GetByDocumentAsync(new DocumentId(query.DocumentId), cancellationToken);
        return record is null
            ? Result.Failure<RecordSummaryDto>(Error.NotFound("record.not_found", "No Record is declared for this document."))
            : Result.Success(record);
    }
}

public sealed class GetRecordHandler(
    DocumentAccess access,
    IRecordReadModel readModel) : IQueryHandler<GetRecordQuery, Result<RecordSummaryDto>>
{
    public async Task<Result<RecordSummaryDto>> HandleAsync(GetRecordQuery query, CancellationToken cancellationToken)
    {
        var record = await readModel.GetAsync(new RecordId(query.RecordId), cancellationToken);
        if (record is null)
        {
            return Result.Failure<RecordSummaryDto>(Error.NotFound("record.not_found", "The Record does not exist."));
        }

        var visible = await access.RequireAsync(record.DocumentId, PermissionCodes.DocumentView, cancellationToken);
        if (visible.IsFailure)
        {
            return Result.Failure<RecordSummaryDto>(visible.Error);
        }

        return Result.Success(record);
    }
}

public sealed class ListRecordClassesHandler(
    IRecordRepository records,
    ICurrentUser currentUser) : IQueryHandler<ListRecordClassesQuery, Result<IReadOnlyList<RecordClassDto>>>
{
    public async Task<Result<IReadOnlyList<RecordClassDto>>> HandleAsync(
        ListRecordClassesQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure<IReadOnlyList<RecordClassDto>>(DocumentErrors.Unauthenticated);
        }

        var list = await records.ListClassesAsync(cancellationToken);
        return Result.Success<IReadOnlyList<RecordClassDto>>(list
            .OrderBy(item => item.Code)
            .Select(item => new RecordClassDto(
                item.Id.Value,
                item.Code,
                item.Name,
                item.Description,
                item.IsActive,
                item.CreatedAt))
            .ToList());
    }
}

public sealed class ListRecordSeriesHandler(
    IRecordRepository records,
    ICurrentUser currentUser) : IQueryHandler<ListRecordSeriesQuery, Result<IReadOnlyList<RecordSeriesDto>>>
{
    public async Task<Result<IReadOnlyList<RecordSeriesDto>>> HandleAsync(
        ListRecordSeriesQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure<IReadOnlyList<RecordSeriesDto>>(DocumentErrors.Unauthenticated);
        }

        RecordClassId? classId = query.RecordClassId is { } id ? new RecordClassId(id) : null;
        var list = await records.ListSeriesAsync(classId, cancellationToken);
        return Result.Success<IReadOnlyList<RecordSeriesDto>>(list
            .OrderBy(item => item.Code)
            .Select(item => new RecordSeriesDto(
                item.Id.Value,
                item.RecordClassId.Value,
                item.Code,
                item.Name,
                item.Description,
                item.IsActive,
                item.CreatedAt))
            .ToList());
    }
}
