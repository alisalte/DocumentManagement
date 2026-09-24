using System.Text.Json;
using Dms.Application;
using Dms.Audit.Contracts;
using Dms.SharedKernel;
using Dms.Storage.Contracts;
using Dms.Storage.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dms.Storage.Application;

/// <summary>
/// Everything that happens to a file after it is attached to a document (section 8.1): the
/// malware scan, then page images for the viewer, then the listeners (Search extracts and
/// indexes). Queued in the transaction that commits the file, so no committed file is ever left
/// unprocessed; every step is idempotent, so retries and re-runs are safe.
/// </summary>
public sealed class ProcessObjectJob(
    IStorageObjectRepository objects,
    IRenditionRepository renditions,
    IFileStorage files,
    IMalwareScanner scanner,
    IEnumerable<IPageRenderer> renderers,
    IEnumerable<IObjectProcessedListener> listeners,
    IAuditWriter audit,
    IOptions<StorageOptions> options,
    TimeProvider timeProvider,
    ILogger<ProcessObjectJob> logger) : IJobHandler
{
    public const string Type = "storage.process-object";

    public string JobType => Type;

    public static JobRequest For(StorageObjectId id) =>
        new(Type, new { storageObjectId = id.Value }, IdempotencyKey: $"{Type}:{id.Value}");

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var id = JsonSerializer.Deserialize<Payload>(payload, JsonSerializerOptions.Web)?.StorageObjectId;
        var file = id is { } value ? await objects.FindAsync(new StorageObjectId(value), cancellationToken) : null;
        if (file is null || file.Status != StorageObjectStatus.Committed)
        {
            return;
        }

        if (!await ScanAsync(file, cancellationToken))
        {
            return;
        }

        var settings = options.Value.Renditions;
        if (settings.Enabled)
        {
            await RenderAsync(file, RenditionKind.Pages, new RenderRequest(settings.PageWidth, settings.MaxPages, file.OriginalFileName), cancellationToken);
            await RenderAsync(file, RenditionKind.Thumbnail, new RenderRequest(settings.ThumbnailWidth, 1, file.OriginalFileName), cancellationToken);
        }

        foreach (var listener in listeners)
        {
            await listener.OnProcessedAsync(file.ToInfo(), cancellationToken);
        }
    }

    /// <summary>Decision D9: nothing else happens to a file until it is known to be clean.</summary>
    private async Task<bool> ScanAsync(StorageObject file, CancellationToken cancellationToken)
    {
        if (file.ScanStatus is ScanStatus.Clean or ScanStatus.Skipped)
        {
            return true;
        }

        if (file.ScanStatus != ScanStatus.Pending)
        {
            return false;
        }

        if (!scanner.IsEnabled)
        {
            file.RecordScanResult(ScanStatus.Skipped, timeProvider.GetUtcNow());
            return true;
        }

        var verdict = await scanner.ScanAsync(new ObjectLocation(file.Bucket, file.ObjectKey), cancellationToken);
        if (verdict == ScanVerdict.Failed)
        {
            // Fail closed and retry with backoff: the file stays unavailable until a scan succeeds.
            throw new InvalidOperationException($"The malware scan of {file.Id} did not complete.");
        }

        var status = verdict == ScanVerdict.Clean ? ScanStatus.Clean : ScanStatus.Infected;
        file.RecordScanResult(status, timeProvider.GetUtcNow());

        if (status == ScanStatus.Infected)
        {
            logger.LogWarning("Storage object {ObjectId} was quarantined by the malware scanner.", file.Id);
            await audit.WriteAsync(
                new AuditRecord
                {
                    Action = AuditActions.FileInfected,
                    Outcome = AuditOutcome.Denied,
                    ActorType = AuditActorType.System,
                    EntityType = "StorageObject",
                    EntityId = file.Id.Value,
                    Metadata = new Dictionary<string, object?> { ["fileName"] = file.OriginalFileName, ["sha256"] = Convert.ToHexStringLower(file.Sha256) },
                },
                cancellationToken);
            return false;
        }

        return true;
    }

    private async Task RenderAsync(StorageObject file, RenditionKind kind, RenderRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var rendition = await renditions.FindAsync(file.Id, kind, cancellationToken);
        if (rendition is { Status: RenditionStatus.Ready or RenditionStatus.NotSupported })
        {
            return;
        }

        if (rendition is null)
        {
            rendition = Rendition.Start(file.Id, kind, now);
            renditions.Add(rendition);
        }
        else
        {
            rendition.Reset();
        }

        try
        {
            await using var content = await files.OpenAsync(new ObjectLocation(file.Bucket, file.ObjectKey), null, cancellationToken);

            // The first renderer that recognises the type wins.
            IAsyncEnumerable<RenderedPage>? pages = null;
            foreach (var renderer in renderers)
            {
                pages = renderer.Render(file.DetectedMimeType, content, request, cancellationToken);
                if (pages is not null)
                {
                    break;
                }
            }

            if (pages is null)
            {
                rendition.NotSupported(timeProvider.GetUtcNow());
                return;
            }

            await foreach (var page in pages.WithCancellation(cancellationToken))
            {
                var stored = await StoreAsync(file, kind, page, cancellationToken);
                rendition.AddPage(page.Number, stored);
            }

            if (rendition.Pages.Count == 0)
            {
                rendition.Fail("The file has no pages that could be rendered.", timeProvider.GetUtcNow());
                return;
            }

            rendition.Complete(timeProvider.GetUtcNow());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A broken preview never blocks search or download; it is recorded and can be retried.
            logger.LogWarning(exception, "Rendering {Kind} for {ObjectId} failed.", kind, file.Id);
            rendition.Fail(exception.Message, timeProvider.GetUtcNow());
        }
    }

    private async Task<StorageObjectId> StoreAsync(StorageObject source, RenditionKind kind, RenderedPage page, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var id = StorageObjectId.New();
        var location = new ObjectLocation(settings.Bucket, FileNames.BuildObjectKey("renditions", id.Value, now));

        await using (var bytes = new MemoryStream(page.Image, writable: false))
        {
            await files.PutAsync(location, bytes, "image/webp", cancellationToken);
        }

        var stored = StorageObject.Derived(
            id,
            files.Provider,
            location.Bucket,
            location.Key,
            StorageObjectPurpose.Rendition,
            $"{source.Id}-{kind.ToString().ToLowerInvariant()}-{page.Number}.webp",
            "image/webp",
            page.Image.LongLength,
            System.Security.Cryptography.SHA256.HashData(page.Image),
            now);

        objects.Add(stored);
        return stored.Id;
    }

    private sealed record Payload(Guid StorageObjectId);
}

