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
    /// <summary>
    /// When false, objects are marked "skipped" and content is served. When true, content stays
    /// unavailable until a scanner reports clean (decision D9).
    /// </summary>
    public bool Enabled { get; set; }
}
