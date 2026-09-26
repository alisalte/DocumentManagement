using System.Text.Json;
using Dms.Application;
using Dms.Authorization.Contracts;
using Dms.Documents.Application;
using Dms.SharedKernel;
using Dms.Storage.Contracts;
using Dms.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Dms.Documents.Infrastructure;

public static class DocumentEndpoints
{
    public const string IdempotencyHeader = "Idempotency-Key";

    public sealed record CreateDocumentRequest(
        string Title,
        string? Description,
        Guid CategoryId,
        Guid DocumentTypeId,
        Guid UploadId,
        IReadOnlyList<string>? Tags,
        string? ChangeDescription,
        JsonElement? Metadata);

    public sealed record AddVersionRequest(Guid UploadId, string? ChangeDescription, Guid? BaseVersionId, JsonElement? Metadata);

    public sealed record UpdateMetadataRequest(
        JsonElement Metadata,
        string? ChangeDescription,
        Guid? BaseVersionId,
        bool UpgradeSchema = false);

    public sealed record UpdateDocumentRequest(string Title, string? Description, Guid CategoryId);

    public sealed record SetTagsRequest(IReadOnlyList<string> Tags);

    public sealed record ReasonRequest(string? Reason);

    public sealed record CreateCategoryRequest(Guid? ParentId, string Name, string Code, string? Description);

    public sealed record UpdateCategoryRequest(string Name, string? Description, bool IsActive = true, int SortOrder = 0);

