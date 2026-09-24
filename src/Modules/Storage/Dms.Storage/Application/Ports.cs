using Dms.Storage.Contracts;
using Dms.Storage.Domain;

namespace Dms.Storage.Application;

public readonly record struct ObjectLocation(string Bucket, string Key);

/// <summary>
/// The physical store. Deliberately tiny and free of SDK types, so S3/MinIO, a plain filesystem or
/// anything else can sit behind it without the domain noticing.
/// </summary>
public interface IFileStorage
{
    string Provider { get; }

    Task PutAsync(ObjectLocation location, Stream content, string contentType, CancellationToken cancellationToken);

    Task<Stream> OpenAsync(ObjectLocation location, ByteRange? range, CancellationToken cancellationToken);

    Task DeleteAsync(ObjectLocation location, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(ObjectLocation location, CancellationToken cancellationToken);
}

public enum ScanVerdict
{
    Clean,
    Infected,
    Failed,
}

/// <summary>
/// Malware scanning. Phase 2 ships a no-op implementation that reports "skipped" when scanning is
/// switched off; ClamAV arrives in phase 5 behind this same interface.
/// </summary>
public interface IMalwareScanner
{
    bool IsEnabled { get; }

    Task<ScanVerdict> ScanAsync(ObjectLocation location, CancellationToken cancellationToken);
}

public interface IStorageObjectRepository
{
    Task<StorageObject?> FindAsync(StorageObjectId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<StorageObject>> FindManyAsync(
        IReadOnlyCollection<StorageObjectId> ids,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StorageObject>> FindByHashAsync(byte[] sha256, CancellationToken cancellationToken);

    Task<IReadOnlyList<StorageObject>> ListStagedBeforeAsync(
        DateTimeOffset cutoff,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StorageObject>> ListPendingDeletionAsync(int limit, CancellationToken cancellationToken);

    void Add(StorageObject storageObject);
}

public interface IRenditionRepository
{
    Task<Rendition?> FindAsync(StorageObjectId source, RenditionKind kind, CancellationToken cancellationToken);

    void Add(Rendition rendition);
}

/// <summary>One rendered page, already encoded, ready to store.</summary>
public sealed record RenderedPage(int Number, byte[] Image);

/// <summary>
/// Turns a file into page images. Implementations live in Infrastructure (PDFium for PDF, Skia
/// for images, Gotenberg for Office via PDF); the job only sees pages.
/// </summary>
public interface IPageRenderer
{
    /// <summary>Null when this renderer does not handle the type.</summary>
    IAsyncEnumerable<RenderedPage>? Render(string mimeType, Stream content, RenderRequest request, CancellationToken cancellationToken);
}

/// <param name="FileName">The original name, for converters that pick a format by extension.</param>
public sealed record RenderRequest(int Width, int MaxPages, string? FileName = null);

/// <summary>Draws the viewer's name and the time across a page image when it is served.</summary>
public interface IWatermarker
{
    byte[] Apply(byte[] image, string text);
}

public sealed class StorageOptions
{
    public const string SectionName = "Dms:Storage";

    /// <summary>"s3" or "filesystem".</summary>
    public string Provider { get; set; } = "s3";

    public string Bucket { get; set; } = "dms-objects";

    /// <summary>Largest upload accepted, before any per-document-type limit.</summary>
    public long MaxUploadBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>Staged objects never attached to a document are removed after this long.</summary>
    public TimeSpan StagingRetention { get; set; } = TimeSpan.FromHours(24);

    public S3Options S3 { get; set; } = new();

    public FileSystemOptions FileSystem { get; set; } = new();

    public ScanningOptions Scanning { get; set; } = new();

    public RenditionOptions Renditions { get; set; } = new();
}

public sealed class RenditionOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Page image width in pixels; about 150 DPI for A4.</summary>
    public int PageWidth { get; set; } = 1240;

    public int ThumbnailWidth { get; set; } = 320;

    /// <summary>Long documents get a preview of their first pages; download has the rest.</summary>
    public int MaxPages { get; set; } = 300;

    /// <summary>Gotenberg for Office → PDF. Empty disables Office previews.</summary>
    public string? GotenbergUrl { get; set; }
}

public sealed class S3Options
{
    public string ServiceUrl { get; set; } = "http://localhost:9000";

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string Region { get; set; } = "us-east-1";

    /// <summary>MinIO and most self-hosted stores need path style addressing.</summary>
    public bool ForcePathStyle { get; set; } = true;
}

public sealed class FileSystemOptions
{
    public string RootPath { get; set; } = "/var/lib/dms/objects";
}

public sealed class ScanningOptions
{
    /// <summary>Where clamd listens (INSTREAM over TCP).</summary>
    public string ClamAvHost { get; set; } = "clamav";

    public int ClamAvPort { get; set; } = 3310;

    /// <summary>
    /// When false, objects are marked "skipped" and content is served. When true, content stays
    /// unavailable until a scanner reports clean (decision D9).
    /// </summary>
    public bool Enabled { get; set; }
}
