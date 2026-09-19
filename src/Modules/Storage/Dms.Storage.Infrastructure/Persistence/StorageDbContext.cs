using Dms.Infrastructure.Persistence;
using Dms.SharedKernel;
using Dms.Storage.Contracts;
using Dms.Storage.Domain;
using Microsoft.EntityFrameworkCore;

namespace Dms.Storage.Infrastructure.Persistence;

public sealed class StorageDbContext : DbContext
{
    public const string Schema = "storage";

    public StorageDbContext(DbContextOptions<StorageDbContext> options, DbSession session)
        : base(options) => session.Register(this);

    public DbSet<StorageObject> StorageObjects => Set<StorageObject>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<StorageObject>(entity =>
        {
            entity.ToTable("storage_objects");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new StorageObjectId(value));
            entity.Property(item => item.Provider).HasColumnName("provider").HasMaxLength(32).IsRequired();
            entity.Property(item => item.Bucket).HasColumnName("bucket").HasMaxLength(128).IsRequired();
            entity.Property(item => item.ObjectKey).HasColumnName("object_key").HasMaxLength(512).IsRequired();
            entity.Property(item => item.Purpose).HasColumnName("purpose")
                .HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(item => item.OriginalFileName)
                .HasColumnName("original_file_name").HasMaxLength(255).IsRequired();
            entity.Property(item => item.DetectedMimeType)
                .HasColumnName("detected_mime_type").HasMaxLength(255).IsRequired();
            entity.Property(item => item.DeclaredMimeType).HasColumnName("declared_mime_type").HasMaxLength(255);
            entity.Property(item => item.Size).HasColumnName("size");
            entity.Property(item => item.Sha256).HasColumnName("sha256").IsRequired();
            entity.Property(item => item.Status).HasColumnName("status")
                .HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(item => item.ScanStatus).HasColumnName("scan_status")
                .HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(item => item.ScannedAt).HasColumnName("scanned_at");
            entity.Property(item => item.CreatedBy).HasColumnName("created_by")
                .HasConversion(id => id!.Value.Value, value => new UserId(value));
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.Property(item => item.CommittedAt).HasColumnName("committed_at");
            entity.Property(item => item.DeletedAt).HasColumnName("deleted_at");
            entity.Property<uint>("Version").HasColumnName("xmin").IsRowVersion();

            entity.HasIndex(item => new { item.Provider, item.Bucket, item.ObjectKey })
                .IsUnique().HasDatabaseName("ux_storage_objects_key");

            // Duplicate detection, and the integrity scrub in a later phase.
            entity.HasIndex(item => item.Sha256).HasDatabaseName("ix_storage_objects_sha256");

            entity.HasIndex(item => item.CreatedAt)
                .HasFilter("status = 'Staged'").HasDatabaseName("ix_storage_objects_staged");
            entity.HasIndex(item => item.Status)
                .HasFilter("status = 'PendingDeletion'").HasDatabaseName("ix_storage_objects_pending_deletion");

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_storage_objects_size", "size >= 0");
                table.HasCheckConstraint("ck_storage_objects_sha256", "octet_length(sha256) = 32");
            });
        });
    }
}
