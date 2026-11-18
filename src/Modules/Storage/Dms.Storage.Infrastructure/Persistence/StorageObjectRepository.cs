using Dms.Storage.Application;
using Dms.Storage.Contracts;
using Dms.Storage.Domain;
using Microsoft.EntityFrameworkCore;

namespace Dms.Storage.Infrastructure.Persistence;

public sealed class StorageObjectRepository(StorageDbContext context) : IStorageObjectRepository
{
    public async Task<StorageObject?> FindAsync(StorageObjectId id, CancellationToken cancellationToken) =>
        // FindAsync checks the identity map first so Register + Find in one transaction works
        // (LINQ FirstOrDefault can miss Added entities with value-converted keys).
        await context.StorageObjects.FindAsync([id], cancellationToken);

    public async Task<IReadOnlyList<StorageObject>> FindManyAsync(
        IReadOnlyCollection<StorageObjectId> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var keys = ids.ToArray();
        return await context.StorageObjects.AsNoTracking()
            .Where(item => keys.Contains(item.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StorageObject>> FindByHashAsync(
        byte[] sha256,
        CancellationToken cancellationToken) =>
        await context.StorageObjects.AsNoTracking()
            .Where(item => item.Sha256 == sha256
                && item.Status == StorageObjectStatus.Committed
                && item.Purpose == StorageObjectPurpose.Original)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StorageObject>> ListStagedBeforeAsync(
        DateTimeOffset cutoff,
        int limit,
        CancellationToken cancellationToken) =>
        await context.StorageObjects
            .Where(item => item.Status == StorageObjectStatus.Staged && item.CreatedAt < cutoff)
            .OrderBy(item => item.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StorageObject>> ListPendingDeletionAsync(
        int limit,
        CancellationToken cancellationToken) =>
        await context.StorageObjects
            .Where(item => item.Status == StorageObjectStatus.PendingDeletion)
            .OrderBy(item => item.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StorageObject>> ListDerivedFromAsync(
        StorageObjectId source,
        CancellationToken cancellationToken) =>
        await context.StorageObjects
            .Where(item => item.DerivedFromId == source)
            .ToListAsync(cancellationToken);

    public async Task<long> SumLiveOriginalBytesAsync(CancellationToken cancellationToken) =>
        await context.StorageObjects.AsNoTracking()
            .Where(item =>
                item.Purpose == StorageObjectPurpose.Original
                && item.Status != StorageObjectStatus.Deleted)
            .SumAsync(item => (long?)item.Size, cancellationToken)
        ?? 0;

    public async Task<IReadOnlyList<StorageObject>> ListWaitingForScanAsync(
        int limit,
        CancellationToken cancellationToken) =>
        await WaitingForScan()
            .OrderBy(item => item.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<int> CountWaitingForScanAsync(CancellationToken cancellationToken) =>
        WaitingForScan().CountAsync(cancellationToken);

    public async Task<IReadOnlyList<StorageObjectId>> ListAvailableOriginalsAsync(
        StorageObjectId? after,
        int limit,
        CancellationToken cancellationToken)
    {
        // Keyset paging on the uuid; the strongly typed id has no ordering EF can translate.
        var source = after is { } cursor
            ? context.StorageObjects.FromSqlInterpolated(
                $"SELECT * FROM storage.storage_objects WHERE id > {cursor.Value}")
            : context.StorageObjects;

        return await source.AsNoTracking()
            .Where(item =>
                item.Purpose == StorageObjectPurpose.Original
                && item.Status == StorageObjectStatus.Committed
                && (item.ScanStatus == ScanStatus.Clean || item.ScanStatus == ScanStatus.Skipped))
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public void Add(StorageObject storageObject) => context.StorageObjects.Add(storageObject);

    private IQueryable<StorageObject> WaitingForScan() =>
        context.StorageObjects.Where(item =>
            item.Purpose == StorageObjectPurpose.Original
            && item.Status == StorageObjectStatus.Committed
            && (item.ScanStatus == ScanStatus.Pending || item.ScanStatus == ScanStatus.Failed));
}

public sealed class StorageSettingRepository(StorageDbContext context) : IStorageSettingRepository
{
    public async Task<StorageSetting?> FindAsync(string key, CancellationToken cancellationToken) =>
        await context.Settings.FindAsync([key], cancellationToken);

    public void Add(StorageSetting setting) => context.Settings.Add(setting);
}

public sealed class RenditionRepository(StorageDbContext context) : IRenditionRepository
{
    public Task<Rendition?> FindAsync(StorageObjectId source, RenditionKind kind, CancellationToken cancellationToken) =>
        context.Renditions.Include(rendition => rendition.Pages)
            .FirstOrDefaultAsync(rendition => rendition.SourceObjectId == source && rendition.Kind == kind, cancellationToken);

    public void Add(Rendition rendition) => context.Renditions.Add(rendition);
}
