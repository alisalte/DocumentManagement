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

        var info = await catalog.GetIndicatorPatternAsync(document.DocumentTypeId, cancellationToken);
        if (info is null)
        {
            return Result.Failure<string>(Error.NotFound("document_type.not_found", "The document type does not exist."));
        }

        var pattern = IndicatorPattern.Parse(info.Pattern);
        if (pattern.IsFailure)
        {
            return Result.Failure<string>(pattern.Error);
        }

        var categoryCode = pattern.Value!.Text.Contains("{CAT}", StringComparison.Ordinal)
            ? (await categories.FindAsync(document.CategoryId, cancellationToken))?.Code
            : null;

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
