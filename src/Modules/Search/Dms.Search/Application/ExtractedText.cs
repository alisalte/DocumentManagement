using System.IO.Compression;
using System.Text;
using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Documents.Contracts;
using Dms.Search.Domain;
using Dms.SharedKernel;
using Dms.Storage.Contracts;
using Microsoft.Extensions.Options;

namespace Dms.Search.Application;

/// <summary>The text read from one stored file, for checking and correcting OCR.</summary>
public sealed record ExtractedTextDto(
    Guid StorageObjectId,
    Guid DocumentId,
    string Title,
    string? FileName,
    string Status,
    string Method,
    int CharCount,
    string? Engine,
    string Text,
    bool CanEdit,
    Guid? EditedBy,
    DateTimeOffset? EditedAt);

public sealed record GetExtractedTextQuery(Guid StorageObjectId) : IQuery<Result<ExtractedTextDto>>;

public sealed record UpdateExtractedTextCommand(Guid StorageObjectId, string Text) : ICommand<Result<ExtractedTextDto>>;

/// <summary>
/// The file text is document content, so ADMIN_MANAGE_SEARCH alone does not open it (decision D5):
/// reading needs DOCUMENT_VIEW on a version that uses the file (draft and malware gates included),
/// and correcting it needs DOCUMENT_EDIT on that document as well.
/// </summary>
public sealed class ExtractedTextAccess(IDmsAuthorizer authorizer, IDocumentIndexSource documents)
{
    public static readonly Error NotFound = Error.NotFound("search.text_not_found", "No extracted text exists for this file.");

    public static readonly Error NoContentAccess = Error.Forbidden(
        "search.text_no_access",
        "The file text is document content: it needs VIEW on a document that uses this file.");

    public static readonly Error NoEditAccess = Error.Forbidden(
        "search.text_no_edit",
        "Correcting the file text needs EDIT on a document that uses this file.");

    /// <summary>The first live document (then a deleted one) through which the caller may read the file.</summary>
    public async Task<(DocumentIndexData Document, VersionIndexData Version, bool CanEdit)?> ResolveAsync(
        Guid storageObjectId,
        CancellationToken cancellationToken)
    {
        var candidates = new List<DocumentIndexData>();
        foreach (var documentId in await documents.DocumentsUsingObjectAsync(storageObjectId, cancellationToken))
        {
            if (await documents.GetAsync(documentId, cancellationToken) is { } document)
            {
                candidates.Add(document);
            }
        }

        foreach (var document in candidates.OrderBy(document => document.IsDeleted))
        {
            var resource = ResourceRef.Document(document.DocumentId);
            var versions = document.Versions
                .Where(version => version.StorageObjectId == storageObjectId)
                .OrderByDescending(version => version.VersionId == document.CurrentVersionId)
                .ThenByDescending(version => version.VersionNumber)
                .ThenByDescending(version => version.RevisionNumber);

            foreach (var version in versions)
            {
                var view = await authorizer.AuthorizeVersionAsync(PermissionCodes.DocumentView, resource, version.VersionId, cancellationToken);
                if (!view.Allowed)
                {
                    continue;
                }

                var edit = await authorizer.AuthorizeAsync(PermissionCodes.DocumentEdit, resource, cancellationToken);
                return (document, version, edit.Allowed);
            }
        }

        return null;
    }
}

