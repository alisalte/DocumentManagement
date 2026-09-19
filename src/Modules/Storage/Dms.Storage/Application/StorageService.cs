using Dms.Application;
using Dms.SharedKernel;
using Dms.Storage.Contracts;
using Dms.Storage.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dms.Storage.Application;

public sealed class StorageService(
    IFileStorage fileStorage,
    IStorageObjectRepository repository,
    IMalwareScanner scanner,
    IJobQueue jobs,
    ICurrentUser currentUser,
    IOptions<StorageOptions> options,
    TimeProvider timeProvider,
    ILogger<StorageService> logger) : IStorageService
{
    public async Task<Result<StagedUpload>> StageAsync(
        Stream content,
        string fileName,
        string? declaredMimeType,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var id = StorageObjectId.New();
        var safeName = FileNames.Sanitize(fileName);
        var location = new ObjectLocation(settings.Bucket, FileNames.BuildObjectKey("objects", id.Value, now));

        await using var measured = new HashingReadStream(content, settings.MaxUploadBytes);
        try
        {
            // The content type is only settled after the bytes have flowed, so the object is
            // stored as opaque and the detected type lives in the database.
            await fileStorage.PutAsync(location, measured, "application/octet-stream", cancellationToken);
        }
        catch (UploadTooLargeException tooLarge)
        {
            await TryDeleteAsync(location, cancellationToken);
            return Result.Failure<StagedUpload>(Error.Validation(
                "upload.too_large",
                $"The file exceeds the maximum upload size of {tooLarge.MaxBytes} bytes."));
        }
        catch (Exception exception)
        {
            await TryDeleteAsync(location, cancellationToken);
            logger.LogError(exception, "Upload of {FileName} failed while streaming to storage.", safeName);
            throw;
        }

        if (measured.BytesRead == 0)
        {
            await TryDeleteAsync(location, cancellationToken);
            return Result.Failure<StagedUpload>(Error.Validation("upload.empty", "The file is empty."));
        }

        var detected = MimeSniffer.Detect(measured.Sample, safeName, declaredMimeType);
        var scanStatus = scanner.IsEnabled ? ScanStatus.Pending : ScanStatus.Skipped;

        var storageObject = StorageObject.Stage(
            fileStorage.Provider,
            location.Bucket,
            location.Key,
            StorageObjectPurpose.Original,
            safeName,
            detected,
            declaredMimeType,
            measured.BytesRead,
            measured.Digest,
            scanStatus,
            currentUser.UserId,
            now);

        repository.Add(storageObject);

        if (scanner.IsEnabled)
        {
            // Queued in the caller's transaction: a stored object always gets its scan.
            await jobs.EnqueueAsync(
                new JobRequest(ScanStorageObjectJob.Type, new { storageObjectId = storageObject.Id.Value }),
                cancellationToken);
        }

        return Result.Success(new StagedUpload(
            storageObject.Id,
            safeName,
            detected,
            measured.BytesRead,
            Convert.ToHexStringLower(measured.Digest)));
    }

    public async Task<Result> CommitAsync(StorageObjectId id, CancellationToken cancellationToken)
    {
        var storageObject = await repository.FindAsync(id, cancellationToken);
        if (storageObject is null)
        {
            return Result.Failure(Error.NotFound("storage.not_found", "The uploaded file no longer exists."));
        }

        return storageObject.Commit(timeProvider.GetUtcNow());
    }

    public async Task<StorageObjectInfo?> FindAsync(StorageObjectId id, CancellationToken cancellationToken) =>
        (await repository.FindAsync(id, cancellationToken))?.ToInfo();

    public async Task<Result<StoredContent>> OpenAsync(
        StorageObjectId id,
        ByteRange? range,
        CancellationToken cancellationToken)
    {
        var storageObject = await repository.FindAsync(id, cancellationToken);
        if (storageObject is null || storageObject.Status is StorageObjectStatus.Deleted)
        {
            return Result.Failure<StoredContent>(Error.NotFound("storage.not_found", "The file no longer exists."));
        }

        // Decision D9. Authorization has already run; this is the content gate, which applies to
        // everyone including the uploader.
        if (storageObject.ScanStatus is not (ScanStatus.Clean or ScanStatus.Skipped))
        {
            return Result.Failure<StoredContent>(Error.Forbidden(
                "storage.scan_incomplete",
                storageObject.ScanStatus == ScanStatus.Infected
                    ? "The file was quarantined by the malware scanner."
                    : "The malware scan has not finished yet."));
        }

        var stream = await fileStorage.OpenAsync(
            new ObjectLocation(storageObject.Bucket, storageObject.ObjectKey),
            range,
            cancellationToken);

        return Result.Success(new StoredContent(
            stream,
            storageObject.DetectedMimeType,
            storageObject.OriginalFileName,
            storageObject.Size));
    }

    public async Task<Result> MarkForDeletionAsync(StorageObjectId id, CancellationToken cancellationToken)
    {
        var storageObject = await repository.FindAsync(id, cancellationToken);
        if (storageObject is null)
        {
            return Result.Success();
        }

        storageObject.MarkForDeletion();
        await jobs.EnqueueAsync(
            new JobRequest(PurgeStorageObjectsJob.Type),
            cancellationToken);

        return Result.Success();
    }

    public async Task<IReadOnlyList<StorageObjectId>> FindByHashAsync(
        byte[] sha256,
        CancellationToken cancellationToken)
    {
        var matches = await repository.FindByHashAsync(sha256, cancellationToken);
        return matches.Select(match => match.Id).ToList();
    }

    private async Task TryDeleteAsync(ObjectLocation location, CancellationToken cancellationToken)
    {
        try
        {
            await fileStorage.DeleteAsync(location, cancellationToken);
        }
        catch (Exception exception)
        {
            // The staged-upload collector will pick it up later.
            logger.LogWarning(exception, "Could not remove partial upload {Key}.", location.Key);
        }
    }
}
