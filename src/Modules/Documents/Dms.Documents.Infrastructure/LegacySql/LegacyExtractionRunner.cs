using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Documents.Application;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dms.Documents.Infrastructure.LegacySql;

/// <summary>
/// Runs copies from the old SQL Server inside the process that received the request, because the
/// password exists only there. Each copy runs on its own task; progress and the stop flag go
/// through the database so any API instance can show or stop it. A copy whose process stops is
/// shown as interrupted and is resumed with the password again; files already copied are reused.
/// </summary>
public sealed class LegacyExtractionRunner(
    IServiceScopeFactory scopeFactory,
    ILegacySqlArchive archive,
    IOptions<ImportOptions> importOptions,
    TimeProvider timeProvider,
    ILogger<LegacyExtractionRunner> logger) : ILegacyExtractionQueue, IHostedService
{
    private static readonly TimeSpan ReportEvery = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<Guid, (Task Task, CancellationTokenSource Stop)> _running = new();
    private readonly CancellationTokenSource _shutdown = new();

    public void Submit(LegacyExtractionId id, LegacySqlConnectionSettings settings)
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var gate = new TaskCompletionSource();
        var task = Task.Run(async () =>
        {
            await gate.Task;
            try
            {
                await RunAsync(id, settings, stop.Token);
            }
            finally
            {
                _running.TryRemove(id.Value, out _);
                stop.Dispose();
            }
        });

        if (!_running.TryAdd(id.Value, (task, stop)))
        {
            logger.LogInformation("Legacy SQL copy {Id} is already running here.", id);
            stop.Dispose();
            gate.SetCanceled();
            return;
        }

        gate.SetResult();
    }

    public bool CancelLocal(LegacyExtractionId id)
    {
        if (!_running.TryGetValue(id.Value, out var running))
        {
            return false;
        }

        running.Stop.Cancel();
        return true;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _shutdown.CancelAsync();
        await Task.WhenAll(_running.Values.Select(running => running.Task))
            .WaitAsync(cancellationToken)
            .ContinueWith(_ => { }, TaskScheduler.Default);
    }

    /// <summary>The whole copy. Public so integration tests can run it without a hosted service.</summary>
    public async Task RunAsync(LegacyExtractionId id, LegacySqlConnectionSettings settings, CancellationToken stop)
    {
        string stagingRoot;
        LegacyExtraction? snapshot;
        try
        {
            var started = false;
            snapshot = await UpdateAsync(id, (extraction, now) => started = extraction.Start(now).IsSuccess, stop);
            if (snapshot is null || !started)
            {
                logger.LogWarning("Legacy SQL copy {Id} is not queued; nothing to do.", id);
                return;
            }

            var root = importOptions.Value.FilesRoot
                ?? throw new InvalidOperationException("Import files root is not configured on the server (Dms:Import:FilesRoot).");
            var resolved = ImportPathSafety.ResolveSafePath(root, snapshot.StagingFolder);
            stagingRoot = resolved.IsSuccess ? resolved.Value : throw new InvalidOperationException(resolved.Error.Message);
            var filesDir = Path.Combine(stagingRoot, "files");
            Directory.CreateDirectory(filesDir);

            await using var session = await archive.OpenAsync(settings, stop);
            var catalog = await session.ReadCatalogAsync(stop);
            var totalBytes = catalog.Files.Sum(file => file.Length);
            var warnings = new List<string>(catalog.Warnings);
            await UpdateAsync(id, (extraction, now) => extraction.Plan(
                catalog.Files.Count,
                totalBytes,
                catalog.EmptyFiles,
                Join(warnings),
                now), stop);
            logger.LogInformation(
                "Legacy SQL copy {Id}: {Files} files, {Bytes} bytes from {Target}.",
                id, catalog.Files.Count, totalBytes, settings);

            var parts = catalog.Files
                .GroupBy(file => (file.Table, file.ParentId))
                .ToDictionary(group => group.Key, group => group.Count());
            var seen = new Dictionary<(string, string?), int>();
            var entries = new List<LegacyManifestEntry>(catalog.Files.Count);
            var filesDone = 0;
            var reused = 0;
            long bytesDone = 0;
            var clock = Stopwatch.StartNew();
            var sizeWarnings = 0;

            foreach (var file in catalog.Files)
            {
                stop.ThrowIfCancellationRequested();
                var staged = await StageAsync(session, catalog, file, filesDir, stop);
                if (staged.Reused)
                {
                    reused++;
                }

                if (file.DeclaredSize is { } declared && declared != staged.Size && sizeWarnings++ < 20)
                {
                    warnings.Add($"{LegacyArchiveMapper.SourceId(file)}: FileSize {declared} ≠ {staged.Size} bytes.");
                }

                var key = (file.Table, file.ParentId);
                seen[key] = seen.GetValueOrDefault(key) + 1;
                entries.Add(LegacyArchiveMapper.Map(
                    catalog,
                    file,
                    seen[key],
                    parts[key],
                    $"files/{staged.FileName}",
                    staged.FileName,
                    staged.ContentType,
                    staged.Size,
                    staged.Sha256));

                filesDone++;
                bytesDone += file.Length;
                if (clock.Elapsed >= ReportEvery)
                {
                    clock.Restart();
                    var current = await UpdateAsync(
                        id,
                        (extraction, now) => extraction.Progress(filesDone, reused, bytesDone, now),
                        stop);
                    if (current is null || current.CancelRequested)
                    {
                        throw new OperationCanceledException("Stopped from the admin page.");
                    }
                }
            }

            if (sizeWarnings > 20)
            {
                warnings.Add($"… and {sizeWarnings - 20} more FileSize differences.");
            }

            if (entries.Count == 0)
            {
                throw new InvalidOperationException(
                    "The old database has no files (tbl_FileSanad, tbl_FilePeyvast and tbl_FilesSampleAsnad are empty).");
            }

            var manifest = LegacyManifestWriter.Write(entries);
            await File.WriteAllTextAsync(Path.Combine(stagingRoot, "manifest.json"), manifest, stop);

            await InScopeAsync(async services =>
            {
                var extraction = await services.GetRequiredService<ILegacyExtractionRepository>().FindAsync(id, stop)
                    ?? throw new InvalidOperationException("The copy was deleted.");
                var job = await services.GetRequiredService<ImportJobFactory>().CreateAsync(
                    extraction.Name,
                    LegacyArchiveMapper.SourceSystem,
                    manifest,
                    stagingRoot,
                    ImportFailurePolicy.ContinueOnError,
                    createMissingCategories: true,
                    dryRunOnly: false,
                    mappingInputs: null,
                    extraction.CreatedBy,
                    stop);
                if (job.IsFailure)
                {
                    throw new InvalidOperationException($"{job.Error.Code}: {job.Error.Message}");
                }

                var now = timeProvider.GetUtcNow();
                extraction.Progress(filesDone, reused, bytesDone, now);
                extraction.Complete(job.Value.Id, Join(warnings), now);
                await services.GetRequiredService<IAuditWriter>().WriteAsync(
                    new AuditRecord
                    {
                        Action = AuditActions.LegacySqlCopyCompleted,
                        UserId = extraction.CreatedBy,
                        EntityType = "LegacyExtraction",
                        EntityId = extraction.Id.Value,
                        Metadata = new Dictionary<string, object?>
                        {
                            ["files"] = entries.Count,
                            ["bytes"] = bytesDone,
                            ["reused"] = reused,
                            ["importJobId"] = job.Value.Id.Value,
                        },
                    },
                    stop);
            });
            logger.LogInformation("Legacy SQL copy {Id} finished: {Files} files.", id, entries.Count);
        }
        catch (OperationCanceledException)
        {
            if (_shutdown.IsCancellationRequested)
            {
                // Shutting down: leave it Running; it shows as interrupted and can be resumed.
                logger.LogWarning("Legacy SQL copy {Id} interrupted by shutdown.", id);
                return;
            }

            await SafeFinishAsync(id, (extraction, now) => extraction.MarkCancelled(now), null);
        }
        catch (Exception exception)
        {
            var message = exception is LegacySqlException sql
                ? $"{sql.Error.Code}: {sql.Error.Message}"
                : exception.Message;
            logger.LogError(exception, "Legacy SQL copy {Id} failed.", id);
            await SafeFinishAsync(id, (extraction, now) => extraction.Fail(message, now), message);
        }
    }

    private sealed record Staged(string FileName, string ContentType, long Size, string Sha256, bool Reused);

    private static async Task<Staged> StageAsync(
        ILegacySqlSession session,
        LegacyArchiveCatalog catalog,
        LegacyFileRow file,
        string filesDir,
        CancellationToken stop)
    {
        var sourceId = LegacyArchiveMapper.SourceId(file);
        var typeExtension = LegacyArchiveMapper.TypeExtension(catalog, file);

        // Resume: a finished file from an earlier run has the final name and the full length.
        foreach (var existing in Directory.EnumerateFiles(filesDir, sourceId + ".*"))
        {
            if (existing.EndsWith(".part", StringComparison.Ordinal))
            {
                continue;
            }

            var info = new FileInfo(existing);
            if (info.Length == file.Length)
            {
                var (sha, head) = await DigestAsync(existing, stop);
                var (_, contentType) = FileSignature.Detect(head, typeExtension);
                return new Staged(info.Name, contentType, info.Length, sha, Reused: true);
            }

            File.Delete(existing);
        }

        var partial = Path.Combine(filesDir, sourceId + ".part");
        long size;
        string sha256;
        var headBuffer = new byte[16];
        var headLength = 0;
        await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var hashing = new HashingStream(output, hash, headBuffer, length => headLength = length);
            await session.CopyFileAsync(file, hashing, stop);
            size = hashing.Position;
            sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        if (size == 0)
        {
            File.Delete(partial);
            throw new InvalidOperationException($"{sourceId}: the file became empty while copying.");
        }

        var (extension, type) = FileSignature.Detect(headBuffer.AsSpan(0, headLength), typeExtension);
        var fileName = $"{sourceId}.{extension}";
        File.Move(partial, Path.Combine(filesDir, fileName), overwrite: true);
        return new Staged(fileName, type, size, sha256, Reused: false);
    }

    private static async Task<(string Sha256, byte[] Head)> DigestAsync(string path, CancellationToken stop)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        var head = new byte[16];
        var read = await input.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, stop);
        input.Position = 0;
        var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, stop));
        return (sha, head[..read]);
    }

    private static string? Join(List<string> warnings) => warnings.Count == 0 ? null : string.Join('\n', warnings);

    /// <summary>Loads, changes and commits the copy in its own short transaction.</summary>
    private async Task<LegacyExtraction?> UpdateAsync(
        LegacyExtractionId id,
        Action<LegacyExtraction, DateTimeOffset> change,
        CancellationToken stop)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                LegacyExtraction? result = null;
                await InScopeAsync(async services =>
                {
                    result = await services.GetRequiredService<ILegacyExtractionRepository>().FindAsync(id, stop);
                    if (result is not null)
                    {
                        change(result, timeProvider.GetUtcNow());
                    }
                });
                return result;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                // Someone pressed «stop» at the same moment; read the row again.
            }
        }
    }

    private async Task SafeFinishAsync(LegacyExtractionId id, Action<LegacyExtraction, DateTimeOffset> finish, string? error)
    {
        try
        {
            await InScopeAsync(async services =>
            {
                var extraction = await services.GetRequiredService<ILegacyExtractionRepository>()
                    .FindAsync(id, CancellationToken.None);
                if (extraction is null)
                {
                    return;
                }

                finish(extraction, timeProvider.GetUtcNow());
                await services.GetRequiredService<IAuditWriter>().WriteAsync(
                    new AuditRecord
                    {
                        Action = error is null ? AuditActions.LegacySqlCopyCancelled : AuditActions.LegacySqlCopyFailed,
                        Outcome = error is null ? AuditOutcome.Success : AuditOutcome.Failed,
                        ActorType = error is null ? AuditActorType.User : AuditActorType.System,
                        UserId = extraction.CreatedBy,
                        EntityType = "LegacyExtraction",
                        EntityId = extraction.Id.Value,
                        Metadata = error is null ? null : new Dictionary<string, object?> { ["error"] = error },
                    },
                    CancellationToken.None);
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not record the end of legacy SQL copy {Id}.", id);
        }
    }

    private async Task InScopeAsync(Func<IServiceProvider, Task> work)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.BeginAsync(CancellationToken.None);
        try
        {
            await work(scope.ServiceProvider);
            await unitOfWork.CommitAsync(CancellationToken.None);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Writes through to a file while hashing and keeping the first bytes for type detection.</summary>
    private sealed class HashingStream(Stream inner, IncrementalHash hash, byte[] head, Action<int> headLength) : Stream
    {
        private int _head;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => Position;

        public override long Position { get; set; }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Track(buffer);
            inner.Write(buffer);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Track(buffer.Span);
            await inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        private void Track(ReadOnlySpan<byte> buffer)
        {
            hash.AppendData(buffer);
            if (_head < head.Length)
            {
                var take = Math.Min(head.Length - _head, buffer.Length);
                buffer[..take].CopyTo(head.AsSpan(_head));
                _head += take;
                headLength(_head);
            }

            Position += buffer.Length;
        }
    }
}