    public sealed record MoveCategoryRequest(Guid? NewParentId);

    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapUploads(endpoints);
        MapDocuments(endpoints);
        MapCategories(endpoints);
        return endpoints;
    }

    private static void MapUploads(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/uploads", UploadAsync)
            .WithTags("Documents")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data")
            .WithSummary("Stage a file. Attach it with POST /documents or POST /documents/{id}/versions.");
    }

    /// <summary>
    /// Section 7.3, step 1. The multipart body is read section by section and piped straight to
    /// storage while being hashed and sniffed: nothing is buffered in memory or in a temp file,
    /// and no database transaction is open while the bytes flow.
    /// </summary>
    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        IStorageService storage,
        IDispatcher dispatcher,
        IConfiguration configuration,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var maxBytes = configuration.GetValue<long?>("Dms:Storage:MaxUploadBytes") ?? 2L * 1024 * 1024 * 1024;

        // Kestrel's 30 MB default applies to every endpoint; this one needs the upload limit plus
        // room for the multipart framing. The hashing stream still enforces the exact limit.
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = maxBytes + (1024 * 1024);
        }

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value is not { Length: > 0 } boundary)
        {
            return ApiResults.Problem(Error.Validation("upload.not_multipart", "Send the file as multipart/form-data."));
        }

        var reader = new MultipartReader(boundary, request.Body) { BodyLengthLimit = maxBytes + 1 };
        while (await reader.ReadNextSectionAsync(ct) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                || !disposition.IsFileDisposition())
            {
                continue;
            }

            var fileName = disposition.FileNameStar.HasValue
                ? disposition.FileNameStar.Value
                : HeaderUtilities.RemoveQuotes(disposition.FileName).Value;

            var written = await storage.WriteAsync(section.Body, fileName ?? string.Empty, section.ContentType, ct);
            if (written.IsFailure)
            {
                return ApiResults.Problem(written.Error);
            }

            try
            {
                var registered = await dispatcher.SendAsync(new RegisterUploadCommand(written.Value), ct);
                if (registered.IsFailure)
                {
                    await storage.DiscardAsync(written.Value, CancellationToken.None);
                }

                return registered.ToHttpResult(upload => Results.Created($"/api/v1/uploads/{upload.UploadId}", upload));
            }
            catch (Exception exception)
            {
                // No row means the staged-upload collector would never find these bytes.
                loggers.CreateLogger(typeof(DocumentEndpoints)).LogError(
                    exception,
                    "Registering upload {UploadId} failed; removing its bytes.",
                    written.Value.Id);
                await storage.DiscardAsync(written.Value, CancellationToken.None);
                throw;
            }
        }

        return ApiResults.Problem(Error.Validation("upload.no_file", "The request contains no file."));
    }

    private static void MapDocuments(IEndpointRouteBuilder endpoints)
    {
        var documents = endpoints.MapGroup("/api/v1/documents")
            .WithTags("Documents")
            .RequireAuthorization();

        documents.MapGet("", async (
                Guid? categoryId,
                bool? includeSubcategories,
                string? search,
                Guid? tagId,
                int? page,
                int? pageSize,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var query = new ListDocumentsQuery(
                    categoryId,
                    includeSubcategories ?? false,
                    search,
                    tagId,
                    page,
                    pageSize);

                return (await dispatcher.QueryAsync(query, ct)).ToHttpResult();
            })
            .WithSummary("Documents the caller may see, newest first.");

        documents.MapPost("", async (
                CreateDocumentRequest request,
                [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var command = new CreateDocumentCommand(
                    request.Title,
                    request.Description,
                    request.CategoryId,
                    request.DocumentTypeId,
                    request.UploadId,
                    request.Tags,
                    request.ChangeDescription,
                    request.Metadata,
                    NormalizeKey(idempotencyKey));

                var result = await dispatcher.SendAsync(command, ct);
                return result.ToHttpResult(created =>
                    Results.Created($"/api/v1/documents/{created.DocumentId}", created));
            })
            .WithSummary("File a staged upload as a new document (V1.1).");

        documents.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new GetDocumentQuery(id), ct)).ToHttpResult())
            .WithSummary("Document details. 404 when the caller may not see it.");

        documents.MapPut("/{id:guid}", async (
                Guid id,
                UpdateDocumentRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var command = new UpdateDocumentCommand(id, request.Title, request.Description, request.CategoryId);
                return (await dispatcher.SendAsync(command, ct)).ToHttpResult();
            })
            .WithSummary("Change title, description or category. Not versioned: these describe the document, not a version.");

        documents.MapPut("/{id:guid}/tags", async (
                Guid id,
                SetTagsRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
                (await dispatcher.SendAsync(new SetDocumentTagsCommand(id, request.Tags), ct)).ToHttpResult())
            .WithSummary("Replace the document's tags.");

        documents.MapDelete("/{id:guid}", async (
                Guid id,
                string? reason,
                IDispatcher dispatcher,
                CancellationToken ct) =>
                (await dispatcher.SendAsync(new DeleteDocumentCommand(id, reason), ct)).ToHttpResult())
            .WithSummary("Move to the recycle bin. Nothing is removed from storage.");

        documents.MapPost("/{id:guid}/restore", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new RestoreDocumentCommand(id), ct)).ToHttpResult())
            .WithSummary("Bring a document back from the recycle bin.");

        documents.MapPost("/{id:guid}/purge", async (
                Guid id,
                ReasonRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
                (await dispatcher.SendAsync(new PurgeDocumentCommand(id, request.Reason ?? string.Empty), ct))
                    .ToHttpResult())
            .WithSummary("Permanently delete a document from the recycle bin. Requires DOCUMENT_PURGE.");

        documents.MapGet("/{id:guid}/versions", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new ListVersionsQuery(id), ct)).ToHttpResult())
            .WithSummary("Version history, newest first.");

        documents.MapGet("/{id:guid}/versions/{versionId:guid}", async (Guid id, Guid versionId, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new GetVersionQuery(id, versionId), ct)).ToHttpResult())
            .WithSummary("One version with its document's title, for readers of that version alone (a share recipient).");

        documents.MapPost("/{id:guid}/versions", async (
                Guid id,
                AddVersionRequest request,
                [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var command = new AddVersionCommand(
                    id,
                    request.UploadId,
                    request.ChangeDescription,
                    request.BaseVersionId,
                    request.Metadata,
                    NormalizeKey(idempotencyKey));

                var result = await dispatcher.SendAsync(command, ct);
                return result.ToHttpResult(created =>
                    Results.Created($"/api/v1/documents/{id}/versions/{created.VersionId}", created));
            })
            .WithSummary("Add a new file version. Send baseVersionId to get 409 if someone else was faster.");

        documents.MapPut("/{id:guid}/metadata", async (
                Guid id,
                UpdateMetadataRequest request,
                [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var command = new UpdateMetadataCommand(
                    id,
                    request.Metadata,
                    request.ChangeDescription,
                    request.BaseVersionId,
                    request.UpgradeSchema,
                    NormalizeKey(idempotencyKey));

                return (await dispatcher.SendAsync(command, ct)).ToHttpResult();
            })
            .WithSummary("Edit metadata without a new file: a new revision (V3.2), or in place for ungoverned types.");

        documents.MapGet("/{id:guid}/content", (Guid id, IDispatcher dispatcher, HttpContext http, CancellationToken ct) =>
                DownloadAsync(new OpenContentCommand(id, null), dispatcher, http, ct))
            .WithSummary("Download the effective version.");

        documents.MapGet("/{id:guid}/versions/{versionId:guid}/content", (
                Guid id,
                Guid versionId,
                IDispatcher dispatcher,
                HttpContext http,
                CancellationToken ct) =>
                DownloadAsync(new OpenContentCommand(id, versionId), dispatcher, http, ct))
            .WithSummary("Download one specific version.");

        documents.MapPost("/{id:guid}/preview", async (Guid id, Guid? versionId, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new OpenPreviewCommand(id, versionId), ct)).ToHttpResult())
            .WithSummary("Open the viewer on a version (the effective one by default): page count and status. Audited as DOCUMENT_VIEWED.");

        documents.MapGet("/{id:guid}/versions/{versionId:guid}/pages/{page:int}", (
                Guid id,
                Guid versionId,
                int page,
                IDispatcher dispatcher,
                HttpContext http,
                CancellationToken ct) =>
                PageAsync(new GetPreviewPageQuery(id, versionId, page, PagePurpose.View), dispatcher, http, ct))
            .WithSummary("One page of the preview as an image, watermarked. Never the original file.");

        documents.MapPost("/{id:guid}/versions/{versionId:guid}/print", async (Guid id, Guid versionId, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new StartPrintCommand(id, versionId), ct)).ToHttpResult())
            .WithSummary("Start printing a version (DOCUMENT_PRINT). Audited as DOCUMENT_PRINTED.");

        documents.MapGet("/{id:guid}/versions/{versionId:guid}/print/{page:int}", (
                Guid id,
                Guid versionId,
                int page,
                IDispatcher dispatcher,
                HttpContext http,
                CancellationToken ct) =>
                PageAsync(new GetPreviewPageQuery(id, versionId, page, PagePurpose.Print), dispatcher, http, ct))
            .WithSummary("One print page (DOCUMENT_PRINT), watermarked.");

        documents.MapGet("/{id:guid}/thumbnail", (Guid id, IDispatcher dispatcher, HttpContext http, CancellationToken ct) =>
                ImageAsync(dispatcher.QueryAsync(new GetThumbnailQuery(id), ct), http))
            .WithSummary("A small image of the first page of the effective version.");

        documents.MapPost("/{id:guid}/versions/{versionId:guid}/reprocess", async (Guid id, Guid versionId, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new RetryProcessingCommand(id, versionId), ct)).ToHttpResult(StatusCodes.Status202Accepted))
            .WithSummary("Run the scan, previews and text extraction of a version again (DOCUMENT_EDIT).");

        endpoints.MapGet("/api/v1/recycle-bin", async (int? page, int? pageSize, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new ListRecycleBinQuery(page, pageSize), ct)).ToHttpResult())
            .WithTags("Documents")
            .RequireAuthorization()
            .WithSummary("Deleted documents the caller may restore (or all of them, for DOCUMENT_PURGE holders).");

        endpoints.MapGet("/api/v1/tags", async (string? search, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new ListTagsQuery(search), ct)).ToHttpResult())
            .WithTags("Documents")
            .RequireAuthorization()
            .WithSummary("Tag suggestions.");
    }

    private static async Task<IResult> DownloadAsync(
        OpenContentCommand command,
        IDispatcher dispatcher,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await dispatcher.SendAsync(command, ct);
        if (result.IsFailure)
        {
            return ApiResults.Problem(result.Error);
        }

        var content = result.Value;

        // Always an attachment and never sniffed: an uploaded HTML/SVG file must not run on our origin.
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.CacheControl = "private, no-store";

        return Results.Stream(
            content.Content,
            DownloadContentType(content.MimeType),
            content.FileName,
            enableRangeProcessing: content.Content.CanSeek);
    }

    /// <summary>
    /// Phase 9: browser-executable types are remapped so a crafted upload cannot execute on our
    /// origin even if a client ignores Content-Disposition.
    /// </summary>
    internal static string DownloadContentType(string mimeType) =>
        mimeType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
        || mimeType.StartsWith("application/xhtml", StringComparison.OrdinalIgnoreCase)
        || mimeType.StartsWith("image/svg", StringComparison.OrdinalIgnoreCase)
        || mimeType.Equals("text/javascript", StringComparison.OrdinalIgnoreCase)
        || mimeType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
        || mimeType.Equals("text/xml", StringComparison.OrdinalIgnoreCase)
        || mimeType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            ? "application/octet-stream"
            : mimeType;

    private static Task<IResult> PageAsync(GetPreviewPageQuery query, IDispatcher dispatcher, HttpContext http, CancellationToken ct) =>
        ImageAsync(dispatcher.QueryAsync(query, ct), http);

    private static async Task<IResult> ImageAsync(Task<Result<StoredContent>> pending, HttpContext http)
    {
        var result = await pending;
        if (result.IsFailure)
        {
            return ApiResults.Problem(result.Error);
        }

        // Inline, but still never cached by shared caches: the watermark names the viewer.
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.CacheControl = "private, no-store";
        return Results.Stream(result.Value.Content, result.Value.MimeType);
    }

    private static void MapCategories(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/categories", async (IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new ListCategoriesQuery(), ct)).ToHttpResult())
            .WithTags("Categories")
            .RequireAuthorization()
            .WithSummary("The category tree as the caller may see it, with what they may do in each.");

        var admin = endpoints.MapGroup("/api/v1/admin/categories")
            .WithTags("Administration: categories")
            .RequireAuthorization()
            .RequireSystemPermission(PermissionCodes.AdminManageCategories);

        admin.MapPost("", async (CreateCategoryRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var command = new CreateCategoryCommand(request.ParentId, request.Name, request.Code, request.Description);
            var result = await dispatcher.SendAsync(command, ct);
            return result.ToHttpResult(id => Results.Created($"/api/v1/admin/categories/{id}", new { id }));
        });

        admin.MapPut("/{id:guid}", async (Guid id, UpdateCategoryRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var command = new UpdateCategoryCommand(id, request.Name, request.Description, request.IsActive, request.SortOrder);
            return (await dispatcher.SendAsync(command, ct)).ToHttpResult();
        });

        admin.MapPost("/{id:guid}/move", async (Guid id, MoveCategoryRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.SendAsync(new MoveCategoryCommand(id, request.NewParentId), ct)).ToHttpResult());
    }

    private static string? NormalizeKey(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : key.Trim()[..Math.Min(key.Trim().Length, 200)];
}
