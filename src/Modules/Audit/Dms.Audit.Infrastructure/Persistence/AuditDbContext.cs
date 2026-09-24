using Dms.Audit.Domain;
using Dms.Infrastructure.Persistence;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Dms.Audit.Infrastructure.Persistence;

public sealed class AuditDbContext : DbContext
{
    public const string Schema = "audit";

    public AuditDbContext(DbContextOptions<AuditDbContext> options, DbSession session)
        : base(options) => session.Register(this);

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<AuditSeal> Seals => Set<AuditSeal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("audit_logs");

            // The table is range partitioned on occurred_at, so the primary key has to include it.
            entity.HasKey(record => new { record.Id, record.OccurredAt });

            entity.Property(record => record.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new AuditEntryId(value));
            entity.Property(record => record.OccurredAt).HasColumnName("occurred_at");
            entity.Property(record => record.ActorType).HasColumnName("actor_type").HasMaxLength(16).IsRequired();
            entity.Property(record => record.UserId).HasColumnName("user_id")
                .HasConversion(id => id!.Value.Value, value => new UserId(value));
            entity.Property(record => record.ShareLinkId).HasColumnName("share_link_id");
            entity.Property(record => record.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
            entity.Property(record => record.Outcome).HasColumnName("outcome").HasMaxLength(16).IsRequired();
            entity.Property(record => record.EntityType).HasColumnName("entity_type").HasMaxLength(64);
            entity.Property(record => record.EntityId).HasColumnName("entity_id");
            entity.Property(record => record.DocumentId).HasColumnName("document_id");
            entity.Property(record => record.VersionId).HasColumnName("version_id");
            entity.Property(record => record.IpAddress).HasColumnName("ip_address").HasMaxLength(64);
            entity.Property(record => record.UserAgent).HasColumnName("user_agent").HasMaxLength(512);
            entity.Property(record => record.CorrelationId).HasColumnName("correlation_id").HasMaxLength(64);
            entity.Property(record => record.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();

            entity.HasIndex(record => new { record.DocumentId, record.OccurredAt })
                .HasDatabaseName("ix_audit_document");
            entity.HasIndex(record => new { record.UserId, record.OccurredAt })
                .HasDatabaseName("ix_audit_user");
            entity.HasIndex(record => new { record.Action, record.OccurredAt })
                .HasDatabaseName("ix_audit_action");
        });

        modelBuilder.Entity<AuditSeal>(entity =>
        {
            entity.ToTable("audit_seals");
            entity.HasKey(seal => seal.Sequence);
            entity.Property(seal => seal.Sequence).HasColumnName("sequence").UseIdentityAlwaysColumn();
            entity.Property(seal => seal.PeriodStart).HasColumnName("period_start");
            entity.Property(seal => seal.PeriodEnd).HasColumnName("period_end");
            entity.Property(seal => seal.RowCount).HasColumnName("row_count");
            entity.Property(seal => seal.RowsDigest).HasColumnName("rows_digest").IsRequired();
            entity.Property(seal => seal.PreviousHash).HasColumnName("previous_hash");
            entity.Property(seal => seal.SealHash).HasColumnName("seal_hash").IsRequired();
            entity.Property(seal => seal.Algorithm).HasColumnName("algorithm").HasMaxLength(16).IsRequired();
            entity.Property(seal => seal.KeyId).HasColumnName("key_id").HasMaxLength(32);
            entity.Property(seal => seal.SealedAt).HasColumnName("sealed_at");

            entity.HasIndex(seal => seal.PeriodStart).IsUnique().HasDatabaseName("ux_audit_seals_period_start");
        });
    }
}
