using System.Text.Json;
using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;
using Microsoft.Extensions.Options;

namespace Dms.Documents.Application;

public sealed record CreateImportJobCommand(
    string Name,
    string SourceSystem,
    string ManifestJson,
    string? FilesRoot,
    string FailurePolicy,
    bool CreateMissingCategories,
    bool DryRunOnly,
    IReadOnlyList<ImportMappingInput>? Mappings) : ICommand<Result<Guid>>;

public sealed record ImportMappingInput(string Kind, string SourceKey, string TargetKey);

public sealed record ValidateImportJobCommand(Guid JobId) : ICommand<Result<ImportValidationReportDto>>;

public sealed record StartImportJobCommand(Guid JobId) : ICommand<Result>;

public sealed record PauseImportJobCommand(Guid JobId) : ICommand<Result>;

public sealed record ResumeImportJobCommand(Guid JobId) : ICommand<Result>;

public sealed record RetryFailedImportCommand(Guid JobId) : ICommand<Result>;

public sealed record ListImportJobsQuery : IQuery<Result<IReadOnlyList<ImportJobDto>>>;

public sealed record GetImportJobQuery(Guid JobId) : IQuery<Result<ImportJobDto>>;

public sealed record ListImportItemsQuery(Guid JobId, string? Status, int Skip, int Take)
    : IQuery<Result<IReadOnlyList<ImportItemDto>>>;

/// <summary>Command (not query): report access is audited and must commit.</summary>
public sealed record GetImportReportCommand(Guid JobId) : ICommand<Result<ImportReportDto>>;

public sealed record CreateClassificationLevelCommand(string Code, string Name, int Rank) : ICommand<Result<Guid>>;

public sealed record ListClassificationLevelsQuery : IQuery<Result<IReadOnlyList<ClassificationLevelInfo>>>;

public sealed class CreateImportJobHandler(
    IDmsAuthorizer authorizer,
    ImportJobFactory factory,
    IOptions<ImportOptions> options,
    ICurrentUser currentUser) : ICommandHandler<CreateImportJobCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateImportJobCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<Guid>(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.ImportManage, cancellationToken)).Allowed)
        {
            return Result.Failure<Guid>(DocumentErrors.Forbidden("Creating imports requires IMPORT_MANAGE."));
        }

        // Files root is server-configured only. A client may supply a relative subfolder under it.
        var configuredRoot = options.Value.FilesRoot;
        string? filesRoot = configuredRoot;
        if (!string.IsNullOrWhiteSpace(command.FilesRoot) && !string.IsNullOrWhiteSpace(configuredRoot))
        {
            var nested = ImportPathSafety.ResolveSafePath(configuredRoot, command.FilesRoot);
            if (nested.IsFailure)
            {
                return Result.Failure<Guid>(nested.Error);
            }

            filesRoot = nested.Value;
        }
        else if (!string.IsNullOrWhiteSpace(command.FilesRoot) && string.IsNullOrWhiteSpace(configuredRoot))
        {
            return Result.Failure<Guid>(Error.Validation(
                "import.files_root",
                "Import files root is not configured on the server (Dms:Import:FilesRoot)."));
        }

        if (!Enum.TryParse<ImportFailurePolicy>(command.FailurePolicy, true, out var policy))
        {
            policy = ImportFailurePolicy.ContinueOnError;
        }

        var created = await factory.CreateAsync(
            command.Name,
            command.SourceSystem,
            command.ManifestJson,
            filesRoot,
            policy,
            command.CreateMissingCategories,
            command.DryRunOnly,
            command.Mappings,
            actor,
            cancellationToken);
        return created.IsFailure ? Result.Failure<Guid>(created.Error) : Result.Success(created.Value.Id.Value);
    }
}

