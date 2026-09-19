using System.Text.Json;
using Dms.Application;
using Dms.Storage.Contracts;
using Dms.Storage.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dms.Storage.Application;

/// <summary>Runs the malware scan and records the verdict (decision D9).</summary>
public sealed class ScanStorageObjectJob(
    IStorageObjectRepository repository,
    IMalwareScanner scanner,
    TimeProvider timeProvider,
    ILogger<ScanStorageObjectJob> logger) : IJobHandler
{
    public const string Type = "storage.scan-object";

    public string JobType => Type;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var id = JsonSerializer.Deserialize<ScanPayload>(payload)?.StorageObjectId;
        if (id is null)
        {
            return;
        }

        var storageObject = await repository.FindAsync(new StorageObjectId(id.Value), cancellationToken);
        if (storageObject is null)
        {
            return;
        }

        var verdict = await scanner.ScanAsync(
            new ObjectLocation(storageObject.Bucket, storageObject.ObjectKey),
            cancellationToken);

        var status = verdict switch
        {
            ScanVerdict.Clean => ScanStatus.Clean,
            ScanVerdict.Infected => ScanStatus.Infected,
            _ => ScanStatus.Failed,
        };

        storageObject.RecordScanResult(status, timeProvider.GetUtcNow());

        if (status == ScanStatus.Infected)
        {
            logger.LogWarning("Storage object {ObjectId} was quarantined by the malware scanner.", storageObject.Id);
        }
    }

    private sealed record ScanPayload(Guid StorageObjectId);
}

/// <summary>
/// Deletes the bytes of objects marked for deletion. Deletion is deliberately delayed and
/// asynchronous: an accidental purge should be recoverable from backups, and the row survives as a
/// tombstone so the hash stays auditable.
/// </summary>
public sealed class PurgeStorageObjectsJob(
    IStorageObjectRepository repository,
    IFileStorage fileStorage,
    TimeProvider timeProvider,
    ILogger<PurgeStorageObjectsJob> logger) : IJobHandler
{
    public const string Type = "storage.purge-objects";

    private const int BatchSize = 100;

    public string JobType => Type;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var pending = await repository.ListPendingDeletionAsync(BatchSize, cancellationToken);
        foreach (var storageObject in pending)
        {
            await fileStorage.DeleteAsync(
                new ObjectLocation(storageObject.Bucket, storageObject.ObjectKey),
                cancellationToken);

            storageObject.MarkDeleted(timeProvider.GetUtcNow());
            logger.LogInformation("Purged storage object {ObjectId}.", storageObject.Id);
        }
    }
}

/// <summary>
/// Removes uploads that were never attached to a document, for example an upload the user
/// abandoned halfway through the form.
/// </summary>
public sealed class CollectStagedUploadsJob(
    IStorageObjectRepository repository,
    IFileStorage fileStorage,
    IOptions<StorageOptions> options,
    TimeProvider timeProvider,
    ILogger<CollectStagedUploadsJob> logger) : IJobHandler
{
    public const string Type = "storage.collect-staged";

    private const int BatchSize = 200;

    public string JobType => Type;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - options.Value.StagingRetention;
        var abandoned = await repository.ListStagedBeforeAsync(cutoff, BatchSize, cancellationToken);

        foreach (var storageObject in abandoned)
        {
            await fileStorage.DeleteAsync(
                new ObjectLocation(storageObject.Bucket, storageObject.ObjectKey),
                cancellationToken);

            storageObject.MarkDeleted(timeProvider.GetUtcNow());
        }

        if (abandoned.Count > 0)
        {
            logger.LogInformation("Collected {Count} abandoned uploads.", abandoned.Count);
        }
    }
}
