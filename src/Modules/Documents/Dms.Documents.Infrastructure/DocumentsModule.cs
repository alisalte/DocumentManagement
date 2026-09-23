using Dms.Application;
using Dms.Authorization.Contracts;
using Dms.Documents.Application;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.Documents.Infrastructure.Persistence;
using Dms.Infrastructure;
using Dms.Infrastructure.Persistence;
using Dms.SharedKernel;
using Dms.Storage.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dms.Documents.Infrastructure;

public static class DocumentsModule
{
    /// <summary>After storage (25) and document types (28): the migration adds foreign keys to both.</summary>
    public const int MigrationOrder = 30;

    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        services.AddDmsModuleDbContext<DocumentsDbContext>(MigrationOrder, DocumentsDbContext.Schema);

        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<IDocumentReadModel, DocumentReadModel>();
        services.AddScoped<IDocumentLocator, DocumentLocator>();

        // The real category tree and document state replace the phase 1 flat stand-in,
        // regardless of the order in which the modules are registered.
        services.RemoveAll<IResourceHierarchy>();
        services.AddScoped<IResourceHierarchy, DocumentResourceHierarchy>();

        services.AddScoped<DocumentAccess>();
        services.AddScoped<UploadAttachment>();
        services.AddScoped<TagResolver>();
        services.AddScoped<MetadataGate>();

        services.AddScoped<ICommandHandler<CreateCategoryCommand, Result<Guid>>, CreateCategoryHandler>();
        services.AddScoped<ICommandHandler<UpdateCategoryCommand, Result>, UpdateCategoryHandler>();
        services.AddScoped<ICommandHandler<MoveCategoryCommand, Result>, MoveCategoryHandler>();

        services.AddScoped<ICommandHandler<RegisterUploadCommand, Result<UploadResultDto>>, RegisterUploadHandler>();
        services.AddScoped<ICommandHandler<CreateDocumentCommand, Result<CreatedVersionDto>>, CreateDocumentHandler>();
        services.AddScoped<ICommandHandler<AddVersionCommand, Result<CreatedVersionDto>>, AddVersionHandler>();
        services.AddScoped<ICommandHandler<UpdateDocumentCommand, Result>, UpdateDocumentHandler>();
        services.AddScoped<ICommandHandler<UpdateMetadataCommand, Result<MetadataUpdateDto>>, UpdateMetadataHandler>();
        services.AddScoped<ICommandHandler<SetDocumentTagsCommand, Result>, SetDocumentTagsHandler>();
        services.AddScoped<ICommandHandler<DeleteDocumentCommand, Result>, DeleteDocumentHandler>();
        services.AddScoped<ICommandHandler<RestoreDocumentCommand, Result>, RestoreDocumentHandler>();
        services.AddScoped<ICommandHandler<PurgeDocumentCommand, Result>, PurgeDocumentHandler>();
        services.AddScoped<ICommandHandler<OpenContentCommand, Result<StoredContent>>, OpenContentHandler>();

        services.AddScoped<IQueryHandler<ListDocumentsQuery, Result<PagedResult<DocumentListItemDto>>>,
            ListDocumentsHandler>();
        services.AddScoped<IQueryHandler<GetDocumentQuery, Result<DocumentDetailsDto>>, GetDocumentHandler>();
        services.AddScoped<IQueryHandler<ListVersionsQuery, Result<IReadOnlyList<DocumentVersionDto>>>,
            ListVersionsHandler>();
        services.AddScoped<IQueryHandler<ListRecycleBinQuery, Result<PagedResult<DocumentListItemDto>>>,
            ListRecycleBinHandler>();
        services.AddScoped<IQueryHandler<ListTagsQuery, Result<IReadOnlyList<TagDto>>>, ListTagsHandler>();
        services.AddScoped<IQueryHandler<ListCategoriesQuery, Result<IReadOnlyList<CategoryNodeDto>>>,
            ListCategoriesHandler>();

        services.AddScoped<IDataSeeder, RootCategorySeeder>();

        return services;
    }
}

/// <summary>
/// Every document needs a category (section 5.2), and an ACL entry on the root then covers the
/// whole archive. The root is created once; administrators build the tree below it.
/// </summary>
public sealed class RootCategorySeeder(DocumentsDbContext context, TimeProvider timeProvider) : IDataSeeder
{
    public const string RootCode = "ROOT";

    public int Order => 30;

    public string Name => "root category";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await context.Categories.AnyAsync(category => category.ParentId == null, cancellationToken))
        {
            return;
        }

        var root = Category.Create(null, "اسناد", RootCode, "ریشه‌ی بایگانی", createdBy: null, timeProvider.GetUtcNow());
        context.Categories.Add(root.Value);
    }
}

public sealed class DocumentsDbContextFactory : IDesignTimeDbContextFactory<DocumentsDbContext>
{
    public DocumentsDbContext CreateDbContext(string[] args) => new(
        DesignTimeSupport.CreateOptions<DocumentsDbContext>(DocumentsDbContext.Schema),
        DesignTimeSupport.CreateSession());
}