/// <summary>
/// Creates an import job and one item per manifest entry. Used by the API and by the legacy SQL
/// copy, which creates its job from a background process with no signed-in caller.
/// </summary>
public sealed class ImportJobFactory(
    IImportJobRepository jobs,
    IImportItemRepository items,
    IImportMappingRepository mappings,
    IAuditWriter audit,
    TimeProvider timeProvider)
{
    /// <param name="filesRoot">Absolute, already checked against Dms:Import:FilesRoot.</param>
    public async Task<Result<ImportJob>> CreateAsync(
        string name,
        string? sourceSystem,
        string manifestJson,
        string? filesRoot,
        ImportFailurePolicy policy,
        bool createMissingCategories,
        bool dryRunOnly,
        IReadOnlyList<ImportMappingInput>? mappingInputs,
        UserId actor,
        CancellationToken cancellationToken)
    {
        var parsed = ManifestSerializer.Parse(manifestJson);
        if (parsed.IsFailure)
        {
            return Result.Failure<ImportJob>(parsed.Error);
        }

        var source = string.IsNullOrWhiteSpace(sourceSystem)
            ? parsed.Value.Source ?? "legacy"
            : sourceSystem;

        var created = ImportJob.Create(
            source,
            name,
            filesRoot,
            ManifestSerializer.Hash(manifestJson),
            policy,
            createMissingCategories,
            dryRunOnly,
            actor,
            timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return created;
        }

        var job = created.Value;
        jobs.Add(job);

        if (mappingInputs is { Count: > 0 })
        {
            foreach (var mapping in mappingInputs)
            {
                if (!Enum.TryParse<ImportMappingKind>(mapping.Kind, true, out var kind))
                {
                    return Result.Failure<ImportJob>(Error.Validation("import.mapping_kind", $"Unknown mapping kind '{mapping.Kind}'."));
                }

                mappings.Add(ImportMapping.Create(
                    job.Id,
                    job.SourceSystem,
                    kind,
                    mapping.SourceKey,
                    mapping.TargetKey,
                    actor,
                    timeProvider.GetUtcNow()));
            }
        }

        var now = timeProvider.GetUtcNow();
        var batch = new List<ImportItem>(parsed.Value.Entries.Count);
        foreach (var entry in parsed.Value.Entries)
        {
            var relative = entry.ResolveRelativePath();
            var sourceId = entry.ResolveSourceId(job.SourceSystem);
            batch.Add(ImportItem.Create(
                job.Id,
                sourceId,
                relative,
                entry.Title,
                JsonSerializer.Serialize(entry, ManifestSerializer.Options),
                entry.Sha256?.Trim().ToLowerInvariant(),
                entry.Size,
                now));
        }

        items.AddRange(batch);

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.ImportCreated,
                UserId = actor,
                EntityType = "ImportJob",
                EntityId = job.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["sourceSystem"] = job.SourceSystem,
                    ["total"] = batch.Count,
                    ["dryRunOnly"] = job.DryRunOnly,
                },
            },
            cancellationToken);

        return Result.Success(job);
    }
}

public sealed class ValidateImportJobHandler(
    IDmsAuthorizer authorizer,
    IImportJobRepository jobs,
    IImportItemRepository items,
    ImportValidator validator,
    IJobQueue jobQueue,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<ValidateImportJobCommand, Result<ImportValidationReportDto>>
{
    public async Task<Result<ImportValidationReportDto>> HandleAsync(
        ValidateImportJobCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure<ImportValidationReportDto>(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.ImportManage, cancellationToken)).Allowed
            && !(await authorizer.AuthorizeSystemAsync(PermissionCodes.ImportRun, cancellationToken)).Allowed)
        {
            return Result.Failure<ImportValidationReportDto>(
                DocumentErrors.Forbidden("Validating imports requires IMPORT_MANAGE or IMPORT_RUN."));
        }

        var job = await jobs.FindAsync(new ImportJobId(command.JobId), cancellationToken);
        if (job is null)
        {
            return Result.Failure<ImportValidationReportDto>(Error.NotFound("import.not_found", "Import job not found."));
        }

        var now = timeProvider.GetUtcNow();
        var total = await items.CountByJobAsync(job.Id, cancellationToken);
        var begun = job.BeginValidation(total, now);
        if (begun.IsFailure)
        {
            return Result.Failure<ImportValidationReportDto>(begun.Error);
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.ImportValidationStarted,
                EntityType = "ImportJob",
                EntityId = job.Id.Value,
                Metadata = new Dictionary<string, object?> { ["total"] = total },
            },
            cancellationToken);

        if (total > ImportValidator.InlineLimit)
        {
            // Too many files to check within one request: the job worker checks them page by page
            // and the job's status and validatedItems show the progress.
            await items.ResetForValidationAsync(job.Id, now, cancellationToken);
            await jobQueue.EnqueueAsync(ImportValidateJob.Request(job.Id.Value, $"begin:{now.UtcTicks}"), cancellationToken);
            return Result.Success(new ImportValidationReportDto(
                job.Id.Value,
                job.Status.ToString(),
                total,
                0,
                0,
                0,
                []));
        }

        var report = await validator.ValidateAllAsync(job, cancellationToken);
        await ImportValidateJob.AuditCompletedAsync(audit, job, report.Warnings, cancellationToken);
        return Result.Success(report);
    }
}

