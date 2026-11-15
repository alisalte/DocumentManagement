using System.Text.Json;
using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Documents.Domain;
using Dms.DocumentTypes.Contracts;
using Dms.SharedKernel;

namespace Dms.Documents.Application;

/// <summary>How a document got its number, recorded in the activity log.</summary>
public enum IndicatorSource
{
    Manual,
    Import,
    Backfill,
}

/// <summary>
/// Builds and assigns indicator numbers: the type's pattern (or the default), the next value of the
/// counter for the document's type and fiscal year, and the category code if the pattern uses it.
/// Call it as the last fallible step of a command, so a failure afterwards cannot burn a number.
/// </summary>
public sealed class IndicatorNumberAllocator(
    IDocumentTypeCatalog catalog,
    IIndicatorCounter counter,
    ICategoryRepository categories,
    IDocumentRepository documents,
    IAuditWriter audit,
    TimeProvider timeProvider)
{
    // A numbering run assigns hundreds of numbers; types and categories rarely change in between.
    private readonly Dictionary<DocumentTypeId, IndicatorPatternInfo?> _patterns = [];
    private readonly Dictionary<CategoryId, string?> _categoryCodes = [];

    public static readonly Error Duplicate = Error.Conflict(
        "document.indicator_duplicate",
        "Another document already has this indicator number. Check the indicator patterns of the document types.");

    public async Task<Result<string>> AssignAsync(
        Document document,
        UserId? by,
        IndicatorSource source,
        CancellationToken cancellationToken)
    {
        if (document.HasIndicatorNumber)
        {
            return Result.Failure<string>(Error.Conflict(
                "document.indicator_already_registered",
                "This document already has an indicator number."));
        }

        if (!_patterns.TryGetValue(document.DocumentTypeId, out var info))
        {
            info = await catalog.GetIndicatorPatternAsync(document.DocumentTypeId, cancellationToken);
            _patterns[document.DocumentTypeId] = info;
        }

        if (info is null)
        {
            return Result.Failure<string>(Error.NotFound("document_type.not_found", "The document type does not exist."));
        }

        var pattern = IndicatorPattern.Parse(info.Pattern);
        if (pattern.IsFailure)
        {
            return Result.Failure<string>(pattern.Error);
        }

        string? categoryCode = null;
        if (pattern.Value!.Text.Contains("{CAT}", StringComparison.Ordinal)
            && !_categoryCodes.TryGetValue(document.CategoryId, out categoryCode))
        {
            categoryCode = (await categories.FindAsync(document.CategoryId, cancellationToken))?.Code;
            _categoryCodes[document.CategoryId] = categoryCode;
        }

        var sequence = await counter.NextAsync(document.DocumentTypeId, document.FiscalYear, cancellationToken);
        var number = pattern.Value.Format(info.TypeCode, document.FiscalYear, categoryCode, sequence);

        // The pattern policy keeps types from colliding; this turns a remaining clash into a clear
        // error instead of a unique-index failure at commit.
        if (await documents.IndicatorNumberExistsAsync(number, cancellationToken))
        {
            return Result.Failure<string>(Duplicate);
        }

        var registered = document.RegisterIndicator(number, sequence, by, timeProvider.GetUtcNow());
        if (registered.IsFailure)
        {
            return Result.Failure<string>(registered.Error);
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DocumentIndicatorRegistered,
                EntityType = "Document",
                EntityId = document.Id.Value,
                DocumentId = document.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["number"] = number,
                    ["source"] = source.ToString().ToLowerInvariant(),
                },
            },
            cancellationToken);

        return Result.Success(number);
    }
}

public sealed record RegisterIndicatorCommand(Guid DocumentId) : ICommand<Result<string>>;

/// <summary>
/// "Register in the indicator": a user with DOCUMENT_REGISTER_INDICATOR gives a document its number
/// (BR-2, BR-12). Deliberately not blocked by closed fiscal years or record immutability (BR-17).
/// </summary>
public sealed class RegisterIndicatorHandler(
    DocumentAccess access,
    IDocumentRepository documents,
    IndicatorNumberAllocator allocator,
    DocumentChanges changes,
    ICurrentUser currentUser) : ICommandHandler<RegisterIndicatorCommand, Result<string>>
{
    public async Task<Result<string>> HandleAsync(RegisterIndicatorCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<string>(DocumentErrors.Unauthenticated);
        }

        var allowed = await access.RequireAsync(command.DocumentId, PermissionCodes.DocumentRegisterIndicator, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<string>(allowed.Error);
        }

        // Locked so two clicks on the same document cannot both pass the "no number yet" check.
        var document = await documents.FindForUpdateAsync(new DocumentId(command.DocumentId), cancellationToken);
        if (document is null)
        {
            return Result.Failure<string>(DocumentErrors.DocumentNotFound);
        }

        var number = await allocator.AssignAsync(document, actor, IndicatorSource.Manual, cancellationToken);
        if (number.IsFailure)
        {
            return number;
        }

        await changes.NotifyAsync(document.Id.Value, cancellationToken);
        return number;
    }
}

