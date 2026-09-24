using System.Text.Json;
using Dms.Application;
using Dms.Authorization.Contracts;
using Dms.Documents.Contracts;
using Dms.Infrastructure;
using Dms.Infrastructure.Persistence;
using Dms.Search.Application;
using Dms.Search.Infrastructure.Engine;
using Dms.Search.Infrastructure.Extraction;
using Dms.Search.Infrastructure.Persistence;
using Dms.SharedKernel;
using Dms.Storage.Contracts;
using Dms.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dms.Search.Infrastructure;

public static class SearchModule
{
    /// <summary>Last of the document modules: it only reads the others.</summary>
    public const int MigrationOrder = 40;

    public static IServiceCollection AddSearchModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SearchOptions>(configuration.GetSection(SearchOptions.SectionName));
        var options = configuration.GetSection(SearchOptions.SectionName).Get<SearchOptions>() ?? new SearchOptions();

        services.AddDmsModuleDbContext<SearchDbContext>(MigrationOrder, SearchDbContext.Schema);
        services.AddScoped<IContentExtractionRepository, ContentExtractionRepository>();

        // Both engines are optional: without OpenSearch search falls back to titles in Postgres,
        // without Tika only metadata is searchable. Neither blocks filing a document.
        if (string.IsNullOrWhiteSpace(options.OpenSearchUrl))
        {
            services.AddSingleton<ISearchEngine, DisabledSearchEngine>();
        }
        else
        {
            services.AddHttpClient<ISearchEngine, OpenSearchEngine>(client => client.Timeout = TimeSpan.FromSeconds(30));
        }

        if (string.IsNullOrWhiteSpace(options.TikaUrl))
        {
            services.AddSingleton<ITextExtractor, NullTextExtractor>();
        }
        else
        {
            // OCR of a long scan takes minutes.
            services.AddHttpClient<ITextExtractor, TikaTextExtractor>(client => client.Timeout = TimeSpan.FromMinutes(20));
        }

        services.AddScoped<DocumentIndexer>();
        services.AddScoped<IDocumentChangeListener, SearchDocumentChangeListener>();
        services.AddScoped<IObjectProcessedListener, SearchObjectProcessedListener>();

        services.AddScoped<IJobHandler, ExtractTextJob>();
        services.AddScoped<IJobHandler, IndexDocumentJob>();
        services.AddScoped<IJobHandler, ReindexJob>();
        services.AddScoped<IJobHandler, RetryFailedExtractionsJob>();
        services.AddRecurringJob(RetryFailedExtractionsJob.Type, TimeSpan.FromHours(1));

        services.AddScoped<IQueryHandler<SearchDocumentsQuery, Result<SearchResultDto>>, SearchDocumentsHandler>();
        services.AddScoped<IQueryHandler<SearchStatusQuery, Result<SearchStatusDto>>, SearchStatusHandler>();
        services.AddScoped<ICommandHandler<StartReindexCommand, Result>, StartReindexHandler>();
        services.AddScoped<ICommandHandler<RetryFailedExtractionsCommand, Result<int>>, RetryFailedExtractionsHandler>();

        return services;
    }

    /// <summary>The POST form of search, for metadata conditions that do not fit a query string.</summary>
    public sealed record SearchRequest(
        string? Q,
        Guid? CategoryId,
        Guid? DocumentTypeId,
        string? MimeType,
        string? Tag,
        DateTimeOffset? From,
        DateTimeOffset? To,
        bool AllVersions,
        IReadOnlyList<MetadataFilterRequest>? Metadata,
        int? Page,
        int? PageSize);

    public sealed record MetadataFilterRequest(string Field, string Op, JsonElement Value);

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var search = endpoints.MapGroup("/api/v1/search").WithTags("Search").RequireAuthorization();

        search.MapGet("", async (
                string? q,
                Guid? categoryId,
                Guid? documentTypeId,
                string? mimeType,
                string? tag,
                DateTimeOffset? from,
                DateTimeOffset? to,
                bool? allVersions,
                int? page,
                int? pageSize,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var query = new SearchDocumentsQuery(q, categoryId, documentTypeId, mimeType, tag, from, to, allVersions ?? false, null, page, pageSize);
                return (await dispatcher.QueryAsync(query, ct)).ToHttpResult();
            })
            .WithSummary("Full-text search over titles, metadata and file content, limited to what the caller may see.");

        search.MapPost("", async (SearchRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var query = new SearchDocumentsQuery(
                    request.Q,
                    request.CategoryId,
                    request.DocumentTypeId,
                    request.MimeType,
                    request.Tag,
                    request.From,
                    request.To,
                    request.AllVersions,
                    request.Metadata?.Select(filter => new MetadataFilter(filter.Field, filter.Op, filter.Value)).ToList(),
                    request.Page,
                    request.PageSize);
                return (await dispatcher.QueryAsync(query, ct)).ToHttpResult();
            })
            .WithSummary("Search with typed metadata conditions (eq, gt, gte, lt, lte, contains) within one document type.");

        var admin = endpoints.MapGroup("/api/v1/admin/search")
            .WithTags("Administration: search")
            .RequireAuthorization()
            .RequireSystemPermission(PermissionCodes.AdminManageSearch);

        admin.MapGet("/status", async (IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new SearchStatusQuery(), ct)).ToHttpResult())
            .WithSummary("Engines, the live index, and text extraction counts by status.");

        admin.MapPost("/reindex", async (IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(new StartReindexCommand(), ct);
                return result.IsSuccess ? Results.Accepted() : ApiResults.Problem(result.Error);
            })
            .WithSummary("Rebuild the index into a new generation from Postgres and the stored text; no OCR is repeated.");

        admin.MapPost("/retry-failed", async (IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new RetryFailedExtractionsCommand(), ct)).ToHttpResult(count => Results.Ok(new { queued = count })))
            .WithSummary("Queue every failed text extraction again.");

        return endpoints;
    }
}

public sealed class SearchDbContextFactory : IDesignTimeDbContextFactory<SearchDbContext>
{
    public SearchDbContext CreateDbContext(string[] args) => new(
        DesignTimeSupport.CreateOptions<SearchDbContext>(SearchDbContext.Schema),
        DesignTimeSupport.CreateSession());
}
