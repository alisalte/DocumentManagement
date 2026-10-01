using Dms.Infrastructure.Persistence;
using Dms.Search.Application;
using Dms.Search.Domain;
using Microsoft.EntityFrameworkCore;

namespace Dms.Search.Infrastructure.Persistence;

public sealed class SearchDbContext : DbContext
{
    public const string Schema = "search";

    public SearchDbContext(DbContextOptions<SearchDbContext> options, DbSession session)
        : base(options) => session.Register(this);

    public DbSet<ContentExtraction> Extractions => Set<ContentExtraction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ContentExtraction>(entity =>
        {
            entity.ToTable("content_extractions");
            entity.HasKey(extraction => extraction.Id);
            entity.Property(extraction => extraction.Id).HasColumnName("id").ValueGeneratedNever();

            // No foreign key into storage: modules only reference each other by id.
            entity.Property(extraction => extraction.StorageObjectId).HasColumnName("storage_object_id");
            entity.Property(extraction => extraction.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(extraction => extraction.Method).HasColumnName("method").HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(extraction => extraction.TextObjectId).HasColumnName("text_object_id");
            entity.Property(extraction => extraction.CharCount).HasColumnName("char_count");
            entity.Property(extraction => extraction.Engine).HasColumnName("engine").HasMaxLength(64);
            entity.Property(extraction => extraction.Attempts).HasColumnName("attempts");
            entity.Property(extraction => extraction.LastError).HasColumnName("last_error").HasMaxLength(2000);
            entity.Property(extraction => extraction.CreatedAt).HasColumnName("created_at");
            entity.Property(extraction => extraction.CompletedAt).HasColumnName("completed_at");
            entity.Property<uint>("Version").HasColumnName("xmin").IsRowVersion();

            entity.HasIndex(extraction => extraction.StorageObjectId).IsUnique().HasDatabaseName("ux_content_extractions_object");
            entity.HasIndex(extraction => extraction.Status).HasDatabaseName("ix_content_extractions_status");
        });
    }
}

public sealed class ContentExtractionRepository(SearchDbContext context) : IContentExtractionRepository
{
    public Task<ContentExtraction?> FindAsync(Guid storageObjectId, CancellationToken cancellationToken) =>
        context.Extractions.FirstOrDefaultAsync(extraction => extraction.StorageObjectId == storageObjectId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, ContentExtraction>> FindManyAsync(
        IReadOnlyCollection<Guid> storageObjectIds,
        CancellationToken cancellationToken)
    {
        if (storageObjectIds.Count == 0)
        {
            return new Dictionary<Guid, ContentExtraction>();
        }

        var ids = storageObjectIds.ToArray();
        return await context.Extractions.AsNoTracking()
            .Where(extraction => ids.Contains(extraction.StorageObjectId))
            .ToDictionaryAsync(extraction => extraction.StorageObjectId, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<ExtractionStatus, int>> CountByStatusAsync(CancellationToken cancellationToken) =>
        await context.Extractions.AsNoTracking()
            .GroupBy(extraction => extraction.Status)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);

    public async Task<ExtractionMethodTotals> TotalsAsync(CancellationToken cancellationToken)
    {
        var rows = await context.Extractions.AsNoTracking()
            .Where(extraction => extraction.Status == ExtractionStatus.Completed)
            .GroupBy(extraction => extraction.Method)
            .Select(group => new { group.Key, Count = group.Count(), Characters = group.Sum(extraction => (long)extraction.CharCount) })
            .ToListAsync(cancellationToken);

        int Count(ExtractionMethod method) => rows.FirstOrDefault(row => row.Key == method)?.Count ?? 0;
        long Characters(ExtractionMethod method) => rows.FirstOrDefault(row => row.Key == method)?.Characters ?? 0;
        return new ExtractionMethodTotals(
            Count(ExtractionMethod.Ocr),
            Count(ExtractionMethod.TextLayer),
            Count(ExtractionMethod.None),
            Characters(ExtractionMethod.Ocr),
            Characters(ExtractionMethod.TextLayer));
    }

    public async Task<IReadOnlyList<ContentExtraction>> ListRecentAsync(
        ExtractionStatus? status,
        ExtractionMethod? method,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = context.Extractions.AsNoTracking().AsQueryable();
        if (status is { } wantedStatus)
        {
            query = query.Where(extraction => extraction.Status == wantedStatus);
        }

        if (method is { } wantedMethod)
        {
            query = query.Where(extraction => extraction.Method == wantedMethod);
        }

        return await query
            .OrderByDescending(extraction => extraction.CompletedAt)
            .ThenByDescending(extraction => extraction.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ContentExtraction>> ListFailedAsync(int? belowAttempts, int limit, CancellationToken cancellationToken) =>
        await context.Extractions.AsNoTracking()
            .Where(extraction => extraction.Status == ExtractionStatus.Failed
                && (belowAttempts == null || extraction.Attempts < belowAttempts))
            .OrderBy(extraction => extraction.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public void Add(ContentExtraction extraction) => context.Extractions.Add(extraction);
}