/// <summary>
/// Validates a large import job one page per run, then queues the next page; the last run sets
/// the job to Ready or ValidationFailed. Each run commits on its own, so progress survives restarts.
/// </summary>
public sealed class ImportValidateJob(
    IImportJobRepository jobs,
    IImportItemRepository items,
    ImportValidator validator,
    IJobQueue jobQueue,
    IAuditWriter audit,
    TimeProvider timeProvider) : IJobHandler
{
    public const string Type = "documents.import-validate";

    public string JobType => Type;

    public static JobRequest Request(Guid jobId, string step) =>
        new(
            Type,
            new { jobId },
            IdempotencyKey: $"import-validate:{jobId}:{step}",
            MaxAttempts: 20);

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
        if (!doc.RootElement.TryGetProperty("jobId", out var jobIdProperty)
            || !jobIdProperty.TryGetGuid(out var jobIdValue))
        {
            return;
        }

        var job = await jobs.FindAsync(new ImportJobId(jobIdValue), cancellationToken);
        if (job is null || job.Status is not ImportJobStatus.Validating)
        {
            return;
        }

        var page = await items.ListByJobAsync(
            job.Id,
            ImportItemStatus.Pending,
            0,
            ImportValidator.PageSize,
            cancellationToken);
        if (page.Count == 0)
        {
            job.CompleteValidation(timeProvider.GetUtcNow());
            await AuditCompletedAsync(audit, job, warnings: null, cancellationToken);
            return;
        }

        await validator.ValidatePageAsync(job, page, cancellationToken);
        await jobQueue.EnqueueAsync(Request(job.Id.Value, $"after:{page[^1].Id.Value}"), cancellationToken);
    }

    public static Task AuditCompletedAsync(
        IAuditWriter audit,
        ImportJob job,
        int? warnings,
        CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.ImportValidationCompleted,
                UserId = job.CreatedBy,
                EntityType = "ImportJob",
                EntityId = job.Id.Value,
                Outcome = job.InvalidItems == 0 ? AuditOutcome.Success : AuditOutcome.Failed,
                Metadata = new Dictionary<string, object?>
                {
                    ["valid"] = job.ValidatedItems - job.InvalidItems,
                    ["invalid"] = job.InvalidItems,
                    ["warnings"] = warnings,
                },
            },
            cancellationToken);
}