public sealed class GetExtractedTextHandler(
    IDmsAuthorizer authorizer,
    ExtractedTextAccess access,
    IContentExtractionRepository extractions,
    IStorageService storage,
    IAuditWriter audit,
    IUnitOfWork unitOfWork) : IQueryHandler<GetExtractedTextQuery, Result<ExtractedTextDto>>
{
    public async Task<Result<ExtractedTextDto>> HandleAsync(GetExtractedTextQuery query, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageSearch, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<ExtractedTextDto>(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var extraction = await extractions.FindAsync(query.StorageObjectId, cancellationToken);
        if (extraction is null)
        {
            return Result.Failure<ExtractedTextDto>(ExtractedTextAccess.NotFound);
        }

        if (await access.ResolveAsync(query.StorageObjectId, cancellationToken) is not { } granted)
        {
            return Result.Failure<ExtractedTextDto>(ExtractedTextAccess.NoContentAccess);
        }

        var text = extraction.TextObjectId is { } textId ? await ExtractedTextStore.ReadAsync(storage, textId, cancellationToken) : string.Empty;

        // Queries run outside a transaction, so reading content is audited in one of its own.
        await unitOfWork.BeginAsync(cancellationToken);
        try
        {
            await audit.WriteAsync(ExtractedTextStore.AuditOf(AuditActions.ExtractedTextViewed, granted.Document, granted.Version, extraction), cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }

        return Result.Success(ExtractedTextStore.ToDto(extraction, granted.Document, granted.Version, granted.CanEdit, text));
    }
}

public sealed class UpdateExtractedTextHandler(
    IDmsAuthorizer authorizer,
    ExtractedTextAccess access,
    IContentExtractionRepository extractions,
    IDocumentIndexSource documents,
    IStorageService storage,
    IJobQueue jobs,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IOptions<SearchOptions> options,
    TimeProvider timeProvider) : ICommandHandler<UpdateExtractedTextCommand, Result<ExtractedTextDto>>
{
    public async Task<Result<ExtractedTextDto>> HandleAsync(UpdateExtractedTextCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageSearch, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<ExtractedTextDto>(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var text = (command.Text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (text.Length > options.Value.MaxTextChars)
        {
            return Result.Failure<ExtractedTextDto>(Error.Validation(
                "search.text_too_long",
                $"The text may have at most {options.Value.MaxTextChars} characters."));
        }

        var extraction = await extractions.FindAsync(command.StorageObjectId, cancellationToken);
        if (extraction is null)
        {
            return Result.Failure<ExtractedTextDto>(ExtractedTextAccess.NotFound);
        }

        if (await access.ResolveAsync(command.StorageObjectId, cancellationToken) is not { } granted)
        {
            return Result.Failure<ExtractedTextDto>(ExtractedTextAccess.NoContentAccess);
        }

        if (!granted.CanEdit)
        {
            return Result.Failure<ExtractedTextDto>(ExtractedTextAccess.NoEditAccess);
        }

        var previousChars = extraction.CharCount;
        var previousObject = extraction.TextObjectId;
        var source = new StorageObjectId(extraction.StorageObjectId);

        Guid? textObject = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            textObject = await ExtractedTextStore.WriteAsync(storage, source, text, cancellationToken);
        }
        else
        {
            text = string.Empty;
        }

        extraction.ReplaceText(textObject, text.Length, currentUser.UserId!.Value.Value, timeProvider.GetUtcNow());
        if (previousObject is { } old)
        {
            await storage.MarkForDeletionAsync(new StorageObjectId(old), cancellationToken);
        }

        await audit.WriteAsync(
            ExtractedTextStore.AuditOf(AuditActions.ExtractedTextUpdated, granted.Document, granted.Version, extraction, previousChars),
            cancellationToken);

        // Every document on this file searches the corrected text.
        foreach (var documentId in await documents.DocumentsUsingObjectAsync(extraction.StorageObjectId, cancellationToken))
        {
            await jobs.EnqueueAsync(IndexDocumentJob.For(documentId), cancellationToken);
        }

        return Result.Success(ExtractedTextStore.ToDto(extraction, granted.Document, granted.Version, granted.CanEdit, text));
    }
}

/// <summary>Extracted text lives in storage as gzip (section 8.4); the same format is written back.</summary>
internal static class ExtractedTextStore
{
    public static async Task<string> ReadAsync(IStorageService storage, Guid textObjectId, CancellationToken cancellationToken)
    {
        await using var stored = await storage.OpenForProcessingAsync(new StorageObjectId(textObjectId), cancellationToken);
        await using var gzip = new GZipStream(stored, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public static async Task<Guid> WriteAsync(IStorageService storage, StorageObjectId source, string text, CancellationToken cancellationToken)
    {
        using var packed = new MemoryStream();
        await using (var gzip = new GZipStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            await gzip.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
        }

        packed.Position = 0;
        return (await storage.StoreDerivedAsync(source, packed, $"{source.Value}.txt.gz", "application/gzip", DerivedPurpose.ExtractedText, cancellationToken)).Value;
    }

    public static ExtractedTextDto ToDto(
        ContentExtraction extraction,
        DocumentIndexData document,
        VersionIndexData version,
        bool canEdit,
        string text) => new(
            extraction.StorageObjectId,
            document.DocumentId,
            document.Title,
            version.FileName,
            extraction.Status.ToString(),
            extraction.Method.ToString(),
            extraction.CharCount,
            extraction.Engine,
            text,
            canEdit,
            extraction.EditedBy,
            extraction.EditedAt);

    public static AuditRecord AuditOf(
        string action,
        DocumentIndexData document,
        VersionIndexData version,
        ContentExtraction extraction,
        int? previousChars = null) => new()
        {
            Action = action,
            EntityType = "ContentExtraction",
            EntityId = extraction.Id,
            DocumentId = document.DocumentId,
            VersionId = version.VersionId,
            Metadata = new Dictionary<string, object?>
            {
                ["storageObjectId"] = extraction.StorageObjectId,
                ["method"] = extraction.Method.ToString(),
                ["charCount"] = extraction.CharCount,
                ["previousCharCount"] = previousChars,
            },
        };
}