public sealed class RenditionService(
    IStorageObjectRepository objects,
    IRenditionRepository renditions,
    IFileStorage files,
    IWatermarker watermarker,
    IJobQueue jobs) : IRenditionService
{
    public async Task<RenditionInfo> GetPreviewAsync(StorageObjectId sourceId, CancellationToken cancellationToken)
    {
        var rendition = await renditions.FindAsync(sourceId, RenditionKind.Pages, cancellationToken);
        return rendition is null
            ? new RenditionInfo(sourceId, RenditionStatus.Pending, 0, null)
            : new RenditionInfo(sourceId, rendition.Status, rendition.Pages.Count, rendition.Error);
    }

    public Task<Result<StoredContent>> OpenPageAsync(StorageObjectId sourceId, int page, string? watermark, CancellationToken cancellationToken) =>
        OpenAsync(sourceId, RenditionKind.Pages, page, watermark, cancellationToken);

    public Task<Result<StoredContent>> OpenThumbnailAsync(StorageObjectId sourceId, CancellationToken cancellationToken) =>
        OpenAsync(sourceId, RenditionKind.Thumbnail, 1, null, cancellationToken);

    public async Task RequestProcessingAsync(StorageObjectId sourceId, CancellationToken cancellationToken)
    {
        foreach (var kind in new[] { RenditionKind.Pages, RenditionKind.Thumbnail })
        {
            if (await renditions.FindAsync(sourceId, kind, cancellationToken) is { Status: RenditionStatus.Failed } failed)
            {
                failed.Reset();
            }
        }

        await jobs.EnqueueAsync(ProcessObjectJob.For(sourceId), cancellationToken);
    }

    private async Task<Result<StoredContent>> OpenAsync(
        StorageObjectId sourceId,
        RenditionKind kind,
        int page,
        string? watermark,
        CancellationToken cancellationToken)
    {
        var rendition = await renditions.FindAsync(sourceId, kind, cancellationToken);
        var entry = rendition is { Status: RenditionStatus.Ready }
            ? rendition.Pages.FirstOrDefault(candidate => candidate.PageNumber == page)
            : null;

        var stored = entry is null ? null : await objects.FindAsync(entry.ObjectId, cancellationToken);
        if (stored is null)
        {
            return Result.Failure<StoredContent>(Error.NotFound("rendition.not_found", "This page is not available."));
        }

        await using var source = await files.OpenAsync(new ObjectLocation(stored.Bucket, stored.ObjectKey), null, cancellationToken);
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);

        var image = watermark is null ? buffer.ToArray() : watermarker.Apply(buffer.ToArray(), watermark);
        return Result.Success(new StoredContent(new MemoryStream(image, writable: false), "image/webp", $"page-{page}.webp", image.LongLength));
    }
}