public sealed class StartImportJobHandler(
    IDmsAuthorizer authorizer,
    IImportJobRepository jobs,
    IImportItemRepository items,
    IJobQueue jobQueue,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<StartImportJobCommand, Result>
{
    public async Task<Result> HandleAsync(StartImportJobCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.ImportRun, cancellationToken)).Allowed
            && !(await authorizer.AuthorizeSystemAsync(PermissionCodes.ImportManage, cancellationToken)).Allowed)
        {
            return Result.Failure(DocumentErrors.Forbidden("Starting imports requires IMPORT_RUN."));
        }

        var job = await jobs.FindAsync(new ImportJobId(command.JobId), cancellationToken);
        if (job is null)
        {
            return Result.Failure(Error.NotFound("import.not_found", "Import job not found."));
        }

        if (job.Status == ImportJobStatus.Created)
        {
            return Result.Failure(Error.Conflict("import.not_validated", "Validate the import before starting."));
        }

        var started = job.Start(timeProvider.GetUtcNow());
        if (started.IsFailure)
        {
            return started;
        }

        // One UPDATE for all items: a legacy archive can hold hundreds of thousands of them.
        var now = timeProvider.GetUtcNow();
        var (failedCount, failedBytes) = await items.MarkReadyAsync(
            job.Id,
            [ImportItemStatus.Valid, ImportItemStatus.Retryable, ImportItemStatus.Failed, ImportItemStatus.Running],
            now,
            cancellationToken);
        job.UncountFailures(failedCount, failedBytes, now);

        await jobQueue.EnqueueAsync(
            new JobRequest(
                ImportRunJob.Type,
                new { jobId = job.Id.Value },
                IdempotencyKey: $"import-run:{job.Id.Value}:{job.UpdatedAt.UtcTicks}",
                MaxAttempts: 20),
            cancellationToken);

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.ImportStarted,
                EntityType = "ImportJob",
                EntityId = job.Id.Value,
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class PauseImportJobHandler(
    IDmsAuthorizer authorizer,
    IImportJobRepository jobs,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<PauseImportJobCommand, Result>
{
    public async Task<Result> HandleAsync(PauseImportJobCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.ImportRun, cancellationToken)).Allowed)
        {
            return Result.Failure(DocumentErrors.Forbidden("Pausing imports requires IMPORT_RUN."));
        }

        var job = await jobs.FindAsync(new ImportJobId(command.JobId), cancellationToken);
        return job is null
            ? Result.Failure(Error.NotFound("import.not_found", "Import job not found."))
            : job.Pause(timeProvider.GetUtcNow());
    }
}

public sealed class ResumeImportJobHandler(
    IDmsAuthorizer authorizer,
    IImportJobRepository jobs,
    IJobQueue jobQueue,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<ResumeImportJobCommand, Result>
{
    public async Task<Result> HandleAsync(ResumeImportJobCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.ImportRun, cancellationToken)).Allowed)
        {
            return Result.Failure(DocumentErrors.Forbidden("Resuming imports requires IMPORT_RUN."));
        }

        var job = await jobs.FindAsync(new ImportJobId(command.JobId), cancellationToken);
        if (job is null)
        {
            return Result.Failure(Error.NotFound("import.not_found", "Import job not found."));
        }

        var resumed = job.Resume(timeProvider.GetUtcNow());
        if (resumed.IsFailure)
        {
            return resumed;
        }

        await jobQueue.EnqueueAsync(
            new JobRequest(
                ImportRunJob.Type,
                new { jobId = job.Id.Value },
                IdempotencyKey: $"import-run:{job.Id.Value}:{job.UpdatedAt.UtcTicks}",
                MaxAttempts: 20),
            cancellationToken);

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.ImportResumed,
                EntityType = "ImportJob",
                EntityId = job.Id.Value,
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class RetryFailedImportHandler(
    IDmsAuthorizer authorizer,
    IImportJobRepository jobs,
    IImportItemRepository items,
    IJobQueue jobQueue,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<RetryFailedImportCommand, Result>
{
    public async Task<Result> HandleAsync(RetryFailedImportCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.ImportRun, cancellationToken)).Allowed)
        {
            return Result.Failure(DocumentErrors.Forbidden("Retrying imports requires IMPORT_RUN."));
        }

        var job = await jobs.FindAsync(new ImportJobId(command.JobId), cancellationToken);
        if (job is null)
        {
            return Result.Failure(Error.NotFound("import.not_found", "Import job not found."));
        }

        var now = timeProvider.GetUtcNow();
        var (failedCount, failedBytes) = await items.MarkReadyAsync(
            job.Id,
            [ImportItemStatus.Failed, ImportItemStatus.Retryable],
            now,
            cancellationToken);
        job.UncountFailures(failedCount, failedBytes, now);

        var started = job.Start(now);
        if (started.IsFailure)
        {
            return started;
        }

        await jobQueue.EnqueueAsync(
            new JobRequest(
                ImportRunJob.Type,
                new { jobId = job.Id.Value },
                IdempotencyKey: $"import-retry:{job.Id.Value}:{now.UtcTicks}",
                MaxAttempts: 20),
            cancellationToken);
        return Result.Success();
    }
}

