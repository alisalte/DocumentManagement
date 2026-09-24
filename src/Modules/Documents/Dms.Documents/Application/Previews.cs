using System.Globalization;
using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Documents.Contracts;
using Dms.Identity.Contracts;
using Dms.SharedKernel;
using Dms.Storage.Contracts;
using Microsoft.Extensions.Options;

namespace Dms.Documents.Application;

public sealed class PreviewOptions
{
    public const string SectionName = "Dms:Preview";

    /// <summary>Stamp every preview and print page with the viewer and the time.</summary>
    public bool Watermark { get; set; } = true;
}

public enum PagePurpose
{
    View,
    Print,
}

/// <summary>Opens the viewer on one version: DOCUMENT_VIEWED is recorded here, once, not per page.</summary>
public sealed record OpenPreviewCommand(Guid DocumentId, Guid? VersionId) : ICommand<Result<PreviewDto>>;

/// <summary>Records DOCUMENT_PRINTED before the print pages are fetched.</summary>
public sealed record StartPrintCommand(Guid DocumentId, Guid VersionId) : ICommand<Result<PreviewDto>>;

public sealed record GetPreviewPageQuery(Guid DocumentId, Guid VersionId, int Page, PagePurpose Purpose) : IQuery<Result<StoredContent>>;

public sealed record GetThumbnailQuery(Guid DocumentId) : IQuery<Result<StoredContent>>;

/// <summary>Runs scan, previews and text extraction again for a version whose processing failed.</summary>
public sealed record RetryProcessingCommand(Guid DocumentId, Guid VersionId) : ICommand<Result>;

public sealed record PreviewDto(
    Guid VersionId,
    string Label,
    RenditionStatus Status,
    int PageCount,
    string? Error,
    bool CanPrint,
    bool CanDownload);

/// <summary>
/// Preview and print (section 7.4). Both serve page images, never the original file: VIEW and
/// PRINT must not quietly amount to DOWNLOAD. The version gates of the evaluator (drafts,
/// decision D6; the malware scan, decision D9) apply exactly as they do to a download.
/// </summary>
public sealed class PreviewService(
    DocumentAccess access,
    IDocumentRepository documents,
    IRenditionService renditions,
    IUserDirectory users,
    ICurrentUser currentUser,
    IAuditWriter audit,
    IOptions<PreviewOptions> options,
    TimeProvider timeProvider)
{
    public async Task<Result<(DocumentVersionView Version, PreviewDto Preview)>> OpenAsync(
        Guid documentId,
        Guid? versionId,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        var visible = await access.RequireAsync(documentId, PermissionCodes.DocumentView, cancellationToken);
        if (visible.IsFailure)
        {
            return Result.Failure<(DocumentVersionView, PreviewDto)>(visible.Error);
        }

        var version = await ResolveAsync(documentId, versionId, cancellationToken);
        if (version is null)
        {
            return Result.Failure<(DocumentVersionView, PreviewDto)>(DocumentErrors.VersionNotFound);
        }

        var allowed = await access.RequireAsync(documentId, permissionCode, version.Id, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<(DocumentVersionView, PreviewDto)>(allowed.Error);
        }

        var info = await renditions.GetPreviewAsync(version.StorageObjectId, cancellationToken);
        var canPrint = await access.IsVersionAllowedAsync(documentId, PermissionCodes.DocumentPrint, version.Id, cancellationToken);
        var canDownload = await access.IsVersionAllowedAsync(documentId, PermissionCodes.DocumentDownload, version.Id, cancellationToken);

        return Result.Success((version, new PreviewDto(version.Id, version.Label, info.Status, info.PageCount, info.Error, canPrint, canDownload)));
    }

    public async Task<Result<StoredContent>> OpenPageAsync(GetPreviewPageQuery query, CancellationToken cancellationToken)
    {
        var permission = query.Purpose == PagePurpose.Print ? PermissionCodes.DocumentPrint : PermissionCodes.DocumentView;
        var visible = await access.RequireAsync(query.DocumentId, PermissionCodes.DocumentView, cancellationToken);
        if (visible.IsFailure)
        {
            return Result.Failure<StoredContent>(visible.Error);
        }

        var version = await ResolveAsync(query.DocumentId, query.VersionId, cancellationToken);
        if (version is null)
        {
            return Result.Failure<StoredContent>(DocumentErrors.VersionNotFound);
        }

        var allowed = await access.RequireAsync(query.DocumentId, permission, version.Id, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<StoredContent>(allowed.Error);
        }

        return await renditions.OpenPageAsync(version.StorageObjectId, query.Page, await WatermarkAsync(cancellationToken), cancellationToken);
    }

    public async Task<Result<StoredContent>> OpenThumbnailAsync(Guid documentId, CancellationToken cancellationToken)
    {
        // Thumbnails show in lists, so they follow the version a plain reader gets.
        var opened = await OpenAsync(documentId, null, PermissionCodes.DocumentView, cancellationToken);
        return opened.IsFailure
            ? Result.Failure<StoredContent>(opened.Error)
            : await renditions.OpenThumbnailAsync(opened.Value.Version.StorageObjectId, cancellationToken);
    }

    public Task WriteAuditAsync(string action, Guid documentId, DocumentVersionView version, int pageCount, CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new AuditRecord
            {
                Action = action,
                EntityType = "DocumentVersion",
                EntityId = version.Id,
                DocumentId = documentId,
                VersionId = version.Id,
                Metadata = new Dictionary<string, object?>
                {
                    ["label"] = version.Label,
                    ["pages"] = pageCount,
                },
            },
            cancellationToken);

    public Task RetryAsync(DocumentVersionView version, CancellationToken cancellationToken) =>
        renditions.RequestProcessingAsync(version.StorageObjectId, cancellationToken);

    /// <summary>Plain readers get the effective (published) version, decision D7.</summary>
    private async Task<DocumentVersionView?> ResolveAsync(Guid documentId, Guid? versionId, CancellationToken cancellationToken)
    {
        var document = await documents.FindAsync(new DocumentId(documentId), cancellationToken);
        if (document is null)
        {
            return null;
        }

        var resolved = versionId ?? document.EffectiveVersionId?.Value ?? document.CurrentVersionId?.Value;
        if (resolved is not { } id)
        {
            return null;
        }

        var version = await documents.FindVersionAsync(new DocumentVersionId(id), cancellationToken);
        return version is null || version.DocumentId != document.Id
            ? null
            : new DocumentVersionView(version.Id.Value, version.Label, version.StorageObjectId);
    }

    private async Task<string?> WatermarkAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Watermark || currentUser.UserId is not { } viewer)
        {
            return null;
        }

        var user = await users.FindAsync(viewer, cancellationToken);
        var stamp = timeProvider.GetUtcNow().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        return $"{user?.Username ?? viewer.Value.ToString()} · {stamp}";
    }
}

