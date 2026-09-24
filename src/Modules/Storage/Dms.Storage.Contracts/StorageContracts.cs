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

    /// <summary>
    /// Stores a file the system produced (a page image, extracted text). Committed at once, never
    /// scanned (it came from a clean source), and never exposed except through its owner.
    /// Deleted together with <paramref name="source"/>.
    /// </summary>
    Task<StorageObjectId> StoreDerivedAsync(
        StorageObjectId source,
        Stream content,
        string fileName,
        string mimeType,
        DerivedPurpose purpose,
        CancellationToken cancellationToken);

    /// <summary>Opens the bytes of any object, clean or not, for internal processing only.</summary>
    Task<Stream> OpenForProcessingAsync(StorageObjectId id, CancellationToken cancellationToken);

    /// <summary>Other objects with the same SHA-256. The caller filters by what the user may see.</summary>
    Task<IReadOnlyList<StorageObjectId>> FindByHashAsync(byte[] sha256, CancellationToken cancellationToken);
}

public readonly record struct ByteRange(long From, long? To);

/// <summary>What a derived object holds. Derived objects are made by the system, never uploaded.</summary>
public enum DerivedPurpose
{
    Rendition,
    ExtractedText,
}

public enum RenditionStatus
{
    /// <summary>Not produced yet: the processing job has not reached this file.</summary>
    Pending,
    Ready,
    Failed,

    /// <summary>No preview for this kind of file (CAD, archives, …): metadata and download only.</summary>
    NotSupported,
}

/// <summary>The preview of one stored file: page images rendered once, served through the API.</summary>
public sealed record RenditionInfo(StorageObjectId SourceId, RenditionStatus Status, int PageCount, string? Error);

/// <summary>
/// Previews and print pages (section 7.4). They are images of the pages, never the original file,
/// so VIEW and PRINT never quietly grant DOWNLOAD. Each page can be stamped with who is looking
/// and when.
/// </summary>
public interface IRenditionService
{
    Task<RenditionInfo> GetPreviewAsync(StorageObjectId sourceId, CancellationToken cancellationToken);

    /// <param name="page">1-based.</param>
    /// <param name="watermark">Text drawn across the page, or null for none.</param>
    Task<Result<StoredContent>> OpenPageAsync(
        StorageObjectId sourceId,
        int page,
        string? watermark,
        CancellationToken cancellationToken);

    Task<Result<StoredContent>> OpenThumbnailAsync(StorageObjectId sourceId, CancellationToken cancellationToken);

    /// <summary>Queues processing again (scan, previews, text) for a committed file, e.g. after a failure.</summary>
    Task RequestProcessingAsync(StorageObjectId sourceId, CancellationToken cancellationToken);
}

/// <summary>
/// Told when a committed file has been scanned and rendered, in the processing job's transaction.
/// Search implements it to extract text and index; Storage knows nothing about search.
/// </summary>
public interface IObjectProcessedListener
{
    Task OnProcessedAsync(StorageObjectInfo file, CancellationToken cancellationToken);
}
