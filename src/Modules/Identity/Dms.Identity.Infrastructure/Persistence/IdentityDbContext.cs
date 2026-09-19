using Dms.Identity.Domain;
using Dms.Infrastructure.Persistence;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Dms.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext : DbContext
{
    public const string Schema = "identity";

    public IdentityDbContext(DbContextOptions<IdentityDbContext> options, DbSession session)
        : base(options) => session.Register(this);

    public DbSet<User> Users => Set<User>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<UserGroupMembership> Memberships => Set<UserGroupMembership>();

    public DbSet<UserSession> Sessions => Set<UserSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(user => user.Username).HasColumnName("username").HasMaxLength(150).IsRequired();
            entity.Property(user => user.NormalizedUsername)
                .HasColumnName("normalized_username").HasMaxLength(150).IsRequired();
            entity.Property(user => user.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
            entity.Property(user => user.Email).HasColumnName("email").HasMaxLength(320);
            entity.Property(user => user.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(320);
            entity.Property(user => user.PasswordHash).HasColumnName("password_hash").HasMaxLength(512);
            entity.Property(user => user.AuthSource).HasColumnName("auth_source")
                .HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(user => user.ExternalId).HasColumnName("external_id").HasMaxLength(256);
            entity.Property(user => user.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(user => user.IsSystemAdmin).HasColumnName("is_system_admin").HasDefaultValue(false);
            entity.Property(user => user.MustChangePassword)
                .HasColumnName("must_change_password").HasDefaultValue(false);
            entity.Property(user => user.ManagerId).HasColumnName("manager_id")
                .HasConversion(id => id!.Value.Value, value => new UserId(value));
            entity.Property(user => user.AccessFailedCount).HasColumnName("access_failed_count").HasDefaultValue(0);
            entity.Property(user => user.LockoutEndsAt).HasColumnName("lockout_ends_at");
            entity.Property(user => user.LastLoginAt).HasColumnName("last_login_at");
            entity.Property(user => user.CreatedAt).HasColumnName("created_at");
            entity.Property(user => user.UpdatedAt).HasColumnName("updated_at");

            // Npgsql maps the system column xmin as a concurrency token: optimistic concurrency
            // without an extra column to maintain.
            entity.Property<uint>("Version").HasColumnName("xmin").IsRowVersion();

            entity.HasIndex(user => user.NormalizedUsername).IsUnique().HasDatabaseName("ux_users_username");
            entity.HasIndex(user => user.NormalizedEmail).IsUnique()
                .HasFilter("normalized_email IS NOT NULL").HasDatabaseName("ux_users_email");
            entity.HasIndex(user => user.ManagerId).HasDatabaseName("ix_users_manager");
            entity.HasOne<User>().WithMany()
                .HasForeignKey(user => user.ManagerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Group>(entity =>
        {
            entity.ToTable("groups");
            entity.HasKey(group => group.Id);
            entity.Property(group => group.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new GroupId(value));
            entity.Property(group => group.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            entity.Property(group => group.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(group => group.Kind).HasColumnName("kind")
                .HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(group => group.ExternalId).HasColumnName("external_id").HasMaxLength(256);
            entity.Property(group => group.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(group => group.CreatedAt).HasColumnName("created_at");
            entity.Property(group => group.UpdatedAt).HasColumnName("updated_at");
            entity.Property<uint>("Version").HasColumnName("xmin").IsRowVersion();
            entity.HasIndex(group => group.Code).IsUnique().HasDatabaseName("ux_groups_code");
        });

        modelBuilder.Entity<UserGroupMembership>(entity =>
        {
            entity.ToTable("user_groups");
            entity.HasKey(membership => new { membership.UserId, membership.GroupId });
            entity.Property(membership => membership.UserId).HasColumnName("user_id")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(membership => membership.GroupId).HasColumnName("group_id")
                .HasConversion(id => id.Value, value => new GroupId(value));
            entity.Property(membership => membership.AddedBy).HasColumnName("added_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(membership => membership.AddedAt).HasColumnName("added_at");
            entity.HasIndex(membership => membership.GroupId).HasDatabaseName("ix_user_groups_group");

            entity.HasOne<User>().WithMany()
                .HasForeignKey(membership => membership.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Group>().WithMany()
                .HasForeignKey(membership => membership.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("user_sessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new SessionId(value));
            entity.Property(session => session.UserId).HasColumnName("user_id")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(session => session.TokenHash).HasColumnName("refresh_token_hash").IsRequired();
            entity.Property(session => session.ExpiresAt).HasColumnName("expires_at");
            entity.Property(session => session.CreatedAt).HasColumnName("created_at");
            entity.Property(session => session.RevokedAt).HasColumnName("revoked_at");
            entity.Property(session => session.ReplacedBySessionId).HasColumnName("replaced_by_session_id")
                .HasConversion(id => id!.Value.Value, value => new SessionId(value));
            entity.Property(session => session.IpAddress).HasColumnName("ip_address").HasMaxLength(64);
            entity.Property(session => session.UserAgent).HasColumnName("user_agent").HasMaxLength(512);

            entity.HasIndex(session => session.TokenHash).IsUnique().HasDatabaseName("ux_user_sessions_token");
            entity.HasIndex(session => session.UserId)
                .HasFilter("revoked_at IS NULL").HasDatabaseName("ix_user_sessions_active");

            entity.HasOne<User>().WithMany()
                .HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
