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
    public async Task<Result<WrittenFile>> WriteAsync(
        Stream content,
        string fileName,
        string? declaredMimeType,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var id = StorageObjectId.New();
        var safeName = FileNames.Sanitize(fileName);

        // The same id names the row and the object key, so an orphaned object can always be traced.
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
            return Result.Failure<WrittenFile>(Error.Validation(
                "upload.too_large",
                $"The file exceeds the maximum upload size of {tooLarge.MaxBytes} bytes."));
        }
        catch (Exception exception)
        {
            await TryDeleteAsync(location, CancellationToken.None);
            logger.LogError(exception, "Upload of {FileName} failed while streaming to storage.", safeName);
            throw;
        }

        if (measured.BytesRead == 0)
        {
            await TryDeleteAsync(location, cancellationToken);
            return Result.Failure<WrittenFile>(Error.Validation("upload.empty", "The file is empty."));
        }

        return Result.Success(new WrittenFile(
            id,
            location.Bucket,
            location.Key,
            safeName,
            MimeSniffer.Detect(measured.Sample, safeName, declaredMimeType),
            declaredMimeType,
            measured.BytesRead,
            measured.Digest));
    }

    public Task<StagedUpload> RegisterAsync(WrittenFile file, CancellationToken cancellationToken)
    {
        var scanStatus = scanner.IsEnabled ? ScanStatus.Pending : ScanStatus.Skipped;

        var storageObject = StorageObject.Stage(
            file.Id,
            fileStorage.Provider,
            file.Bucket,
            file.ObjectKey,
            StorageObjectPurpose.Original,
            file.FileName,
            file.DetectedMimeType,
            file.DeclaredMimeType,
            file.Size,
            file.Sha256,
            scanStatus,
            currentUser.UserId,
            timeProvider.GetUtcNow());

        // The scan runs once the file is attached (ProcessObjectJob); a staged file nobody attaches
        // is never served and is collected instead.
        repository.Add(storageObject);

        return Task.FromResult(new StagedUpload(
            storageObject.Id,
            file.FileName,
            file.DetectedMimeType,
            file.Size,
            Convert.ToHexStringLower(file.Sha256)));
    }

    public Task DiscardAsync(WrittenFile file, CancellationToken cancellationToken) =>
        TryDeleteAsync(new ObjectLocation(file.Bucket, file.ObjectKey), cancellationToken);

    public async Task<Result> CommitAsync(StorageObjectId id, CancellationToken cancellationToken)
    {
        var storageObject = await repository.FindAsync(id, cancellationToken);
        if (storageObject is null)
        {
            return Result.Failure(Error.NotFound("storage.not_found", "The uploaded file no longer exists."));
        }

        var wasStaged = storageObject.Status == StorageObjectStatus.Staged;
        var committed = storageObject.Commit(timeProvider.GetUtcNow());
        if (committed.IsSuccess && wasStaged)
        {
            // Same transaction as the version that references it: a committed file always gets
            // scanned, rendered and indexed (section 8.1).
            await jobs.EnqueueAsync(ProcessObjectJob.For(id), cancellationToken);
        }

        return committed;
    }

    public async Task<StorageObjectId> StoreDerivedAsync(
        Stream content,
        string fileName,
        string mimeType,
        DerivedPurpose purpose,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var id = StorageObjectId.New();
        var prefix = purpose == DerivedPurpose.ExtractedText ? "text" : "renditions";
        var location = new ObjectLocation(settings.Bucket, FileNames.BuildObjectKey(prefix, id.Value, now));

        await using var measured = new HashingReadStream(content, long.MaxValue);
        await fileStorage.PutAsync(location, measured, mimeType, cancellationToken);

        repository.Add(StorageObject.Derived(
            id,
            fileStorage.Provider,
            location.Bucket,
            location.Key,
            purpose == DerivedPurpose.ExtractedText ? StorageObjectPurpose.ExtractedText : StorageObjectPurpose.Rendition,
            FileNames.Sanitize(fileName),
            mimeType,
            measured.BytesRead,
            measured.Digest,
            now));

        return id;
    }

    public async Task<Stream> OpenForProcessingAsync(StorageObjectId id, CancellationToken cancellationToken)
    {
        var storageObject = await repository.FindAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Storage object {id} does not exist.");

        return await fileStorage.OpenAsync(new ObjectLocation(storageObject.Bucket, storageObject.ObjectKey), null, cancellationToken);
    }

    public async Task<StorageObjectInfo?> FindAsync(StorageObjectId id, CancellationToken cancellationToken) =>
        (await repository.FindAsync(id, cancellationToken))?.ToInfo();

    public async Task<IReadOnlyDictionary<StorageObjectId, StorageObjectInfo>> FindManyAsync(
        IReadOnlyCollection<StorageObjectId> ids,
        CancellationToken cancellationToken)
    {
        var found = await repository.FindManyAsync(ids, cancellationToken);
        return found.ToDictionary(item => item.Id, item => item.ToInfo());
    }

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