public sealed class ListImportJobsHandler(
    IDmsAuthorizer authorizer,
    IImportJobRepository jobs,
    ICurrentUser currentUser) : IQueryHandler<ListImportJobsQuery, Result<IReadOnlyList<ImportJobDto>>>
{
    public async Task<Result<IReadOnlyList<ImportJobDto>>> HandleAsync(
        ListImportJobsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure<IReadOnlyList<ImportJobDto>>(DocumentErrors.Unauthenticated);
        }

        if (!await CanViewAsync(authorizer, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<ImportJobDto>>(
                DocumentErrors.Forbidden("Viewing imports requires IMPORT_VIEW."));
        }

        var list = await jobs.ListAsync(100, cancellationToken);
        return Result.Success<IReadOnlyList<ImportJobDto>>(list.Select(ImportMaps.ToDto).ToList());
    }

    internal static async Task<bool> CanViewAsync(IDmsAuthorizer authorizer, CancellationToken cancellationToken) =>
        (await authorizer.AuthorizeSystemAsync(PermissionCodes.ImportView, cancellationToken)).Allowed
        || (await authorizer.AuthorizeSystemAsync(PermissionCodes.ImportManage, cancellationToken)).Allowed
        || (await authorizer.AuthorizeSystemAsync(PermissionCodes.ImportRun, cancellationToken)).Allowed;
}

public sealed class GetImportJobHandler(
    IDmsAuthorizer authorizer,
    IImportJobRepository jobs,
    ICurrentUser currentUser) : IQueryHandler<GetImportJobQuery, Result<ImportJobDto>>
{
    public async Task<Result<ImportJobDto>> HandleAsync(GetImportJobQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure<ImportJobDto>(DocumentErrors.Unauthenticated);
        }

        if (!await ListImportJobsHandler.CanViewAsync(authorizer, cancellationToken))
        {
            return Result.Failure<ImportJobDto>(DocumentErrors.Forbidden("Viewing imports requires IMPORT_VIEW."));
        }

        var job = await jobs.FindAsync(new ImportJobId(query.JobId), cancellationToken);
        return job is null
            ? Result.Failure<ImportJobDto>(Error.NotFound("import.not_found", "Import job not found."))
            : Result.Success(ImportMaps.ToDto(job));
    }
}

public sealed class ListImportItemsHandler(
    IDmsAuthorizer authorizer,
    IImportItemRepository items,
    ICurrentUser currentUser) : IQueryHandler<ListImportItemsQuery, Result<IReadOnlyList<ImportItemDto>>>
{
    public async Task<Result<IReadOnlyList<ImportItemDto>>> HandleAsync(
        ListImportItemsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure<IReadOnlyList<ImportItemDto>>(DocumentErrors.Unauthenticated);
        }

        if (!await ListImportJobsHandler.CanViewAsync(authorizer, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<ImportItemDto>>(
                DocumentErrors.Forbidden("Viewing import items requires IMPORT_VIEW."));
        }

        ImportItemStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status)
            && Enum.TryParse<ImportItemStatus>(query.Status, true, out var parsed))
        {
            status = parsed;
        }

        var list = await items.ListByJobAsync(
            new ImportJobId(query.JobId),
            status,
            query.Skip,
            query.Take <= 0 ? 100 : Math.Min(query.Take, 500),
            cancellationToken);
        return Result.Success<IReadOnlyList<ImportItemDto>>(list.Select(ImportMaps.ToDto).ToList());
    }
}

