using Dms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dms.Infrastructure.Jobs;

public sealed class InfraDbContext : DbContext
{
    public InfraDbContext(DbContextOptions<InfraDbContext> options, DbSession session)
        : base(options) => session.Register(this);

    public DbSet<JobRecord> Jobs => Set<JobRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(InfraSchema.Name);

        modelBuilder.Entity<JobRecord>(entity =>
        {
            entity.ToTable("jobs");
            entity.HasKey(job => job.Id);
            entity.Property(job => job.Id).HasColumnName("id");
            entity.Property(job => job.Queue).HasColumnName("queue").HasMaxLength(64).IsRequired();
            entity.Property(job => job.Type).HasColumnName("type").HasMaxLength(128).IsRequired();
            entity.Property(job => job.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            entity.Property(job => job.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
            entity.Property(job => job.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
            entity.Property(job => job.Priority).HasColumnName("priority");
            entity.Property(job => job.RunAfter).HasColumnName("run_after");
            entity.Property(job => job.Attempts).HasColumnName("attempts");
            entity.Property(job => job.MaxAttempts).HasColumnName("max_attempts");
            entity.Property(job => job.LockedBy).HasColumnName("locked_by").HasMaxLength(128);
            entity.Property(job => job.LockedUntil).HasColumnName("locked_until");
            entity.Property(job => job.LastError).HasColumnName("last_error");
            entity.Property(job => job.CreatedAt).HasColumnName("created_at");
            entity.Property(job => job.CompletedAt).HasColumnName("completed_at");

            entity.ToTable(table => table.HasCheckConstraint(
                "ck_jobs_status",
                "status IN ('QUEUED','RUNNING','SUCCEEDED','FAILED','DEAD')"));

            // Dequeue path: only queued rows that are due, highest priority first.
            entity.HasIndex(job => new { job.Queue, job.Priority, job.RunAfter })
                .HasDatabaseName("ix_jobs_dequeue")
                .HasFilter("status = 'QUEUED'");

            // Lease recovery for jobs whose worker died.
            entity.HasIndex(job => job.LockedUntil)
                .HasDatabaseName("ix_jobs_locked_until")
                .HasFilter("status = 'RUNNING'");

            entity.HasIndex(job => job.IdempotencyKey)
                .HasDatabaseName("ux_jobs_idempotency_key")
                .IsUnique()
                .HasFilter("status IN ('QUEUED','RUNNING')");
        });
    }
}

public static class InfraSchema
{
    public const string Name = "infra";
}
