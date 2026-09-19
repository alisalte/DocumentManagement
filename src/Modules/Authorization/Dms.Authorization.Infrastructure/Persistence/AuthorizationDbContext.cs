using Dms.Authorization.Domain;
using Dms.Infrastructure.Persistence;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Dms.Authorization.Infrastructure.Persistence;

/// <summary>Database mirror of the code-defined permission catalog, for referential integrity.</summary>
public sealed class PermissionRecord
{
    public string Code { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public bool RequiresView { get; set; }

    public string Description { get; set; } = string.Empty;
}

public sealed class AuthorizationDbContext : DbContext
{
    public const string Schema = "authz";

    public AuthorizationDbContext(DbContextOptions<AuthorizationDbContext> options, DbSession session)
        : base(options) => session.Register(this);

    public DbSet<PermissionRecord> Permissions => Set<PermissionRecord>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<UserRoleAssignment> UserRoles => Set<UserRoleAssignment>();

    public DbSet<ResourcePermissionEntry> ResourcePermissions => Set<ResourcePermissionEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<PermissionRecord>(entity =>
        {
            entity.ToTable("permissions");
            entity.HasKey(permission => permission.Code);
            entity.Property(permission => permission.Code).HasColumnName("code").HasMaxLength(64);
            entity.Property(permission => permission.Scope).HasColumnName("scope").HasMaxLength(16).IsRequired();
            entity.Property(permission => permission.RequiresView).HasColumnName("requires_view");
            entity.Property(permission => permission.Description)
                .HasColumnName("description").HasMaxLength(400).IsRequired();
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(role => role.Id);
            entity.Property(role => role.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new RoleId(value));
            entity.Property(role => role.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            entity.Property(role => role.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(role => role.Description).HasColumnName("description").HasMaxLength(400);
            entity.Property(role => role.IsSystem).HasColumnName("is_system");
            entity.Property(role => role.CreatedAt).HasColumnName("created_at");
            entity.Property<uint>("Version").HasColumnName("xmin").IsRowVersion();
            entity.HasIndex(role => role.Code).IsUnique().HasDatabaseName("ux_roles_code");

            entity.OwnsMany(role => role.Permissions, permission =>
            {
                permission.ToTable("role_permissions");
                permission.WithOwner().HasForeignKey(rolePermission => rolePermission.RoleId);
                permission.HasKey(rolePermission => new { rolePermission.RoleId, rolePermission.PermissionCode });
                permission.Property(rolePermission => rolePermission.RoleId).HasColumnName("role_id")
                    .HasConversion(id => id.Value, value => new RoleId(value));
                permission.Property(rolePermission => rolePermission.PermissionCode)
                    .HasColumnName("permission_code").HasMaxLength(64);
                permission.HasOne<PermissionRecord>().WithMany()
                    .HasForeignKey(rolePermission => rolePermission.PermissionCode)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        });

        modelBuilder.Entity<UserRoleAssignment>(entity =>
        {
            entity.ToTable("user_roles");
            entity.HasKey(assignment => new { assignment.UserId, assignment.RoleId });
            entity.Property(assignment => assignment.UserId).HasColumnName("user_id")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(assignment => assignment.RoleId).HasColumnName("role_id")
                .HasConversion(id => id.Value, value => new RoleId(value));
            entity.Property(assignment => assignment.GrantedBy).HasColumnName("granted_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(assignment => assignment.GrantedAt).HasColumnName("granted_at");
            entity.HasIndex(assignment => assignment.RoleId).HasDatabaseName("ix_user_roles_role");
            entity.HasOne<Role>().WithMany()
                .HasForeignKey(assignment => assignment.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ResourcePermissionEntry>(entity =>
        {
            entity.ToTable("resource_permissions");
            entity.HasKey(acl => acl.Id);
            entity.Property(acl => acl.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new AclEntryId(value));
            entity.Property(acl => acl.ResourceType).HasColumnName("resource_type")
                .HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(acl => acl.ResourceId).HasColumnName("resource_id");
            entity.Property(acl => acl.SubjectType).HasColumnName("subject_type")
                .HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(acl => acl.SubjectId).HasColumnName("subject_id");
            entity.Property(acl => acl.PermissionCode).HasColumnName("permission_code").HasMaxLength(64).IsRequired();
            entity.Property(acl => acl.Effect).HasColumnName("effect")
                .HasConversion<string>().HasMaxLength(8).IsRequired();
            entity.Property(acl => acl.Inherit).HasColumnName("inherit");
            entity.Property(acl => acl.Reason).HasColumnName("reason").HasMaxLength(400);
            entity.Property(acl => acl.CreatedBy).HasColumnName("created_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(acl => acl.CreatedAt).HasColumnName("created_at");

            // One effect per (resource, subject, permission): changing it is a revoke plus a grant,
            // so an ALLOW and a DENY for the same tuple can never coexist.
            entity.HasIndex(acl => new
                {
                    acl.ResourceType,
                    acl.ResourceId,
                    acl.SubjectType,
                    acl.SubjectId,
                    acl.PermissionCode,
                })
                .IsUnique()
                .HasDatabaseName("ux_resource_permissions_tuple");

            entity.HasIndex(acl => new { acl.ResourceType, acl.ResourceId })
                .HasDatabaseName("ix_resource_permissions_resource");
            entity.HasIndex(acl => new { acl.SubjectType, acl.SubjectId })
                .HasDatabaseName("ix_resource_permissions_subject");

            entity.HasOne<PermissionRecord>().WithMany()
                .HasForeignKey(acl => acl.PermissionCode).OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(table => table.HasCheckConstraint(
                "ck_resource_permissions_inherit",
                "inherit = false OR resource_type = 'Category'"));
        });
    }
}
