using Dms.Infrastructure.Persistence;
using Dms.SharedKernel;
using Dms.Sharing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Dms.Sharing.Infrastructure.Persistence;

public sealed class SharingDbContext : DbContext
{
    public const string Schema = "sharing";

    public SharingDbContext(DbContextOptions<SharingDbContext> options, DbSession session)
        : base(options) => session.Register(this);

    public DbSet<DocumentShare> Shares => Set<DocumentShare>();

    public DbSet<ShareLink> Links => Set<ShareLink>();

    public DbSet<ShareLinkSession> LinkSessions => Set<ShareLinkSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<DocumentShare>(entity =>
        {
            entity.ToTable("document_shares", table =>
            {
                table.HasCheckConstraint("ck_document_shares_view", "(permissions & 1) = 1");
                table.HasCheckConstraint("ck_document_shares_permissions", "permissions BETWEEN 1 AND 7");
                table.HasCheckConstraint("ck_document_shares_not_self", "shared_by <> shared_with_user_id");
            });

            entity.HasKey(share => share.Id);
            entity.Property(share => share.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new ShareId(value));
            entity.Property(share => share.DocumentId).HasColumnName("document_id")
                .HasConversion(id => id.Value, value => new DocumentId(value));
            entity.Property(share => share.VersionId).HasColumnName("version_id");
            entity.Property(share => share.SharedBy).HasColumnName("shared_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(share => share.SharedWith).HasColumnName("shared_with_user_id")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(share => share.Permissions).HasColumnName("permissions").HasConversion<short>();
            entity.Property(share => share.ExpiresAt).HasColumnName("expires_at");
            entity.Property(share => share.Message).HasColumnName("message").HasMaxLength(DocumentShare.MaxMessageLength);
            entity.Property(share => share.CreatedAt).HasColumnName("created_at");
            entity.Property(share => share.RevokedAt).HasColumnName("revoked_at");
            entity.Property(share => share.RevokedBy).HasColumnName("revoked_by")
                .HasConversion(id => id!.Value.Value, value => new UserId(value));
            entity.Property<uint>("RowVersion").HasColumnName("xmin").IsRowVersion();

            // One live share per version and person; sharing again revokes the old row first.
            entity.HasIndex(share => new { share.VersionId, share.SharedWith })
                .IsUnique()
                .HasFilter("revoked_at IS NULL")
                .HasDatabaseName("ux_document_shares_version_recipient");

            // The permission evaluator asks "what did this user receive on this document?".
            entity.HasIndex(share => new { share.SharedWith, share.DocumentId })
                .HasFilter("revoked_at IS NULL")
                .HasDatabaseName("ix_document_shares_recipient");
            entity.HasIndex(share => share.DocumentId).HasDatabaseName("ix_document_shares_document");
        });

        modelBuilder.Entity<ShareLink>(entity =>
        {
            entity.ToTable("share_links", table =>
            {
                table.HasCheckConstraint("ck_share_links_view", "(permissions & 1) = 1");
                table.HasCheckConstraint("ck_share_links_permissions", "permissions BETWEEN 1 AND 7");
                table.HasCheckConstraint("ck_share_links_max_count", "max_access_count IS NULL OR max_access_count > 0");
                table.HasCheckConstraint("ck_share_links_count", "access_count <= coalesce(max_access_count, access_count)");
                table.HasCheckConstraint("ck_share_links_expiry", "expires_at > created_at");
            });

            entity.HasKey(link => link.Id);
            entity.Property(link => link.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new ShareLinkId(value));
            entity.Property(link => link.DocumentId).HasColumnName("document_id")
                .HasConversion(id => id.Value, value => new DocumentId(value));
            entity.Property(link => link.VersionId).HasColumnName("version_id");
            entity.Property(link => link.TokenHash).HasColumnName("token_hash").IsRequired();
            entity.Property(link => link.TokenPrefix).HasColumnName("token_prefix").HasMaxLength(ShareLink.PrefixLength).IsRequired();
            entity.Property(link => link.Permissions).HasColumnName("permissions").HasConversion<short>();
            entity.Property(link => link.PasswordHash).HasColumnName("password_hash").HasMaxLength(200);
            entity.Property(link => link.ExpiresAt).HasColumnName("expires_at");
            entity.Property(link => link.MaxAccessCount).HasColumnName("max_access_count");
            entity.Property(link => link.AccessCount).HasColumnName("access_count");
            entity.Property(link => link.FailedAttempts).HasColumnName("failed_attempts");
            entity.Property(link => link.LockedUntil).HasColumnName("locked_until");
            entity.Property(link => link.Label).HasColumnName("label").HasMaxLength(ShareLink.MaxLabelLength);
            entity.Property(link => link.CreatedBy).HasColumnName("created_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(link => link.CreatedAt).HasColumnName("created_at");
            entity.Property(link => link.RevokedAt).HasColumnName("revoked_at");
            entity.Property(link => link.RevokedBy).HasColumnName("revoked_by")
                .HasConversion(id => id!.Value.Value, value => new UserId(value));
            entity.Property(link => link.LastAccessedAt).HasColumnName("last_accessed_at");
            entity.Ignore(link => link.RequiresPassword);
            entity.Property<uint>("RowVersion").HasColumnName("xmin").IsRowVersion();

            entity.HasIndex(link => link.TokenHash).IsUnique().HasDatabaseName("ux_share_links_token_hash");
            entity.HasIndex(link => link.DocumentId).HasDatabaseName("ix_share_links_document");
        });

        modelBuilder.Entity<ShareLinkSession>(entity =>
        {
            entity.ToTable("share_link_sessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new ShareLinkSessionId(value));
            entity.Property(session => session.LinkId).HasColumnName("link_id")
                .HasConversion(id => id.Value, value => new ShareLinkId(value));
            entity.Property(session => session.TokenHash).HasColumnName("token_hash").IsRequired();
            entity.Property(session => session.CreatedAt).HasColumnName("created_at");
            entity.Property(session => session.ExpiresAt).HasColumnName("expires_at");
            entity.Property(session => session.PrintStartedAt).HasColumnName("print_started_at");

            entity.HasOne<ShareLink>().WithMany()
                .HasForeignKey(session => session.LinkId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_share_link_sessions_link");
            entity.HasIndex(session => session.TokenHash).IsUnique().HasDatabaseName("ux_share_link_sessions_token_hash");
            entity.HasIndex(session => session.LinkId).HasDatabaseName("ix_share_link_sessions_link");
            entity.HasIndex(session => session.ExpiresAt).HasDatabaseName("ix_share_link_sessions_expires_at");
        });
    }
}
