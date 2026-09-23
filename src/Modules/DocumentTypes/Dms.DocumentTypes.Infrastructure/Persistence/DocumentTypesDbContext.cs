using System.Text.Json;
using Dms.DocumentTypes.Application;
using Dms.DocumentTypes.Contracts;
using Dms.DocumentTypes.Domain;
using Dms.Infrastructure.Persistence;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Dms.DocumentTypes.Infrastructure;

public sealed class DocumentTypesDbContext : DbContext
{
    public const string Schema = "doctypes";

    public DocumentTypesDbContext(DbContextOptions<DocumentTypesDbContext> options, DbSession session)
        : base(options) => session.Register(this);

    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();

    public DbSet<DocumentTypeVersion> DocumentTypeVersions => Set<DocumentTypeVersion>();

    public DbSet<FieldDefinition> FieldDefinitions => Set<FieldDefinition>();

    public DbSet<FieldRule> FieldRules => Set<FieldRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<DocumentType>(entity =>
        {
            entity.ToTable("document_types");
            entity.HasKey(type => type.Id);
            entity.Property(type => type.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new DocumentTypeId(value));
            entity.Property(type => type.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            entity.Property(type => type.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(type => type.Description).HasColumnName("description").HasMaxLength(1000);
            entity.Property(type => type.DefaultCategoryId).HasColumnName("default_category_id");
            entity.Property(type => type.IsActive).HasColumnName("is_active");
            entity.Property(type => type.LatestPublishedVersionId).HasColumnName("latest_published_version_id")
                .HasConversion(id => id!.Value.Value, value => new DocumentTypeVersionId(value));
            entity.Property(type => type.CreatedAt).HasColumnName("created_at");
            entity.Property(type => type.UpdatedAt).HasColumnName("updated_at");
            entity.Property<uint>("Version").HasColumnName("xmin").IsRowVersion();

            // Settings are a small, whole-object value: JSONB keeps them versionable without a
            // migration for every new switch.
            entity.Property(type => type.Settings)
                .HasColumnName("settings")
                .HasColumnType("jsonb")
                .HasConversion(Json.Converter<DocumentTypeSettings>(), Json.Comparer<DocumentTypeSettings>())
                .IsRequired();

            entity.HasIndex(type => type.Code).IsUnique().HasDatabaseName("ux_document_types_code");

            entity.HasMany(type => type.Versions)
                .WithOne()
                .HasForeignKey(version => version.DocumentTypeId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(type => type.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<DocumentTypeVersion>(entity =>
        {
            entity.ToTable("document_type_versions");
            entity.HasKey(version => version.Id);
            entity.Property(version => version.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new DocumentTypeVersionId(value));
            entity.Property(version => version.DocumentTypeId).HasColumnName("document_type_id")
                .HasConversion(id => id.Value, value => new DocumentTypeId(value));
            entity.Property(version => version.VersionNumber).HasColumnName("version_number");
            entity.Property(version => version.Status).HasColumnName("status")
                .HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(version => version.CreatedAt).HasColumnName("created_at");
            entity.Property(version => version.PublishedAt).HasColumnName("published_at");
            entity.Property(version => version.PublishedBy).HasColumnName("published_by")
                .HasConversion(id => id!.Value.Value, value => new UserId(value));

            entity.HasIndex(version => new { version.DocumentTypeId, version.VersionNumber })
                .IsUnique().HasDatabaseName("ux_document_type_versions_number");

            // At most one draft per type, enforced by the database rather than by convention.
            entity.HasIndex(version => version.DocumentTypeId)
                .IsUnique()
                .HasFilter("status = 'Draft'")
                .HasDatabaseName("ux_document_type_versions_single_draft");

            // CASCADE only reaches draft children in practice: the trigger refuses to delete
            // anything under a published version (section 4.1).
            entity.HasMany(version => version.Fields).WithOne()
                .HasForeignKey(field => field.TypeVersionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_field_definitions_version");
            entity.Navigation(version => version.Fields).UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.HasMany(version => version.Rules).WithOne()
                .HasForeignKey(rule => rule.TypeVersionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_field_rules_version");
            entity.Navigation(version => version.Rules).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        // Child ids are generated by the domain (UUIDv7). ValueGeneratedNever tells EF that a new
        // child with an id already set is an insert, not an update of an existing row.
        modelBuilder.Entity<FieldDefinition>(entity =>
        {
            entity.ToTable("field_definitions", table =>
                table.HasCheckConstraint("ck_field_definitions_code", "code ~ '^[a-z][a-z0-9_]{0,62}$'"));

            entity.HasKey(field => field.Id);
            entity.Property(field => field.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(field => field.TypeVersionId).HasColumnName("type_version_id")
                .HasConversion(id => id.Value, value => new DocumentTypeVersionId(value));
            entity.Property(field => field.Code).HasColumnName("code").HasMaxLength(63).IsRequired();
            entity.Property(field => field.Label).HasColumnName("label").HasColumnType("jsonb")
                .HasConversion(Json.Converter<LocalizedText>(), Json.Comparer<LocalizedText>()).IsRequired();
            entity.Property(field => field.FieldType).HasColumnName("field_type")
                .HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(field => field.IsRequired).HasColumnName("is_required");
            entity.Property(field => field.IsSearchable).HasColumnName("is_searchable");
            entity.Property(field => field.IsSortable).HasColumnName("is_sortable");
            entity.Property(field => field.ShowInList).HasColumnName("show_in_list");
            entity.Property(field => field.IsApprovalRelevant).HasColumnName("is_approval_relevant");
            entity.Property(field => field.DefaultValue).HasColumnName("default_value").HasColumnType("jsonb");
            entity.Property(field => field.Validation).HasColumnName("validation").HasColumnType("jsonb")
                .HasConversion(Json.Converter<FieldValidation>(), Json.Comparer<FieldValidation>()).IsRequired();
            entity.Property(field => field.HelpText).HasColumnName("help_text").HasColumnType("jsonb")
                .HasConversion(Json.NullableConverter<LocalizedText>(), Json.NullableComparer<LocalizedText>());
            entity.Property(field => field.DisplayOrder).HasColumnName("display_order");
            entity.Property(field => field.IsActive).HasColumnName("is_active");

            entity.HasIndex(field => new { field.TypeVersionId, field.Code })
                .IsUnique().HasDatabaseName("ux_field_definitions_code");

            entity.HasMany(field => field.Options).WithOne()
                .HasForeignKey(option => option.FieldDefinitionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_field_options_field");
            entity.Navigation(field => field.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<FieldOption>(entity =>
        {
            entity.ToTable("field_options");
            entity.HasKey(option => option.Id);
            entity.Property(option => option.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(option => option.FieldDefinitionId).HasColumnName("field_definition_id");
            entity.Property(option => option.Value).HasColumnName("value").HasMaxLength(100).IsRequired();
            entity.Property(option => option.Label).HasColumnName("label").HasColumnType("jsonb")
                .HasConversion(Json.Converter<LocalizedText>(), Json.Comparer<LocalizedText>()).IsRequired();
            entity.Property(option => option.DisplayOrder).HasColumnName("display_order");
            entity.Property(option => option.IsActive).HasColumnName("is_active");
            entity.HasIndex(option => new { option.FieldDefinitionId, option.Value })
                .IsUnique().HasDatabaseName("ux_field_options_value");
        });

        modelBuilder.Entity<FieldRule>(entity =>
        {
            entity.ToTable("field_rules");
            entity.HasKey(rule => rule.Id);
            entity.Property(rule => rule.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(rule => rule.TypeVersionId).HasColumnName("type_version_id")
                .HasConversion(id => id.Value, value => new DocumentTypeVersionId(value));
            entity.Property(rule => rule.Kind).HasColumnName("kind")
                .HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(rule => rule.Condition).HasColumnName("condition").HasColumnType("jsonb");
            entity.Property(rule => rule.TargetFieldCodes).HasColumnName("target_field_codes").IsRequired();
            entity.Property(rule => rule.Assertion).HasColumnName("assertion").HasColumnType("jsonb");
            entity.Property(rule => rule.Message).HasColumnName("message").HasColumnType("jsonb")
                .HasConversion(Json.NullableConverter<LocalizedText>(), Json.NullableComparer<LocalizedText>());
            entity.Property(rule => rule.DisplayOrder).HasColumnName("display_order");
            entity.HasIndex(rule => rule.TypeVersionId).HasDatabaseName("ix_field_rules_version");
        });
    }

    /// <summary>Whole-object JSONB mapping for small immutable records (labels, settings).</summary>
    private static class Json
    {
        private static readonly JsonSerializerOptions Options = JsonSerializerOptions.Web;

        public static ValueConverter<T, string> Converter<T>()
            where T : class =>
            new(value => JsonSerializer.Serialize(value, Options), json => JsonSerializer.Deserialize<T>(json, Options)!);

        public static ValueConverter<T?, string?> NullableConverter<T>()
            where T : class =>
            new(
                value => value == null ? null : JsonSerializer.Serialize(value, Options),
                json => json == null ? null : JsonSerializer.Deserialize<T>(json, Options));

        /// <summary>Records compare by value, so a replaced-but-equal label is not an update.</summary>
        public static ValueComparer<T> Comparer<T>()
            where T : class =>
            new(
                (left, right) => JsonSerializer.Serialize(left, Options) == JsonSerializer.Serialize(right, Options),
                value => JsonSerializer.Serialize(value, Options).GetHashCode(StringComparison.Ordinal),
                value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!);

        public static ValueComparer<T?> NullableComparer<T>()
            where T : class =>
            new(
                (left, right) => JsonSerializer.Serialize(left, Options) == JsonSerializer.Serialize(right, Options),
                value => value == null ? 0 : JsonSerializer.Serialize(value, Options).GetHashCode(StringComparison.Ordinal),
                value => value == null ? null : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options));
    }
}

public sealed class DocumentTypeRepository(DocumentTypesDbContext context) : IDocumentTypeRepository
{
    public Task<DocumentType?> FindAsync(DocumentTypeId id, CancellationToken cancellationToken) =>
        WithSchema().FirstOrDefaultAsync(type => type.Id == id, cancellationToken);

    public Task<DocumentType?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        WithSchema().FirstOrDefaultAsync(type => type.Code == code, cancellationToken);

    public async Task<IReadOnlyList<DocumentType>> ListAsync(
        bool includeInactive,
        CancellationToken cancellationToken) =>
        await context.DocumentTypes.AsNoTracking()
            .Where(type => includeInactive || type.IsActive)
            .OrderBy(type => type.Code)
            .ToListAsync(cancellationToken);

    public Task<DocumentTypeVersion?> FindVersionAsync(
        DocumentTypeVersionId versionId,
        CancellationToken cancellationToken) =>
        context.DocumentTypeVersions.AsNoTracking()
            .Include(version => version.Fields).ThenInclude(field => field.Options)
            .Include(version => version.Rules)
            .AsSplitQuery()
            .FirstOrDefaultAsync(version => version.Id == versionId, cancellationToken);

    public void Add(DocumentType documentType) => context.DocumentTypes.Add(documentType);

    private IQueryable<DocumentType> WithSchema() =>
        context.DocumentTypes
            .Include(type => type.Versions).ThenInclude(version => version.Fields).ThenInclude(field => field.Options)
            .Include(type => type.Versions).ThenInclude(version => version.Rules)
            .AsSplitQuery();
}
