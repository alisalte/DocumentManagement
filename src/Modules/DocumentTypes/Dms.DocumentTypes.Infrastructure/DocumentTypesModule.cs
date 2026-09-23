using Dms.Application;
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

public static class DocumentTypesModule
{
    public const int MigrationOrder = 28;

    public static IServiceCollection AddDocumentTypesModule(this IServiceCollection services)
    {
        services.AddDmsModuleDbContext<DocumentTypesDbContext>(MigrationOrder, DocumentTypesDbContext.Schema);
        services.AddScoped<IDocumentTypeRepository, DocumentTypeRepository>();
        services.AddScoped<IDocumentTypeCatalog, DocumentTypeCatalog>();
        services.AddSingleton<PublishedSchemaCache>();

        services.AddScoped<ICommandHandler<CreateDocumentTypeCommand, Result<Guid>>, CreateDocumentTypeHandler>();
        services.AddScoped<ICommandHandler<UpdateDocumentTypeCommand, Result>, UpdateDocumentTypeHandler>();
        services.AddScoped<ICommandHandler<SaveDraftSchemaCommand, Result>, SaveDraftSchemaHandler>();
        services.AddScoped<ICommandHandler<PublishDocumentTypeVersionCommand, Result<Guid>>,
            PublishDocumentTypeVersionHandler>();
        services.AddScoped<IQueryHandler<ListDocumentTypesQuery, Result<IReadOnlyList<DocumentTypeDto>>>,
            ListDocumentTypesHandler>();
        services.AddScoped<IQueryHandler<GetDocumentTypeAdminQuery, Result<DocumentTypeAdminDto>>,
            GetDocumentTypeAdminHandler>();
        services.AddScoped<IQueryHandler<GetSchemaQuery, Result<DocumentTypeSchema>>, GetSchemaHandler>();

        services.AddScoped<IDataSeeder, GeneralDocumentTypeSeeder>();

        return services;
    }

    public static IEndpointRouteBuilder MapDocumentTypeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/document-types")
            .WithTags("Document types")
            .RequireAuthorization();

        group.MapGet("", async (bool? includeInactive, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new ListDocumentTypesQuery(includeInactive ?? false), ct)).ToHttpResult())
            .WithSummary("Document types available when filing a document.");

        group.MapGet("/{id:guid}/schema", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new GetSchemaQuery(null, id), ct)).ToHttpResult())
            .WithSummary("The latest published schema: the form for a new document.");

        group.MapGet("/schemas/{versionId:guid}", async (Guid versionId, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new GetSchemaQuery(versionId, null), ct)).ToHttpResult())
            .WithSummary("One published schema version, to read a document with the schema it was written against.");

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

        admin.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new GetDocumentTypeAdminQuery(id), ct)).ToHttpResult())
            .WithSummary("Type, version history and the editable draft schema.");

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
                request.Settings ?? new DocumentTypeSettings(),
                request.IsActive ?? true);

            return (await dispatcher.SendAsync(command, ct)).ToHttpResult();
        });

        admin.MapPut("/{id:guid}/draft", async (
                Guid id,
                SaveDraftRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
                (await dispatcher.SendAsync(new SaveDraftSchemaCommand(id, request.Fields ?? [], request.Rules ?? []), ct))
                    .ToHttpResult())
            .WithSummary("Replace the draft's fields and rules. Errors come back per path, e.g. fields[2].code.");

        admin.MapPost("/{id:guid}/publish", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(new PublishDocumentTypeVersionCommand(id), ct);
                return result.ToHttpResult(versionId => Results.Ok(new { versionId }));
            })
            .WithSummary("Publish the draft. It becomes immutable, and a new draft starts as its copy.");

        return endpoints;
    }

    public sealed record CreateDocumentTypeRequest(string Code, string Name, string? Description);

    public sealed record UpdateDocumentTypeRequest(
        string Name,
        string? Description,
        DocumentTypeSettings? Settings,
        bool? IsActive);

    public sealed record SaveDraftRequest(IReadOnlyList<FieldSchema>? Fields, IReadOnlyList<FieldRuleSchema>? Rules);
}

/// <summary>
/// One published, field-less type so documents can be filed from day one. Phase 3 lets
/// administrators build real types with fields; this one stays as the catch-all.
/// </summary>
public sealed class GeneralDocumentTypeSeeder(DocumentTypesDbContext context, TimeProvider timeProvider) : IDataSeeder
{
    public const string Code = "GENERAL";

    public int Order => 28;

    public string Name => "general document type";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await context.DocumentTypes.AnyAsync(type => type.Code == Code, cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var general = DocumentType.Create(Code, "سند عمومی", "نوع پیش‌فرض برای اسنادی که نوع خاصی ندارند.", now);
        general.PublishDraft(publishedBy: null, now);
        context.DocumentTypes.Add(general);
    }
}

public sealed class DocumentTypesDbContextFactory : IDesignTimeDbContextFactory<DocumentTypesDbContext>
{
    public DocumentTypesDbContext CreateDbContext(string[] args) => new(
        DesignTimeSupport.CreateOptions<DocumentTypesDbContext>(DocumentTypesDbContext.Schema),
        DesignTimeSupport.CreateSession());
}