public sealed class GetImportReportHandler(
    IDmsAuthorizer authorizer,
    IImportJobRepository jobs,
    IImportItemRepository items,
    IAuditWriter audit,
    ICurrentUser currentUser) : ICommandHandler<GetImportReportCommand, Result<ImportReportDto>>
{
    public async Task<Result<ImportReportDto>> HandleAsync(
        GetImportReportCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure<ImportReportDto>(DocumentErrors.Unauthenticated);
        }

        if (!await ListImportJobsHandler.CanViewAsync(authorizer, cancellationToken))
        {
            return Result.Failure<ImportReportDto>(
                DocumentErrors.Forbidden("Viewing import reports requires IMPORT_VIEW."));
        }

        var job = await jobs.FindAsync(new ImportJobId(command.JobId), cancellationToken);
        if (job is null)
        {
            return Result.Failure<ImportReportDto>(Error.NotFound("import.not_found", "Import job not found."));
        }

        var errors = await items.ListByJobAsync(job.Id, ImportItemStatus.Failed, 0, 1000, cancellationToken);
        var invalid = await items.ListByJobAsync(job.Id, ImportItemStatus.Invalid, 0, 1000, cancellationToken);
        var combined = errors.Concat(invalid).Select(ImportMaps.ToDto).ToList();

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.ImportReportAccessed,
                EntityType = "ImportJob",
                EntityId = job.Id.Value,
            },
            cancellationToken);

        return Result.Success(new ImportReportDto(
            job.Id.Value,
            job.SourceSystem,
            job.Name,
            job.Status.ToString(),
            job.StartedAt,
            job.CompletedAt,
            job.TotalItems,
            job.SucceededItems,
            job.FailedItems,
            job.SkippedItems,
            job.InvalidItems,
            job.BytesProcessed,
            job.BytesTotal,
            combined));
    }
}

public sealed class CreateClassificationLevelHandler(
    IDmsAuthorizer authorizer,
    IClassificationLevelRepository levels,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<CreateClassificationLevelCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateClassificationLevelCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<Guid>(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageRecords, cancellationToken)).Allowed)
        {
            return Result.Failure<Guid>(DocumentErrors.Forbidden("Managing classification requires ADMIN_MANAGE_RECORDS."));
        }

        if (await levels.FindByCodeAsync(command.Code.Trim().ToUpperInvariant(), cancellationToken) is not null)
        {
            return Result.Failure<Guid>(Error.Conflict("classification.exists", "Classification code already exists."));
        }

        var created = ClassificationLevel.Create(
            command.Code,
            command.Name,
            command.Rank,
            actor,
            timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        levels.Add(created.Value);
        return Result.Success(created.Value.Id.Value);
    }
}

public sealed class ListClassificationLevelsHandler(
    IClassificationLevelRepository levels,
    ICurrentUser currentUser) : IQueryHandler<ListClassificationLevelsQuery, Result<IReadOnlyList<ClassificationLevelInfo>>>
{
    public async Task<Result<IReadOnlyList<ClassificationLevelInfo>>> HandleAsync(
        ListClassificationLevelsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure<IReadOnlyList<ClassificationLevelInfo>>(DocumentErrors.Unauthenticated);
        }

        var list = await levels.ListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<ClassificationLevelInfo>>(list
            .Select(level => new ClassificationLevelInfo(level.Id.Value, level.Code, level.Name, level.IsActive))
            .ToList());
    }
}

public sealed class ClassificationCatalogService(IClassificationLevelRepository levels) : IClassificationCatalog
{
    public async Task<ClassificationLevelInfo?> FindByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var level = await levels.FindByCodeAsync(code.Trim().ToUpperInvariant(), cancellationToken);
        return level is null
            ? null
            : new ClassificationLevelInfo(level.Id.Value, level.Code, level.Name, level.IsActive);
    }

    public async Task<Result> AssignAsync(
        DocumentId documentId,
        string code,
        UserId actor,
        CancellationToken cancellationToken)
    {
        var level = await levels.FindByCodeAsync(code.Trim().ToUpperInvariant(), cancellationToken);
        if (level is null || !level.IsActive)
        {
            return Result.Failure(Error.Validation(
                "IMPORT_UNKNOWN_CLASSIFICATION",
                $"Unknown classification '{code}'."));
        }

        levels.Assign(new DocumentClassification(documentId, level.Id, actor, DateTimeOffset.UtcNow));
        return Result.Success();
    }
}

