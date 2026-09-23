using Dms.SharedKernel;

namespace Dms.Storage.Contracts;

public readonly record struct StorageObjectId(Guid Value)
{
    public static StorageObjectId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public enum StorageObjectStatus
{
    /// <summary>Uploaded but not yet attached to a document. Collected by the cleanup job.</summary>
    Staged,
    Committed,
    Quarantined,
    PendingDeletion,
    Deleted,
}

/// <summary>Decision D9: content stays unavailable until the scan reports Clean.</summary>
public enum ScanStatus
{
    Pending,
    Clean,
    Infected,
    Failed,

    /// <summary>Scanning is switched off for this deployment; treated as clean.</summary>
    Skipped,
}

/// <summary>What another module needs to know about a stored file.</summary>
/// <param name="CreatedBy">The uploader. Only they may attach a staged upload to a document.</param>
public sealed record StorageObjectInfo(
    StorageObjectId Id,
    string FileName,
    string MimeType,
    long Size,
    byte[] Sha256,
    StorageObjectStatus Status,
    ScanStatus ScanStatus,
    UserId? CreatedBy)
{
    public string Sha256Hex => Convert.ToHexStringLower(Sha256);

    /// <summary>Content may only be served once the scan has passed.</summary>
    public bool IsContentAvailable => ScanStatus is ScanStatus.Clean or ScanStatus.Skipped;
}

public sealed record StagedUpload(StorageObjectId Id, string FileName, string MimeType, long Size, string Sha256Hex);

/// <summary>
/// Bytes that have reached storage but are not yet recorded in the database. Produced by
/// <see cref="IStorageService.WriteAsync"/> outside any transaction and turned into a row by
/// <see cref="IStorageService.RegisterAsync"/> inside a short one.
/// </summary>
public sealed record WrittenFile(
    StorageObjectId Id,
    string Bucket,
    string ObjectKey,
    string FileName,
    string DetectedMimeType,
    string? DeclaredMimeType,
    long Size,
    byte[] Sha256);

/// <summary>A stream of stored bytes, plus what the caller needs to send response headers.</summary>
public sealed record StoredContent(Stream Content, string MimeType, string FileName, long Size) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>
/// The storage module's public surface. Callers never see S3, MinIO or file paths, and never get
/// a direct storage URL: every byte is served through the application's authorization layer.
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// Streams an upload into storage, hashing and sniffing it on the way through. Touches no
    /// database: an upload can take minutes, and no transaction or pooled connection may be held
    /// open for that long (docs/architecture.md section 4.11).
    /// </summary>
    Task<Result<WrittenFile>> WriteAsync(
        Stream content,
        string fileName,
        string? declaredMimeType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records written bytes as a STAGED object and queues its malware scan, in the caller's
    /// transaction.
    /// </summary>
    Task<StagedUpload> RegisterAsync(WrittenFile file, CancellationToken cancellationToken);

    /// <summary>Best effort removal of bytes that never got a database row.</summary>
    Task DiscardAsync(WrittenFile file, CancellationToken cancellationToken);

    /// <summary>Attaches a staged object to a document. Idempotent.</summary>
    Task<Result> CommitAsync(StorageObjectId id, CancellationToken cancellationToken);

    Task<StorageObjectInfo?> FindAsync(StorageObjectId id, CancellationToken cancellationToken);

    /// <summary>Batch lookup for listings, so a version history is one query rather than N.</summary>
    Task<IReadOnlyDictionary<StorageObjectId, StorageObjectInfo>> FindManyAsync(
        IReadOnlyCollection<StorageObjectId> ids,
        CancellationToken cancellationToken);

    Task<Result<StoredContent>> OpenAsync(StorageObjectId id, ByteRange? range, CancellationToken cancellationToken);

    /// <summary>Marks an object for deletion; the bytes are removed by a background job.</summary>
    Task<Result> MarkForDeletionAsync(StorageObjectId id, CancellationToken cancellationToken);

    /// <summary>Other objects with the same SHA-256. The caller filters by what the user may see.</summary>
    Task<IReadOnlyList<StorageObjectId>> FindByHashAsync(byte[] sha256, CancellationToken cancellationToken);
}

public readonly record struct ByteRange(long From, long? To);
