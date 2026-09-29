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

    public void Add(StorageObject storageObject) => context.StorageObjects.Add(storageObject);
}

public sealed class RenditionRepository(StorageDbContext context) : IRenditionRepository
{
    public Task<Rendition?> FindAsync(StorageObjectId source, RenditionKind kind, CancellationToken cancellationToken) =>
        context.Renditions.Include(rendition => rendition.Pages)
            .FirstOrDefaultAsync(rendition => rendition.SourceObjectId == source && rendition.Kind == kind, cancellationToken);

    public void Add(Rendition rendition) => context.Renditions.Add(rendition);
}
