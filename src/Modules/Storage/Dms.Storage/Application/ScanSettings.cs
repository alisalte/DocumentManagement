using System.Text.Json;
using Dms.Application;
using Dms.Storage.Contracts;
using Dms.Storage.Domain;
using Microsoft.Extensions.Logging;

namespace Dms.Storage.Application;

/// <summary>
/// Whether new files are scanned for malware: ClamAV must be configured for the deployment, and an
/// administrator may switch it off from the application (for example while a large old archive is
/// imported, or when no scanner is running). Decision D9 still holds while it is on.
/// </summary>
public sealed class ScanPolicy(IMalwareScanner scanner, IStorageSettingRepository settings)
{
    public const string Off = "off";
    public const string On = "on";

    public async Task<bool> IsScanningOnAsync(CancellationToken cancellationToken)
    {
        if (!scanner.IsEnabled)
        {
            return false;
        }

        var setting = await settings.FindAsync(StorageSetting.MalwareScanning, cancellationToken);
        return setting is null || !string.Equals(setting.Value, Off, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class FileProcessingAdmin(
    IStorageObjectRepository objects,
    IStorageSettingRepository settings,
    IMalwareScanner scanner,
    ScanPolicy policy,
    IJobQueue jobs,
    TimeProvider timeProvider) : IFileProcessingAdmin
{
    public async Task<MalwareScanState> GetScanStateAsync(CancellationToken cancellationToken) =>
        new(
            scanner.IsEnabled,
            await policy.IsScanningOnAsync(cancellationToken),
            await objects.CountWaitingForScanAsync(cancellationToken));

    public async Task SetScanningAsync(bool enabled, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var value = enabled ? ScanPolicy.On : ScanPolicy.Off;
        var setting = await settings.FindAsync(StorageSetting.MalwareScanning, cancellationToken);
        if (setting is null)
        {
            settings.Add(new StorageSetting(StorageSetting.MalwareScanning, value, now));
        }
        else
        {
            setting.Change(value, now);
        }

        if (!enabled)
        {
            await jobs.EnqueueAsync(ReleaseScanWaitingJob.Start(), cancellationToken);
        }
    }

    public Task QueueReprocessAllAsync(CancellationToken cancellationToken) =>
        jobs.EnqueueAsync(ReprocessAllObjectsJob.Start(), cancellationToken);
}

/// <summary>
/// With the scan switched off, hands every file still waiting for a scan to processing as
/// "skipped": it can then be opened and downloaded, and gets its pages and OCR text. Works in
/// batches and queues itself again until nothing is left.
/// </summary>
public sealed class ReleaseScanWaitingJob(
    IStorageObjectRepository objects,
    ScanPolicy policy,
    IJobQueue jobs,
    TimeProvider timeProvider,
    ILogger<ReleaseScanWaitingJob> logger) : IJobHandler
{
    public const string Type = "storage.release-scan-waiting";

    private const int BatchSize = 500;

    public string JobType => Type;

    public static JobRequest Start() => new(Type, IdempotencyKey: Type);

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        // Switched back on in the meantime: the files wait for the scanner again.
        if (await policy.IsScanningOnAsync(cancellationToken))
        {
            return;
        }

        var waiting = await objects.ListWaitingForScanAsync(BatchSize, cancellationToken);
        var now = timeProvider.GetUtcNow();
        foreach (var file in waiting)
        {
            file.RecordScanResult(ScanStatus.Skipped, now);
            await jobs.EnqueueAsync(ProcessObjectJob.For(file.Id), cancellationToken);
        }

        if (waiting.Count > 0)
        {
            logger.LogInformation("Released {Count} files that were waiting for the malware scan.", waiting.Count);
        }

        // The current job holds the idempotency key while it runs, so the next batch goes without one.
        if (waiting.Count == BatchSize)
        {
            await jobs.EnqueueAsync(new JobRequest(Type), cancellationToken);
        }
    }
}

/// <summary>
/// Queues processing (pages, then text extraction and OCR) again for every attached file whose
/// content may be served, in batches. Each step skips what is already done, so files that have
/// their pages and text cost little; text corrected by hand is never replaced.
/// </summary>
public sealed class ReprocessAllObjectsJob(
    IStorageObjectRepository objects,
    IRenditionService renditions,
    IJobQueue jobs,
    ILogger<ReprocessAllObjectsJob> logger) : IJobHandler
{
    public const string Type = "storage.reprocess-all";

    private const int BatchSize = 200;

    public string JobType => Type;

    public static JobRequest Start() => new(Type, IdempotencyKey: Type);

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var after = JsonSerializer.Deserialize<Payload>(payload, JsonSerializerOptions.Web)?.After;
        var ids = await objects.ListAvailableOriginalsAsync(
            after is { } cursor ? new StorageObjectId(cursor) : null,
            BatchSize,
            cancellationToken);

        foreach (var id in ids)
        {
            await renditions.RequestProcessingAsync(id, cancellationToken);
        }

        logger.LogInformation("Queued {Count} files for processing again.", ids.Count);

        if (ids.Count == BatchSize)
        {
            await jobs.EnqueueAsync(new JobRequest(Type, new { after = ids[^1].Value }), cancellationToken);
        }
    }

    private sealed record Payload(Guid? After);
}