/// <summary>
/// Numbers documents that have none, oldest first, in batches: every document when an administrator
/// starts the backfill (BR-18), or the documents of one import job when it finishes (BR-6, BR-19).
/// Each batch is one transaction; the job queues its own continuation until nothing is left.
/// </summary>
public sealed class IndicatorNumberingJob(
    IDocumentRepository documents,
    IndicatorNumberAllocator allocator,
    DocumentChanges changes,
    IJobQueue jobQueue) : IJobHandler
{
    public const string Type = "documents.indicator-numbering";
    public const int BatchSize = 200;

    public string JobType => Type;

    public static JobRequest Request(Guid? importJobId, string step) => new(
        Type,
        new { importJobId },
        IdempotencyKey: $"{Type}:{importJobId?.ToString() ?? "all"}:{step}",
        MaxAttempts: 5);

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        Guid? importJobId = null;
        using (var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload))
        {
            if (json.RootElement.TryGetProperty("importJobId", out var value) && value.ValueKind == JsonValueKind.String)
            {
                importJobId = value.GetGuid();
            }
        }

        var batch = await documents.ClaimUnnumberedAsync(importJobId, BatchSize, cancellationToken);
        var source = importJobId is null ? IndicatorSource.Backfill : IndicatorSource.Import;
        foreach (var document in batch)
        {
            var number = await allocator.AssignAsync(document, by: null, source, cancellationToken);
            if (number.IsFailure)
            {
                // Rolls the whole batch back; a skipped document would be claimed again forever.
                throw new InvalidOperationException(
                    $"Numbering document {document.Id.Value} failed: {number.Error.Code} {number.Error.Message}");
            }

            await changes.NotifyAsync(document.Id.Value, cancellationToken);
        }

        if (batch.Count == BatchSize)
        {
            await jobQueue.EnqueueAsync(Request(importJobId, batch[^1].Id.Value.ToString()), cancellationToken);
        }
    }
}

public sealed record IndicatorBackfillDto(int Unnumbered);

public sealed record GetIndicatorBackfillQuery : IQuery<Result<IndicatorBackfillDto>>;

public sealed record StartIndicatorBackfillCommand : ICommand<Result<IndicatorBackfillDto>>;

public sealed class GetIndicatorBackfillHandler(IDmsAuthorizer authorizer, IDocumentRepository documents)
    : IQueryHandler<GetIndicatorBackfillQuery, Result<IndicatorBackfillDto>>
{
    public async Task<Result<IndicatorBackfillDto>> HandleAsync(GetIndicatorBackfillQuery query, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageDocumentTypes, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<IndicatorBackfillDto>(DocumentErrors.Forbidden(decision.Explanation));
        }

        return Result.Success(new IndicatorBackfillDto(await documents.CountUnnumberedAsync(cancellationToken)));
    }
}

/// <summary>
/// Starts numbering every document that has no indicator number yet. Not automatic (AQ5): the
/// numbers can never change, so an administrator runs it once the patterns are right.
/// </summary>
public sealed class StartIndicatorBackfillHandler(
    IDmsAuthorizer authorizer,
    IDocumentRepository documents,
    IJobQueue jobQueue,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<StartIndicatorBackfillCommand, Result<IndicatorBackfillDto>>
{
    public async Task<Result<IndicatorBackfillDto>> HandleAsync(StartIndicatorBackfillCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageDocumentTypes, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<IndicatorBackfillDto>(DocumentErrors.Forbidden(decision.Explanation));
        }

        var unnumbered = await documents.CountUnnumberedAsync(cancellationToken);
        if (unnumbered > 0)
        {
            await jobQueue.EnqueueAsync(
                IndicatorNumberingJob.Request(null, $"start:{timeProvider.GetUtcNow().ToUnixTimeMilliseconds()}"),
                cancellationToken);
            await audit.WriteAsync(
                new AuditRecord
                {
                    Action = AuditActions.IndicatorBackfillStarted,
                    EntityType = "Document",
                    Metadata = new Dictionary<string, object?> { ["documents"] = unnumbered },
                },
                cancellationToken);
        }

        return Result.Success(new IndicatorBackfillDto(unnumbered));
    }
}
