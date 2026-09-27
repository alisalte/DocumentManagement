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

    public DbSet<RecordClass> RecordClasses => Set<RecordClass>();

    public DbSet<RecordSeries> RecordSeries => Set<RecordSeries>();

    public DbSet<ManagedRecord> Records => Set<ManagedRecord>();

    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();

    public DbSet<LegalHold> LegalHolds => Set<LegalHold>();

    public DbSet<Disposition> Dispositions => Set<Disposition>();

    public DbSet<DestructionCertificate> DestructionCertificates => Set<DestructionCertificate>();

    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();

    public DbSet<ImportItem> ImportItems => Set<ImportItem>();

    public DbSet<ImportMapping> ImportMappings => Set<ImportMapping>();

    public DbSet<ImportSourceIndex> ImportSourceIndexes => Set<ImportSourceIndex>();

    public DbSet<ClassificationLevel> ClassificationLevels => Set<ClassificationLevel>();

    public DbSet<DocumentClassification> DocumentClassifications => Set<DocumentClassification>();

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

        modelBuilder.Entity<RecordClass>(entity =>
        {
            entity.ToTable("record_classes");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new RecordClassId(value));
            entity.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            entity.Property(item => item.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(item => item.Description).HasColumnName("description").HasMaxLength(1000);
            entity.Property(item => item.IsActive).HasColumnName("is_active");
            entity.Property(item => item.CreatedBy).HasColumnName("created_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(item => item.Code).IsUnique().HasDatabaseName("ux_record_classes_code");
        });

        modelBuilder.Entity<RecordSeries>(entity =>
        {
            entity.ToTable("record_series");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new RecordSeriesId(value));
            entity.Property(item => item.RecordClassId).HasColumnName("record_class_id")
                .HasConversion(id => id.Value, value => new RecordClassId(value));
            entity.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            entity.Property(item => item.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(item => item.Description).HasColumnName("description").HasMaxLength(1000);
            entity.Property(item => item.IsActive).HasColumnName("is_active");
            entity.Property(item => item.CreatedBy).HasColumnName("created_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne<RecordClass>().WithMany()
                .HasForeignKey(item => item.RecordClassId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_record_series_class");

            entity.HasIndex(item => new { item.RecordClassId, item.Code })
                .IsUnique().HasDatabaseName("ux_record_series_class_code");
        });

        modelBuilder.Entity<ManagedRecord>(entity =>
        {
            entity.ToTable("records", table =>
            {
                table.HasCheckConstraint(
                    "ck_records_status",
                    "status IN ('Active','UnderRetention','Expired','PendingDisposal','Destroyed')");
            });

            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new RecordId(value));
            entity.Property(item => item.DocumentId).HasColumnName("document_id")
                .HasConversion(id => id.Value, value => new DocumentId(value));
            entity.Property(item => item.FinalVersionId).HasColumnName("final_version_id")
                .HasConversion(id => id.Value, value => new DocumentVersionId(value));
            entity.Property(item => item.RecordClassId).HasColumnName("record_class_id")
                .HasConversion(id => id.Value, value => new RecordClassId(value));
            entity.Property(item => item.RecordSeriesId).HasColumnName("record_series_id")
                .HasConversion(id => id!.Value.Value, value => new RecordSeriesId(value));
            entity.Property(item => item.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
            entity.Property(item => item.Status).HasColumnName("status")
                .HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(item => item.DeclaredBy).HasColumnName("declared_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(item => item.DeclaredAt).HasColumnName("declared_at");
            entity.Property(item => item.MetadataFrozenAt).HasColumnName("metadata_frozen_at");
            entity.Property(item => item.UpdatedBy).HasColumnName("updated_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            entity.Property(item => item.LastTransitionReason).HasColumnName("last_transition_reason").HasMaxLength(1000);
            entity.Property(item => item.RetentionPolicyId).HasColumnName("retention_policy_id")
                .HasConversion(id => id!.Value.Value, value => new RetentionPolicyId(value));
            entity.Property(item => item.RetentionPolicyVersion).HasColumnName("retention_policy_version");
            entity.Property(item => item.RetentionPeriodDays).HasColumnName("retention_period_days");
            entity.Property(item => item.RetentionStartEvent).HasColumnName("retention_start_event")
                .HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.RetentionStartedAt).HasColumnName("retention_started_at");
            entity.Property(item => item.RetentionExpiresAt).HasColumnName("retention_expires_at");
            entity.Property(item => item.RetentionExceptionReason).HasColumnName("retention_exception_reason")
                .HasMaxLength(2000);

            entity.HasOne<Document>().WithMany()
                .HasForeignKey(item => item.DocumentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_records_document");
            entity.HasOne<DocumentVersion>().WithMany()
                .HasForeignKey(item => item.FinalVersionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_records_final_version");
            entity.HasOne<RecordClass>().WithMany()
                .HasForeignKey(item => item.RecordClassId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_records_class");
            entity.HasOne<RecordSeries>().WithMany()
                .HasForeignKey(item => item.RecordSeriesId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_records_series");
            entity.HasOne<RetentionPolicy>().WithMany()
                .HasForeignKey(item => item.RetentionPolicyId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_records_retention_policy");

            entity.HasIndex(item => item.DocumentId).IsUnique().HasDatabaseName("ux_records_document");
            entity.HasIndex(item => item.Status).HasDatabaseName("ix_records_status");
            entity.HasIndex(item => item.RecordClassId).HasDatabaseName("ix_records_class");
            entity.HasIndex(item => item.RetentionExpiresAt)
                .HasFilter("retention_expires_at IS NOT NULL")
                .HasDatabaseName("ix_records_retention_expires");
        });

        modelBuilder.Entity<RetentionPolicy>(entity =>
        {
            entity.ToTable("retention_policies", table =>
            {
                table.HasCheckConstraint("ck_retention_policies_days", "retention_period_days > 0");
                table.HasCheckConstraint("ck_retention_policies_version", "version_number > 0");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new RetentionPolicyId(value));
            entity.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            entity.Property(item => item.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(item => item.Description).HasColumnName("description").HasMaxLength(1000);
            entity.Property(item => item.RetentionPeriodDays).HasColumnName("retention_period_days");
            entity.Property(item => item.StartEvent).HasColumnName("start_event")
                .HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(item => item.VersionNumber).HasColumnName("version_number");
            entity.Property(item => item.IsActive).HasColumnName("is_active");
            entity.Property(item => item.CreatedBy).HasColumnName("created_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(item => item.Code).IsUnique().HasDatabaseName("ux_retention_policies_code");
        });

        modelBuilder.Entity<LegalHold>(entity =>
        {
            entity.ToTable("legal_holds");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new LegalHoldId(value));
            entity.Property(item => item.DocumentId).HasColumnName("document_id")
                .HasConversion(id => id.Value, value => new DocumentId(value));
            entity.Property(item => item.Reason).HasColumnName("reason").HasMaxLength(2000).IsRequired();
            entity.Property(item => item.CreatedBy).HasColumnName("created_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.Property(item => item.ReleasedAt).HasColumnName("released_at");
            entity.Property(item => item.ReleasedBy).HasColumnName("released_by")
                .HasConversion(id => id!.Value.Value, value => new UserId(value));
            entity.Property(item => item.ReleaseReason).HasColumnName("release_reason").HasMaxLength(2000);

            entity.HasOne<Document>().WithMany()
                .HasForeignKey(item => item.DocumentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_legal_holds_document");

            entity.HasIndex(item => item.DocumentId).HasDatabaseName("ix_legal_holds_document");
            entity.HasIndex(item => new { item.DocumentId, item.ReleasedAt })
                .HasFilter("released_at IS NULL")
                .HasDatabaseName("ix_legal_holds_active");
        });

        modelBuilder.Entity<Disposition>(entity =>
        {
            entity.ToTable("dispositions", table =>
            {
                table.HasCheckConstraint(
                    "ck_dispositions_status",
                    "status IN ('PendingReview','Approved','Rejected','Destroyed')");
            });

            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new DispositionId(value));
            entity.Property(item => item.RecordId).HasColumnName("record_id")
                .HasConversion(id => id.Value, value => new RecordId(value));
            entity.Property(item => item.DocumentId).HasColumnName("document_id")
                .HasConversion(id => id.Value, value => new DocumentId(value));
            entity.Property(item => item.Status).HasColumnName("status")
                .HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(item => item.RequestReason).HasColumnName("request_reason").HasMaxLength(2000);
            entity.Property(item => item.RequestedBy).HasColumnName("requested_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(item => item.RequestedAt).HasColumnName("requested_at");
            entity.Property(item => item.ReviewedBy).HasColumnName("reviewed_by")
                .HasConversion(id => id!.Value.Value, value => new UserId(value));
            entity.Property(item => item.ReviewedAt).HasColumnName("reviewed_at");
            entity.Property(item => item.DecisionReason).HasColumnName("decision_reason").HasMaxLength(2000);
            entity.Property(item => item.ApprovedBy).HasColumnName("approved_by")
                .HasConversion(id => id!.Value.Value, value => new UserId(value));
            entity.Property(item => item.ApprovedAt).HasColumnName("approved_at");
            entity.Property(item => item.DestroyedBy).HasColumnName("destroyed_by")
                .HasConversion(id => id!.Value.Value, value => new UserId(value));
            entity.Property(item => item.DestroyedAt).HasColumnName("destroyed_at");
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            entity.Property(item => item.RowVersion).HasColumnName("xmin").IsRowVersion();

            entity.HasOne<ManagedRecord>().WithMany()
                .HasForeignKey(item => item.RecordId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_dispositions_record");
            entity.HasOne<Document>().WithMany()
                .HasForeignKey(item => item.DocumentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_dispositions_document");

            // At most one active (PendingReview/Approved) disposition per Record.
            entity.HasIndex(item => item.RecordId)
                .IsUnique()
                .HasFilter("status IN ('PendingReview','Approved')")
                .HasDatabaseName("ux_dispositions_active_record");
            entity.HasIndex(item => item.Status).HasDatabaseName("ix_dispositions_status");
            entity.HasIndex(item => item.DocumentId).HasDatabaseName("ix_dispositions_document");
        });

        modelBuilder.Entity<DestructionCertificate>(entity =>
        {
            entity.ToTable("destruction_certificates", table =>
            {
                table.HasCheckConstraint(
                    "ck_destruction_certificates_sha256",
                    "octet_length(content_sha256) = 32 AND octet_length(certificate_hash) = 32");
            });

            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new DestructionCertificateId(value));
            entity.Property(item => item.CertificateNumber).HasColumnName("certificate_number")
                .HasMaxLength(64).IsRequired();
            entity.Property(item => item.DispositionId).HasColumnName("disposition_id")
                .HasConversion(id => id.Value, value => new DispositionId(value));
            entity.Property(item => item.RecordId).HasColumnName("record_id")
                .HasConversion(id => id.Value, value => new RecordId(value));
            entity.Property(item => item.DocumentId).HasColumnName("document_id")
                .HasConversion(id => id.Value, value => new DocumentId(value));
            entity.Property(item => item.FinalVersionId).HasColumnName("final_version_id")
                .HasConversion(id => id.Value, value => new DocumentVersionId(value));
            entity.Property(item => item.FinalVersionLabel).HasColumnName("final_version_label")
                .HasMaxLength(32).IsRequired();
            entity.Property(item => item.RecordTitle).HasColumnName("record_title").HasMaxLength(500).IsRequired();
            entity.Property(item => item.ContentSha256).HasColumnName("content_sha256").IsRequired();
            entity.Property(item => item.RetentionPolicyId).HasColumnName("retention_policy_id")
                .HasConversion(id => id!.Value.Value, value => new RetentionPolicyId(value));
            entity.Property(item => item.RetentionPolicyVersion).HasColumnName("retention_policy_version");
            entity.Property(item => item.RetentionExpiresAt).HasColumnName("retention_expires_at");
            entity.Property(item => item.LegalHoldCheckedAt).HasColumnName("legal_hold_checked_at");
            entity.Property(item => item.ApprovedBy).HasColumnName("approved_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(item => item.ApprovedAt).HasColumnName("approved_at");
            entity.Property(item => item.DestroyedBy).HasColumnName("destroyed_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(item => item.DestroyedAt).HasColumnName("destroyed_at");
            entity.Property(item => item.Reason).HasColumnName("reason").HasMaxLength(2000);
            entity.Property(item => item.CertificateHash).HasColumnName("certificate_hash").IsRequired();
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");

            entity.HasOne<Disposition>().WithMany()
                .HasForeignKey(item => item.DispositionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_destruction_certificates_disposition");
            entity.HasOne<ManagedRecord>().WithMany()
                .HasForeignKey(item => item.RecordId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_destruction_certificates_record");

            entity.HasIndex(item => item.DispositionId).IsUnique()
                .HasDatabaseName("ux_destruction_certificates_disposition");
            entity.HasIndex(item => item.RecordId).IsUnique()
                .HasDatabaseName("ux_destruction_certificates_record");
            entity.HasIndex(item => item.CertificateNumber).IsUnique()
                .HasDatabaseName("ux_destruction_certificates_number");
        });

        modelBuilder.Entity<ImportJob>(entity =>
        {
            entity.ToTable("import_jobs", table =>
            {
                table.HasCheckConstraint(
                    "ck_import_jobs_status",
                    "status IN ('Created','Validating','ValidationFailed','Ready','Running','Paused','Completed','CompletedWithErrors','Failed')");
                table.HasCheckConstraint(
                    "ck_import_jobs_failure_policy",
                    "failure_policy IN ('ContinueOnError','StopOnError')");
            });
            entity.HasKey(job => job.Id);
            entity.Property(job => job.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new ImportJobId(value));
            entity.Property(job => job.SourceSystem).HasColumnName("source_system").HasMaxLength(128).IsRequired();
            entity.Property(job => job.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(job => job.FilesRoot).HasColumnName("files_root").HasMaxLength(1000);
            entity.Property(job => job.ManifestSha256).HasColumnName("manifest_sha256").HasMaxLength(64).IsRequired();
            entity.Property(job => job.Status).HasColumnName("status")
                .HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(job => job.FailurePolicy).HasColumnName("failure_policy")
                .HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(job => job.CreateMissingCategories).HasColumnName("create_missing_categories");
            entity.Property(job => job.DryRunOnly).HasColumnName("dry_run_only");
            entity.Property(job => job.TotalItems).HasColumnName("total_items");
            entity.Property(job => job.ProcessedItems).HasColumnName("processed_items");
            entity.Property(job => job.SucceededItems).HasColumnName("succeeded_items");
            entity.Property(job => job.FailedItems).HasColumnName("failed_items");
            entity.Property(job => job.SkippedItems).HasColumnName("skipped_items");
            entity.Property(job => job.InvalidItems).HasColumnName("invalid_items");
            entity.Property(job => job.BytesProcessed).HasColumnName("bytes_processed");
            entity.Property(job => job.BytesTotal).HasColumnName("bytes_total");
            entity.Property(job => job.LastError).HasColumnName("last_error").HasMaxLength(2000);
            entity.Property(job => job.CreatedBy).HasColumnName("created_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(job => job.CreatedAt).HasColumnName("created_at");
            entity.Property(job => job.UpdatedAt).HasColumnName("updated_at");
            entity.Property(job => job.StartedAt).HasColumnName("started_at");
            entity.Property(job => job.CompletedAt).HasColumnName("completed_at");
            entity.Property(job => job.RowVersion).HasColumnName("xmin").IsRowVersion();

            entity.HasIndex(job => job.Status).HasDatabaseName("ix_import_jobs_status");
            entity.HasIndex(job => job.CreatedAt).HasDatabaseName("ix_import_jobs_created_at");
            entity.HasIndex(job => new { job.SourceSystem, job.CreatedAt })
                .HasDatabaseName("ix_import_jobs_source_created");
        });

        modelBuilder.Entity<ImportItem>(entity =>
        {
            entity.ToTable("import_items", table =>
            {
                table.HasCheckConstraint(
                    "ck_import_items_status",
                    "status IN ('Pending','Valid','Invalid','Ready','Running','Succeeded','Failed','Skipped','Retryable')");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new ImportItemId(value));
            entity.Property(item => item.ImportJobId).HasColumnName("import_job_id")
                .HasConversion(id => id.Value, value => new ImportJobId(value));
            entity.Property(item => item.SourceId).HasColumnName("source_id").HasMaxLength(256).IsRequired();
            entity.Property(item => item.SourcePath).HasColumnName("source_path").HasMaxLength(1000).IsRequired();
            entity.Property(item => item.Title).HasColumnName("title").HasMaxLength(500);
            entity.Property(item => item.EntryJson).HasColumnName("entry_json").HasColumnType("jsonb").IsRequired();
            entity.Property(item => item.Status).HasColumnName("status")
                .HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(item => item.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
            entity.Property(item => item.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
            entity.Property(item => item.ExpectedSha256).HasColumnName("expected_sha256").HasMaxLength(64);
            entity.Property(item => item.ActualSha256).HasColumnName("actual_sha256").HasMaxLength(64);
            entity.Property(item => item.ExpectedSize).HasColumnName("expected_size");
            entity.Property(item => item.ActualSize).HasColumnName("actual_size");
            entity.Property(item => item.TargetDocumentId).HasColumnName("target_document_id")
                .HasConversion(id => id!.Value.Value, value => new DocumentId(value));
            entity.Property(item => item.TargetRecordId).HasColumnName("target_record_id")
                .HasConversion(id => id!.Value.Value, value => new RecordId(value));
            entity.Property(item => item.TargetVersionId).HasColumnName("target_version_id")
                .HasConversion(id => id!.Value.Value, value => new DocumentVersionId(value));
            entity.Property(item => item.RetryCount).HasColumnName("retry_count");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            entity.Property(item => item.StartedAt).HasColumnName("started_at");
            entity.Property(item => item.CompletedAt).HasColumnName("completed_at");
            entity.Property(item => item.RowVersion).HasColumnName("xmin").IsRowVersion();

            entity.HasOne<ImportJob>().WithMany()
                .HasForeignKey(item => item.ImportJobId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_import_items_job");

            entity.HasIndex(item => new { item.ImportJobId, item.SourceId })
                .IsUnique().HasDatabaseName("ux_import_items_job_source");
            entity.HasIndex(item => new { item.ImportJobId, item.Status })
                .HasDatabaseName("ix_import_items_job_status");
            entity.HasIndex(item => item.Status)
                .HasFilter("status IN ('Ready','Retryable','Running')")
                .HasDatabaseName("ix_import_items_claimable");
        });

        modelBuilder.Entity<ImportMapping>(entity =>
        {
            entity.ToTable("import_mappings");
            entity.HasKey(mapping => mapping.Id);
            entity.Property(mapping => mapping.Id).HasColumnName("id");
            entity.Property(mapping => mapping.ImportJobId).HasColumnName("import_job_id")
                .HasConversion(id => id!.Value.Value, value => new ImportJobId(value));
            entity.Property(mapping => mapping.SourceSystem).HasColumnName("source_system").HasMaxLength(128).IsRequired();
            entity.Property(mapping => mapping.Kind).HasColumnName("kind")
                .HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(mapping => mapping.SourceKey).HasColumnName("source_key").HasMaxLength(256).IsRequired();
            entity.Property(mapping => mapping.TargetKey).HasColumnName("target_key").HasMaxLength(256).IsRequired();
            entity.Property(mapping => mapping.CreatedBy).HasColumnName("created_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(mapping => mapping.CreatedAt).HasColumnName("created_at");

            entity.HasOne<ImportJob>().WithMany()
                .HasForeignKey(mapping => mapping.ImportJobId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_import_mappings_job");

            entity.HasIndex(mapping => new { mapping.SourceSystem, mapping.ImportJobId, mapping.Kind, mapping.SourceKey })
                .IsUnique()
                .AreNullsDistinct(false)
                .HasDatabaseName("ux_import_mappings_lookup");
        });

        modelBuilder.Entity<ImportSourceIndex>(entity =>
        {
            entity.ToTable("import_source_index");
            entity.HasKey(row => new { row.SourceSystem, row.SourceId });
            entity.Property(row => row.SourceSystem).HasColumnName("source_system").HasMaxLength(128);
            entity.Property(row => row.SourceId).HasColumnName("source_id").HasMaxLength(256);
            entity.Property(row => row.DocumentId).HasColumnName("document_id")
                .HasConversion(id => id.Value, value => new DocumentId(value));
            entity.Property(row => row.ImportJobId).HasColumnName("import_job_id")
                .HasConversion(id => id.Value, value => new ImportJobId(value));
            entity.Property(row => row.ImportItemId).HasColumnName("import_item_id")
                .HasConversion(id => id.Value, value => new ImportItemId(value));
            entity.Property(row => row.ImportedAt).HasColumnName("imported_at");

            entity.HasIndex(row => row.DocumentId).HasDatabaseName("ix_import_source_index_document");
        });

        modelBuilder.Entity<ClassificationLevel>(entity =>
        {
            entity.ToTable("classification_levels");
            entity.HasKey(level => level.Id);
            entity.Property(level => level.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new ClassificationLevelId(value));
            entity.Property(level => level.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            entity.Property(level => level.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(level => level.Rank).HasColumnName("rank");
            entity.Property(level => level.IsActive).HasColumnName("is_active");
            entity.Property(level => level.CreatedBy).HasColumnName("created_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(level => level.CreatedAt).HasColumnName("created_at");
            entity.Property(level => level.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(level => level.Code).IsUnique().HasDatabaseName("ux_classification_levels_code");
        });

        modelBuilder.Entity<DocumentClassification>(entity =>
        {
            entity.ToTable("document_classifications");
            entity.HasKey(row => row.DocumentId);
            entity.Property(row => row.DocumentId).HasColumnName("document_id")
                .HasConversion(id => id.Value, value => new DocumentId(value));
            entity.Property(row => row.LevelId).HasColumnName("level_id")
                .HasConversion(id => id.Value, value => new ClassificationLevelId(value));
            entity.Property(row => row.AssignedBy).HasColumnName("assigned_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(row => row.AssignedAt).HasColumnName("assigned_at");

            entity.HasOne<Document>().WithMany()
                .HasForeignKey(row => row.DocumentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_document_classifications_document");
            entity.HasOne<ClassificationLevel>().WithMany()
                .HasForeignKey(row => row.LevelId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_document_classifications_level");
            entity.HasIndex(row => row.LevelId).HasDatabaseName("ix_document_classifications_level");
        });
    }
}