public sealed record DocumentVersionView(Guid Id, string Label, StorageObjectId StorageObjectId);

public sealed class OpenPreviewHandler(PreviewService previews) : ICommandHandler<OpenPreviewCommand, Result<PreviewDto>>
{
    public async Task<Result<PreviewDto>> HandleAsync(OpenPreviewCommand command, CancellationToken cancellationToken)
    {
        var opened = await previews.OpenAsync(command.DocumentId, command.VersionId, PermissionCodes.DocumentView, cancellationToken);
        if (opened.IsFailure)
        {
            return Result.Failure<PreviewDto>(opened.Error);
        }

        var (version, preview) = opened.Value;
        await previews.WriteAuditAsync(AuditActions.DocumentViewed, command.DocumentId, version, preview.PageCount, cancellationToken);
        return Result.Success(preview);
    }
}

public sealed class StartPrintHandler(PreviewService previews) : ICommandHandler<StartPrintCommand, Result<PreviewDto>>
{
    public async Task<Result<PreviewDto>> HandleAsync(StartPrintCommand command, CancellationToken cancellationToken)
    {
        var opened = await previews.OpenAsync(command.DocumentId, command.VersionId, PermissionCodes.DocumentPrint, cancellationToken);
        if (opened.IsFailure)
        {
            return Result.Failure<PreviewDto>(opened.Error);
        }

        var (version, preview) = opened.Value;
        if (preview.Status != RenditionStatus.Ready)
        {
            return Result.Failure<PreviewDto>(Error.Conflict("preview.not_ready", "The print pages of this version are not ready."));
        }

        await previews.WriteAuditAsync(AuditActions.DocumentPrinted, command.DocumentId, version, preview.PageCount, cancellationToken);
        return Result.Success(preview);
    }
}

public sealed class GetPreviewPageHandler(PreviewService previews) : IQueryHandler<GetPreviewPageQuery, Result<StoredContent>>
{
    public Task<Result<StoredContent>> HandleAsync(GetPreviewPageQuery query, CancellationToken cancellationToken) =>
        previews.OpenPageAsync(query, cancellationToken);
}

public sealed class GetThumbnailHandler(PreviewService previews) : IQueryHandler<GetThumbnailQuery, Result<StoredContent>>
{
    public Task<Result<StoredContent>> HandleAsync(GetThumbnailQuery query, CancellationToken cancellationToken) =>
        previews.OpenThumbnailAsync(query.DocumentId, cancellationToken);
}

public sealed class RetryProcessingHandler(PreviewService previews) : ICommandHandler<RetryProcessingCommand, Result>
{
    public async Task<Result> HandleAsync(RetryProcessingCommand command, CancellationToken cancellationToken)
    {
        // Whoever may change the document may ask for its processing again; nothing is exposed.
        var opened = await previews.OpenAsync(command.DocumentId, command.VersionId, PermissionCodes.DocumentEdit, cancellationToken);
        if (opened.IsFailure)
        {
            return Result.Failure(opened.Error);
        }

        await previews.RetryAsync(opened.Value.Version, cancellationToken);
        return Result.Success();
    }
}
