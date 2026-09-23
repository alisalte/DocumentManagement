using Dms.DocumentTypes.Contracts;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.Infrastructure.Persistence;
using Dms.SharedKernel;
using Dms.Storage.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Dms.Documents.Infrastructure.Persistence;

public sealed class DocumentsDbContext : DbContext
{
    public const string Schema = "documents";

    public DocumentsDbContext(DbContextOptions<DocumentsDbContext> options, DbSession session)
        : base(options) => session.Register(this);

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasPostgresExtension("ltree");
        modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("categories", table =>
            {
                table.HasCheckConstraint("ck_categories_not_own_parent", "parent_id <> id");
                table.HasCheckConstraint("ck_categories_depth", $"depth BETWEEN 0 AND {Category.MaxDepth}");
            });

            entity.HasKey(category => category.Id);
            entity.Property(category => category.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new CategoryId(value));
            entity.Property(category => category.ParentId).HasColumnName("parent_id")
                .HasConversion(id => id!.Value.Value, value => new CategoryId(value));
            entity.Property(category => category.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(category => category.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            entity.Property(category => category.Description).HasColumnName("description").HasMaxLength(1000);
            entity.Property(category => category.Path).HasColumnName("path").HasColumnType("ltree").IsRequired();
            entity.Property(category => category.Depth).HasColumnName("depth");
            entity.Property(category => category.IsActive).HasColumnName("is_active");
            entity.Property(category => category.SortOrder).HasColumnName("sort_order");
            entity.Property(category => category.CreatedBy).HasColumnName("created_by")
                .HasConversion(id => id!.Value.Value, value => new UserId(value));
            entity.Property(category => category.CreatedAt).HasColumnName("created_at");
            entity.Property(category => category.UpdatedAt).HasColumnName("updated_at");
            entity.Property<uint>("Version").HasColumnName("xmin").IsRowVersion();

            entity.HasOne<Category>().WithMany()
                .HasForeignKey(category => category.ParentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_categories_parent");

            // Sibling names and codes are unique, roots included (NULLS NOT DISTINCT).
            entity.HasIndex(category => new { category.ParentId, category.Code })
                .IsUnique().AreNullsDistinct(false).HasDatabaseName("ux_categories_parent_code");
            entity.HasIndex(category => new { category.ParentId, category.Name })
                .IsUnique().AreNullsDistinct(false).HasDatabaseName("ux_categories_parent_name");
            entity.HasIndex(category => category.Path).HasMethod("gist").HasDatabaseName("ix_categories_path");
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.ToTable("documents", table =>
            {
                table.HasCheckConstraint(
                    "ck_documents_deleted_consistent",
                    "(deleted_at IS NULL) = (deleted_by IS NULL)");
                table.HasCheckConstraint("ck_documents_latest_version", "latest_version_number >= 0");
            });

            entity.HasKey(document => document.Id);
            entity.Property(document => document.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new DocumentId(value));
            entity.Property(document => document.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
            entity.Property(document => document.Description).HasColumnName("description").HasMaxLength(4000);
            entity.Property(document => document.DocumentTypeId).HasColumnName("document_type_id")
                .HasConversion(id => id.Value, value => new DocumentTypeId(value));
            entity.Property(document => document.CategoryId).HasColumnName("category_id")
                .HasConversion(id => id.Value, value => new CategoryId(value));
            entity.Property(document => document.OwnerId).HasColumnName("owner_id")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(document => document.CurrentVersionId).HasColumnName("current_version_id")
                .HasConversion(id => id!.Value.Value, value => new DocumentVersionId(value));
            entity.Property(document => document.EffectiveVersionId).HasColumnName("effective_version_id")
                .HasConversion(id => id!.Value.Value, value => new DocumentVersionId(value));
            entity.Property(document => document.LatestVersionNumber).HasColumnName("latest_version_number");
            entity.Property(document => document.Status).HasColumnName("status")
                .HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(document => document.DeletedAt).HasColumnName("deleted_at");
            entity.Property(document => document.DeletedBy).HasColumnName("deleted_by")
                .HasConversion(id => id!.Value.Value, value => new UserId(value));
            entity.Property(document => document.DeleteReason).HasColumnName("delete_reason").HasMaxLength(1000);
            entity.Property(document => document.CreatedBy).HasColumnName("created_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(document => document.CreatedAt).HasColumnName("created_at");
            entity.Property(document => document.UpdatedBy).HasColumnName("updated_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(document => document.UpdatedAt).HasColumnName("updated_at");
            entity.Property<uint>("Version").HasColumnName("xmin").IsRowVersion();

            entity.HasOne<Category>().WithMany()
                .HasForeignKey(document => document.CategoryId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_documents_category");

            entity.HasMany(document => document.Versions).WithOne()
                .HasForeignKey(version => version.DocumentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_document_versions_document");
            entity.Navigation(document => document.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.HasMany(document => document.Tags).WithOne()
                .HasForeignKey(tag => tag.DocumentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_document_tags_document");
            entity.Navigation(document => document.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);

            // Section 7.7: deleted documents disappear from every normal query. Restore, purge
            // and the recycle bin opt out explicitly with IgnoreQueryFilters.
            entity.HasQueryFilter(document => document.DeletedAt == null);

            entity.HasIndex(document => document.CategoryId)
                .HasFilter("deleted_at IS NULL").HasDatabaseName("ix_documents_category");
            entity.HasIndex(document => document.DocumentTypeId)
                .HasFilter("deleted_at IS NULL").HasDatabaseName("ix_documents_type");
            entity.HasIndex(document => document.OwnerId)
                .HasFilter("deleted_at IS NULL").HasDatabaseName("ix_documents_owner");
            entity.HasIndex(document => document.UpdatedAt)
                .HasFilter("deleted_at IS NULL").HasDatabaseName("ix_documents_updated_at");
            entity.HasIndex(document => document.DeletedAt)
                .HasFilter("deleted_at IS NOT NULL").HasDatabaseName("ix_documents_trash");

            // Substring search on titles ("قرارداد" anywhere in the title) without a full scan.
            entity.HasIndex(document => document.Title).HasMethod("gin")
                .HasOperators("gin_trgm_ops").HasDatabaseName("ix_documents_title_trgm");
        });

        modelBuilder.Entity<DocumentVersion>(entity =>
        {
            entity.ToTable("document_versions", table =>
            {
                table.HasCheckConstraint("ck_document_versions_number", "version_number > 0 AND revision_number > 0");
                table.HasCheckConstraint("ck_document_versions_size", "file_size >= 0");
                table.HasCheckConstraint("ck_document_versions_sha256", "octet_length(sha256) = 32");
            });

            entity.HasKey(version => version.Id);
            entity.Property(version => version.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new DocumentVersionId(value));
            entity.Property(version => version.DocumentId).HasColumnName("document_id")
                .HasConversion(id => id.Value, value => new DocumentId(value));
            entity.Property(version => version.VersionNumber).HasColumnName("version_number");
            entity.Property(version => version.RevisionNumber).HasColumnName("revision_number");
            entity.Property(version => version.StorageObjectId).HasColumnName("storage_object_id")
                .HasConversion(id => id.Value, value => new StorageObjectId(value));
            entity.Property(version => version.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
            entity.Property(version => version.MimeType).HasColumnName("mime_type").HasMaxLength(255).IsRequired();
            entity.Property(version => version.FileSize).HasColumnName("file_size");
            entity.Property(version => version.Sha256).HasColumnName("sha256").IsRequired();
            entity.Property(version => version.DocumentTypeVersionId).HasColumnName("document_type_version_id")
                .HasConversion(id => id.Value, value => new DocumentTypeVersionId(value));
            entity.Property(version => version.DynamicData).HasColumnName("dynamic_data")
                .HasColumnType("jsonb").IsRequired();
            entity.Property(version => version.ChangeKind).HasColumnName("change_kind")
                .HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(version => version.ChangeDescription).HasColumnName("change_description").HasMaxLength(2000);
            entity.Property(version => version.ApprovalStatus).HasColumnName("approval_status")
                .HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(version => version.ApprovedAt).HasColumnName("approved_at");
            entity.Property(version => version.CreatedBy).HasColumnName("created_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(version => version.CreatedAt).HasColumnName("created_at");

            // ADR 0001: one row per (file, metadata) pair, numbered V{version}.{revision}.
            entity.HasIndex(version => new { version.DocumentId, version.VersionNumber, version.RevisionNumber })
                .IsUnique().HasDatabaseName("ux_document_versions_number");
            entity.HasIndex(version => version.StorageObjectId).HasDatabaseName("ix_document_versions_storage_object");
            entity.HasIndex(version => version.Sha256).HasDatabaseName("ix_document_versions_sha256");
            entity.HasIndex(version => version.DynamicData).HasMethod("gin")
                .HasOperators("jsonb_path_ops").HasDatabaseName("ix_document_versions_dynamic_data");
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.ToTable("tags");
            entity.HasKey(tag => tag.Id);
            entity.Property(tag => tag.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new TagId(value));
            entity.Property(tag => tag.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
            entity.Property(tag => tag.NormalizedName).HasColumnName("normalized_name").HasMaxLength(64).IsRequired();
            entity.Property(tag => tag.CreatedBy).HasColumnName("created_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(tag => tag.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(tag => tag.NormalizedName).IsUnique().HasDatabaseName("ux_tags_normalized_name");
        });

        modelBuilder.Entity<DocumentTag>(entity =>
        {
            entity.ToTable("document_tags");
            entity.HasKey(link => new { link.DocumentId, link.TagId });
            entity.Property(link => link.DocumentId).HasColumnName("document_id")
                .HasConversion(id => id.Value, value => new DocumentId(value));
            entity.Property(link => link.TagId).HasColumnName("tag_id")
                .HasConversion(id => id.Value, value => new TagId(value));
            entity.Property(link => link.AddedBy).HasColumnName("added_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(link => link.AddedAt).HasColumnName("added_at");

            entity.HasOne<Tag>().WithMany()
                .HasForeignKey(link => link.TagId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_document_tags_tag");
            entity.HasIndex(link => link.TagId).HasDatabaseName("ix_document_tags_tag");
        });
    }
}
