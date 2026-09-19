using System.Text.Json;
using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.DocumentTypes.Application;
using Dms.DocumentTypes.Contracts;
using Dms.DocumentTypes.Domain;
using Dms.Infrastructure;
using Dms.Infrastructure.Persistence;
using Dms.SharedKernel;
using Dms.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Dms.DocumentTypes.Infrastructure;

public sealed class DocumentTypesDbContext : DbContext
{
    public const string Schema = "doctypes";

    public DocumentTypesDbContext(DbContextOptions<DocumentTypesDbContext> options, DbSession session)
        : base(options) => session.Register(this);

    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();

    public DbSet<DocumentTypeVersion> DocumentTypeVersions => Set<DocumentTypeVersion>();

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
                .HasConversion(
                    settings => JsonSerializer.Serialize(settings, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<DocumentTypeSettings>(json, (JsonSerializerOptions?)null)!)
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
        });
    }
}

public sealed class DocumentTypeRepository(DocumentTypesDbContext context) : IDocumentTypeRepository
{
    public Task<DocumentType?> FindAsync(DocumentTypeId id, CancellationToken cancellationToken) =>
        context.DocumentTypes.Include(type => type.Versions)
            .FirstOrDefaultAsync(type => type.Id == id, cancellationToken);

    public Task<DocumentType?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        context.DocumentTypes.Include(type => type.Versions)
            .FirstOrDefaultAsync(type => type.Code == code, cancellationToken);

    public async Task<IReadOnlyList<DocumentType>> ListAsync(
        bool includeInactive,
        CancellationToken cancellationToken) =>
        await context.DocumentTypes.AsNoTracking()
            .Where(type => includeInactive || type.IsActive)
            .OrderBy(type => type.Code)
            .ToListAsync(cancellationToken);

    public async Task<DocumentTypeId?> FindTypeOfVersionAsync(
        DocumentTypeVersionId versionId,
        CancellationToken cancellationToken)
    {
        var version = await context.DocumentTypeVersions.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == versionId, cancellationToken);

        return version?.DocumentTypeId;
    }

    public void Add(DocumentType documentType) => context.DocumentTypes.Add(documentType);
}

public static class DocumentTypesModule
{
    public const int MigrationOrder = 28;

    public static IServiceCollection AddDocumentTypesModule(this IServiceCollection services)
    {
        services.AddDmsModuleDbContext<DocumentTypesDbContext>(MigrationOrder, DocumentTypesDbContext.Schema);
        services.AddScoped<IDocumentTypeRepository, DocumentTypeRepository>();
        services.AddScoped<IDocumentTypeCatalog, DocumentTypeCatalog>();

        services.AddScoped<ICommandHandler<CreateDocumentTypeCommand, Result<Guid>>, CreateDocumentTypeHandler>();
        services.AddScoped<ICommandHandler<UpdateDocumentTypeCommand, Result>, UpdateDocumentTypeHandler>();
        services.AddScoped<ICommandHandler<PublishDocumentTypeVersionCommand, Result<Guid>>,
            PublishDocumentTypeVersionHandler>();
        services.AddScoped<IQueryHandler<ListDocumentTypesQuery, Result<IReadOnlyList<DocumentTypeDto>>>,
            ListDocumentTypesHandler>();

        return services;
    }

    public static IEndpointRouteBuilder MapDocumentTypeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/document-types")
            .WithTags("Document types")
            .RequireAuthorization();

        group.MapGet("", async (bool? includeInactive, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.QueryAsync(new ListDocumentTypesQuery(includeInactive ?? false), ct);
                return result.ToHttpResult();
            })
            .WithSummary("Document types available when filing a document.");

        var admin = endpoints.MapGroup("/api/v1/admin/document-types")
            .WithTags("Administration: document types")
            .RequireAuthorization()
            .RequireSystemPermission(PermissionCodes.AdminManageDocumentTypes);

        admin.MapPost("", async (CreateDocumentTypeRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var command = new CreateDocumentTypeCommand(request.Code, request.Name, request.Description);
            var result = await dispatcher.SendAsync(command, ct);
            return result.ToHttpResult(id => Results.Created($"/api/v1/admin/document-types/{id}", new { id }));
        });

        admin.MapPut("/{id:guid}", async (
            Guid id,
            UpdateDocumentTypeRequest request,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var command = new UpdateDocumentTypeCommand(
                id,
                request.Name,
                request.Description,
                request.Settings ?? new DocumentTypeSettings());

            var result = await dispatcher.SendAsync(command, ct);
            return result.ToHttpResult();
        });

        admin.MapPost("/{id:guid}/publish", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new PublishDocumentTypeVersionCommand(id), ct);
            return result.ToHttpResult(versionId => Results.Ok(new { versionId }));
        });

        return endpoints;
    }

    public sealed record CreateDocumentTypeRequest(string Code, string Name, string? Description);

    public sealed record UpdateDocumentTypeRequest(string Name, string? Description, DocumentTypeSettings? Settings);
}

public sealed class DocumentTypesDbContextFactory : IDesignTimeDbContextFactory<DocumentTypesDbContext>
{
    public DocumentTypesDbContext CreateDbContext(string[] args) => new(
        DesignTimeSupport.CreateOptions<DocumentTypesDbContext>(DocumentTypesDbContext.Schema),
        DesignTimeSupport.CreateSession());
}
