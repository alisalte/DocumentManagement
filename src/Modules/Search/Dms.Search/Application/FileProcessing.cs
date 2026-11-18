using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Documents.Contracts;
using Dms.SharedKernel;
using Dms.Storage.Contracts;

namespace Dms.Search.Application;

public sealed record MalwareScanStateQuery : IQuery<Result<MalwareScanState>>;

public sealed record SetMalwareScanningCommand(bool Enabled) : ICommand<Result<MalwareScanState>>;

public sealed record ReprocessAllFilesCommand : ICommand<Result>;

/// <summary>Read the file text of one document again (OCR): the current and effective versions.</summary>
public sealed record RereadDocumentTextCommand(Guid DocumentId) : ICommand<Result<int>>;

/// <summary>
/// The malware scan and bulk OCR sit with the other file-processing controls on the search and
/// OCR page, so ADMIN_MANAGE_SEARCH governs them.
/// </summary>
internal static class FileProcessingGate
{
    public static async Task<Error?> CheckAsync(IDmsAuthorizer authorizer, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageSearch, cancellationToken);
        return decision.Allowed ? null : Error.Forbidden("auth.forbidden", decision.Explanation);
    }
}

public sealed class MalwareScanStateHandler(IDmsAuthorizer authorizer, IFileProcessingAdmin files)
    : IQueryHandler<MalwareScanStateQuery, Result<MalwareScanState>>
{
    public async Task<Result<MalwareScanState>> HandleAsync(MalwareScanStateQuery query, CancellationToken cancellationToken)
    {
        if (await FileProcessingGate.CheckAsync(authorizer, cancellationToken) is { } error)
        {
            return Result.Failure<MalwareScanState>(error);
        }

        return Result.Success(await files.GetScanStateAsync(cancellationToken));
    }
}

public sealed class SetMalwareScanningHandler(IDmsAuthorizer authorizer, IFileProcessingAdmin files, IAuditWriter audit)
    : ICommandHandler<SetMalwareScanningCommand, Result<MalwareScanState>>
{
    public async Task<Result<MalwareScanState>> HandleAsync(SetMalwareScanningCommand command, CancellationToken cancellationToken)
    {
        if (await FileProcessingGate.CheckAsync(authorizer, cancellationToken) is { } error)
        {
            return Result.Failure<MalwareScanState>(error);
        }

        var before = await files.GetScanStateAsync(cancellationToken);
        if (command.Enabled && !before.ScannerConfigured)
        {
            return Result.Failure<MalwareScanState>(Error.Conflict(
                "storage.scanner_not_configured",
                "No malware scanner (ClamAV) is configured for this server, so the scan cannot be switched on here."));
        }

        await files.SetScanningAsync(command.Enabled, cancellationToken);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.MalwareScanningChanged,
                EntityType = "Setting",
                Metadata = new Dictionary<string, object?>
                {
                    ["enabled"] = command.Enabled,
                    ["waitingFiles"] = before.WaitingFiles,
                },
            },
            cancellationToken);

        return Result.Success(await files.GetScanStateAsync(cancellationToken));
    }
}

public sealed class ReprocessAllFilesHandler(IDmsAuthorizer authorizer, IFileProcessingAdmin files, IAuditWriter audit)
    : ICommandHandler<ReprocessAllFilesCommand, Result>
{
    public async Task<Result> HandleAsync(ReprocessAllFilesCommand command, CancellationToken cancellationToken)
    {
        if (await FileProcessingGate.CheckAsync(authorizer, cancellationToken) is { } error)
        {
            return Result.Failure(error);
        }

        await files.QueueReprocessAllAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditRecord { Action = AuditActions.FilesReprocessQueued, EntityType = "Setting" },
            cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// "Read this document again": whoever may change the document may ask for its text to be read
/// again; nothing is revealed by it. Files whose malware scan has not passed are refused with a
/// clear reason, and text corrected by hand is kept.
/// </summary>
public sealed class RereadDocumentTextHandler(
    IDmsAuthorizer authorizer,
    IDocumentIndexSource documents,
    IContentExtractionRepository extractions,
    IStorageService storage,
    IJobQueue jobs,
    IAuditWriter audit) : ICommandHandler<RereadDocumentTextCommand, Result<int>>
{
    public static readonly Error ScanPending = Error.Conflict(
        "search.scan_pending",
        "The malware scan of this file has not passed yet. Switch the scan off on the search and OCR page, or wait for it.");

    public static readonly Error HandEdited = Error.Conflict(
        "search.text_edited",
        "The text was corrected by hand; reading the file again would overwrite the correction.");

    public async Task<Result<int>> HandleAsync(RereadDocumentTextCommand command, CancellationToken cancellationToken)
    {
        var resource = ResourceRef.Document(command.DocumentId);
        var view = await authorizer.AuthorizeAsync(PermissionCodes.DocumentView, resource, cancellationToken);
        var document = view.Allowed ? await documents.GetAsync(command.DocumentId, cancellationToken) : null;
        if (document is null)
        {
            return Result.Failure<int>(Error.NotFound("document.not_found", "The document does not exist."));
        }

        var edit = await authorizer.AuthorizeAsync(PermissionCodes.DocumentEdit, resource, cancellationToken);
        if (!edit.Allowed)
        {
            return Result.Failure<int>(Error.Forbidden("auth.forbidden", edit.Explanation));
        }

        var files = document.Versions
            .Where(version => version.VersionId == document.CurrentVersionId || version.VersionId == document.EffectiveVersionId)
            .Select(version => version.StorageObjectId)
            .Distinct()
            .ToList();

        var queued = 0;
        Error? refusal = null;
        foreach (var file in files)
        {
            var info = await storage.FindAsync(new StorageObjectId(file), cancellationToken);
            if (info is null || !info.IsContentAvailable)
            {
                refusal ??= ScanPending;
                continue;
            }

            // No row yet (never processed, e.g. an old import): the job creates it and reads the file.
            if (await extractions.FindAsync(file, cancellationToken) is { } extraction && extraction.Requeue().IsFailure)
            {
                refusal ??= HandEdited;
                continue;
            }

            await jobs.EnqueueAsync(ExtractTextJob.For(file), cancellationToken);
            queued++;
        }

        if (queued == 0)
        {
            return Result.Failure<int>(refusal ?? ExtractedTextAccess.NotFound);
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.ExtractedTextRequeued,
                EntityType = "Document",
                EntityId = command.DocumentId,
                DocumentId = command.DocumentId,
                Metadata = new Dictionary<string, object?> { ["files"] = queued },
            },
            cancellationToken);
        return Result.Success(queued);
    }
}
