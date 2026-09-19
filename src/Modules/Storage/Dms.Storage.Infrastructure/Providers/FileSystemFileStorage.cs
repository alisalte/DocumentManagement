using Dms.Storage.Application;
using Dms.Storage.Contracts;
using Microsoft.Extensions.Options;

namespace Dms.Storage.Infrastructure.Providers;

/// <summary>
/// Local filesystem storage. Useful for tests and for small single-server installations that do
/// not want an object store. Keys are server-generated and validated again here, so nothing can
/// escape the configured root even if a key were ever crafted.
/// </summary>
public sealed class FileSystemFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(options.Value.FileSystem.RootPath);

    public string Provider => "filesystem";

    public async Task PutAsync(
        ObjectLocation location,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var path = ResolvePath(location);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a temporary name first, then move: a crashed upload never leaves a half file
        // that looks complete.
        var temporary = path + ".part";
        await using (var target = new FileStream(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true))
        {
            await content.CopyToAsync(target, cancellationToken);
        }

        File.Move(temporary, path, overwrite: true);
    }

    public Task<Stream> OpenAsync(ObjectLocation location, ByteRange? range, CancellationToken cancellationToken)
    {
        var path = ResolvePath(location);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The stored object no longer exists.", path);
        }

        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        if (range is { } wanted)
        {
            stream.Seek(wanted.From, SeekOrigin.Begin);
        }

        return Task.FromResult(stream);
    }

    public Task DeleteAsync(ObjectLocation location, CancellationToken cancellationToken)
    {
        var path = ResolvePath(location);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(ObjectLocation location, CancellationToken cancellationToken) =>
        Task.FromResult(File.Exists(ResolvePath(location)));

    private string ResolvePath(ObjectLocation location)
    {
        var candidate = Path.GetFullPath(Path.Combine(_root, location.Bucket, location.Key));
        if (!candidate.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The object key resolves outside the storage root.");
        }

        return candidate;
    }
}

/// <summary>No-op scanner. Phase 5 replaces it with ClamAV behind the same interface.</summary>
public sealed class NullMalwareScanner(IOptions<StorageOptions> options) : IMalwareScanner
{
    public bool IsEnabled => options.Value.Scanning.Enabled;

    public Task<ScanVerdict> ScanAsync(ObjectLocation location, CancellationToken cancellationToken)
    {
        // Enabled with no real scanner behind it would silently pass infected files, so it fails
        // closed instead: the object stays unavailable until a real scanner is configured.
        return Task.FromResult(IsEnabled ? ScanVerdict.Failed : ScanVerdict.Clean);
    }
}