public static class ImportMaps
{
    public static ImportJobDto ToDto(ImportJob job) => new(
        job.Id.Value,
        job.SourceSystem,
        job.Name,
        job.Status.ToString(),
        job.FailurePolicy.ToString(),
        job.CreateMissingCategories,
        job.DryRunOnly,
        job.TotalItems,
        job.ProcessedItems,
        job.SucceededItems,
        job.FailedItems,
        job.SkippedItems,
        job.InvalidItems,
        job.BytesProcessed,
        job.BytesTotal,
        job.LastError,
        job.CreatedBy.Value,
        job.CreatedAt,
        job.UpdatedAt,
        job.StartedAt,
        job.CompletedAt,
        job.ValidatedItems);

    public static ImportItemDto ToDto(ImportItem item) => new(
        item.Id.Value,
        item.ImportJobId.Value,
        item.SourceId,
        item.SourcePath,
        item.Title,
        item.Status.ToString(),
        item.ErrorCode,
        item.ErrorMessage,
        item.ExpectedSha256,
        item.ActualSha256,
        item.ExpectedSize,
        item.ActualSize,
        item.TargetDocumentId?.Value,
        item.TargetRecordId?.Value,
        item.TargetVersionId?.Value,
        item.RetryCount,
        item.CreatedAt,
        item.StartedAt,
        item.CompletedAt);
}

public sealed class ImportRunJob(
    IImportJobRepository jobs,
    IImportItemRepository items,
    ImportItemImporter importer,
    IJobQueue jobQueue,
    IAuditWriter audit,
    TimeProvider timeProvider) : IJobHandler
{
    public const string Type = "documents.import-run";

    public string JobType => Type;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
        if (!doc.RootElement.TryGetProperty("jobId", out var jobIdProperty)
            || !jobIdProperty.TryGetGuid(out var jobIdValue))
        {
            return;
        }

        var job = await jobs.FindAsync(new ImportJobId(jobIdValue), cancellationToken);
        if (job is null || job.Status is not ImportJobStatus.Running)
        {
            return;
        }

        // One item per job execution → item-level transaction via JobRunner Commit.
        var batch = await items.ClaimBatchAsync(job.Id, batchSize: 1, cancellationToken);
        if (batch.Count == 0)
        {
            var remaining = await items.CountByStatusAsync(job.Id, ImportItemStatus.Ready, cancellationToken)
                + await items.CountByStatusAsync(job.Id, ImportItemStatus.Retryable, cancellationToken);
            if (remaining == 0)
            {
                job.Finish(stopRequested: false, timeProvider.GetUtcNow());
                await audit.WriteAsync(
                    new AuditRecord
                    {
                        Action = job.Status == ImportJobStatus.CompletedWithErrors
                            ? AuditActions.ImportCompletedWithErrors
                            : AuditActions.ImportCompleted,
                        EntityType = "ImportJob",
                        EntityId = job.Id.Value,
                        Metadata = new Dictionary<string, object?>
                        {
                            ["succeeded"] = job.SucceededItems,
                            ["failed"] = job.FailedItems,
                            ["skipped"] = job.SkippedItems,
                        },
                    },
                    cancellationToken);
            }

            return;
        }

        var item = batch[0];
        await importer.ImportAsync(job, item, job.CreatedBy, cancellationToken);

        if (job.FailurePolicy == ImportFailurePolicy.StopOnError
            && item.Status is ImportItemStatus.Failed or ImportItemStatus.Retryable)
        {
            job.Finish(stopRequested: true, timeProvider.GetUtcNow());
            await audit.WriteAsync(
                new AuditRecord
                {
                    Action = AuditActions.ImportFailed,
                    EntityType = "ImportJob",
                    EntityId = job.Id.Value,
                    Outcome = AuditOutcome.Failed,
                },
                cancellationToken);
            return;
        }

        if (job.Status == ImportJobStatus.Running)
        {
            await jobQueue.EnqueueAsync(
                new JobRequest(
                    Type,
                    new { jobId = job.Id.Value },
                    IdempotencyKey: $"import-run-continue:{job.Id.Value}:{item.Id.Value}",
                    MaxAttempts: 20),
                cancellationToken);
        }
    }
}
