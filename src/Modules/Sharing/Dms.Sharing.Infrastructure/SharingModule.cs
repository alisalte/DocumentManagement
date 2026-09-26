using Dms.Application;
using Dms.Authorization.Contracts;
using Dms.Infrastructure;
using Dms.Infrastructure.Persistence;
using Dms.SharedKernel;
using Dms.Sharing.Application;
using Dms.Sharing.Domain;
using Dms.Sharing.Infrastructure.Persistence;
using Dms.Storage.Contracts;
using Dms.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dms.Sharing.Infrastructure;

public static class SharingModule
{
    /// <summary>After workflow (35) and before audit (40): shares reference document versions.</summary>
    public const int MigrationOrder = 37;

    /// <summary>Opening a link and asking about it: few per minute per address, against guessing.</summary>
    public const string OpenRateLimitPolicy = "share-link-open";

    /// <summary>Pages and downloads of an opened link: generous, a long document has many pages.</summary>
    public const string ContentRateLimitPolicy = "share-link-content";

    /// <summary>The link session travels in a header, never in the URL, so it stays out of logs.</summary>
    public const string SessionHeader = "X-Share-Session";

    public static IServiceCollection AddSharingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDmsModuleDbContext<SharingDbContext>(MigrationOrder, SharingDbContext.Schema);
        services.Configure<SharingOptions>(configuration.GetSection(SharingOptions.SectionName));

        services.AddScoped<IShareRepository, ShareRepository>();
        services.AddScoped<IShareLinkRepository, ShareLinkRepository>();
        services.AddScoped<SharingPolicy>();
        services.AddScoped<SharingAudit>();
        services.AddScoped<ShareManagement>();
        services.AddScoped<PublicLinkResolver>();
        services.AddScoped<ITemporaryGrantSource, ShareGrantSource>();

        services.AddScoped<ICommandHandler<CreateShareCommand, Result<Guid>>, CreateShareHandler>();
        services.AddScoped<ICommandHandler<RevokeShareCommand, Result>, RevokeShareHandler>();
        services.AddScoped<ICommandHandler<CreateShareLinkCommand, Result<CreatedShareLinkDto>>, CreateShareLinkHandler>();
        services.AddScoped<ICommandHandler<RevokeShareLinkCommand, Result>, RevokeShareLinkHandler>();
        services.AddScoped<IQueryHandler<ListDocumentSharesQuery, Result<DocumentSharesDto>>, ListDocumentSharesHandler>();
        services.AddScoped<IQueryHandler<ListReceivedSharesQuery, Result<IReadOnlyList<ReceivedShareDto>>>, ListReceivedSharesHandler>();

        services.AddScoped<IQueryHandler<GetPublicLinkQuery, Result<PublicLinkInfoDto>>, GetPublicLinkHandler>();
        services.AddScoped<ICommandHandler<OpenPublicLinkCommand, Result<OpenedLinkDto>>, OpenPublicLinkHandler>();
        services.AddScoped<IQueryHandler<GetPublicLinkPageQuery, Result<StoredContent>>, GetPublicLinkPageHandler>();
        services.AddScoped<ICommandHandler<DownloadPublicLinkCommand, Result<StoredContent>>, DownloadPublicLinkHandler>();
        services.AddScoped<ICommandHandler<StartPublicLinkPrintCommand, Result>, StartPublicLinkPrintHandler>();

        services.AddScoped<IJobHandler, SharingCleanupJob>();
        services.AddRecurringJob(SharingCleanupJob.Type, TimeSpan.FromHours(1));

        return services;
    }

    public sealed record CreateShareRequest(
        Guid VersionId,
        Guid RecipientId,
        IReadOnlyList<SharePermissions>? Permissions,
        DateTimeOffset? ExpiresAt,
        string? Message);

    public sealed record CreateLinkRequest(
        Guid VersionId,
        IReadOnlyList<SharePermissions>? Permissions,
        DateTimeOffset ExpiresAt,
        int? MaxAccessCount,
        string? Password,
        string? Label);

    public sealed record OpenLinkRequest(string? Password);

    public static IEndpointRouteBuilder MapSharingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var documents = endpoints.MapGroup("/api/v1/documents/{id:guid}").WithTags("Sharing").RequireAuthorization();

        documents.MapGet("/shares", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new ListDocumentSharesQuery(id), ct)).ToHttpResult())
            .WithSummary("Shares and links of a document: all of them for its permission managers, otherwise the caller's own.");

        documents.MapPost("/shares", async (Guid id, CreateShareRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var command = new CreateShareCommand(
                    id,
                    request.VersionId,
                    request.RecipientId,
                    SharePermissionCodes.Combine(request.Permissions),
                    request.ExpiresAt,
                    request.Message);
                var result = await dispatcher.SendAsync(command, ct);
                return result.ToHttpResult(shareId => Results.Created($"/api/v1/shares/{shareId}", new { id = shareId }));
            })
            .WithSummary("Share a published version with a colleague (DOCUMENT_SHARE, and each shared permission yourself).");

        documents.MapPost("/links", async (Guid id, CreateLinkRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var command = new CreateShareLinkCommand(
                    id,
                    request.VersionId,
                    SharePermissionCodes.Combine(request.Permissions),
                    request.ExpiresAt,
                    request.MaxAccessCount,
                    request.Password,
                    request.Label);
                var result = await dispatcher.SendAsync(command, ct);
                return result.ToHttpResult(created => Results.Created($"/api/v1/share-links/{created.Id}", created));
            })
            .WithSummary("Create an external link to a published version (DOCUMENT_SHARE_EXTERNAL). The token is returned once.");

        var shares = endpoints.MapGroup("/api/v1").WithTags("Sharing").RequireAuthorization();

        shares.MapGet("/shares/received", async (IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new ListReceivedSharesQuery(), ct)).ToHttpResult())
            .WithSummary("Versions other people shared with the caller that still work.");

        shares.MapDelete("/shares/{shareId:guid}", async (Guid shareId, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new RevokeShareCommand(shareId), ct)).ToHttpResult())
            .WithSummary("Revoke a share (its sharer, its recipient, or a permission manager of the document).");

        shares.MapDelete("/share-links/{linkId:guid}", async (Guid linkId, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new RevokeShareLinkCommand(linkId), ct)).ToHttpResult())
            .WithSummary("Revoke an external link (its creator or a permission manager of the document).");

        MapPublic(endpoints);
        return endpoints;
    }

    /// <summary>
    /// The anonymous side of links. The token is in the path because it is the link; the session
    /// comes in a header. Every answer is marked private and uncacheable.
    /// </summary>
    private static void MapPublic(IEndpointRouteBuilder endpoints)
    {
        var links = endpoints.MapGroup("/api/v1/public/links/{token}")
            .WithTags("Sharing: public links")
            .AllowAnonymous()
            .AddEndpointFilter(async (context, next) =>
            {
                var headers = context.HttpContext.Response.Headers;
                headers.CacheControl = "private, no-store";
                headers["Referrer-Policy"] = "no-referrer";
                headers.XContentTypeOptions = "nosniff";
                return await next(context);
            });

        links.MapGet("", async (string token, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new GetPublicLinkQuery(token), ct)).ToHttpResult())
            .RequireRateLimiting(OpenRateLimitPolicy)
            .WithSummary("Whether the link needs a password. Says nothing about the document.");

        links.MapPost("/open", async (string token, OpenLinkRequest? request, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new OpenPublicLinkCommand(token, request?.Password), ct)).ToHttpResult())
            .RequireRateLimiting(OpenRateLimitPolicy)
            .WithSummary("Open the link: checks the password, counts one opening and returns a session.");

        links.MapGet("/pages/{page:int}", (string token, int page, [FromHeader(Name = SessionHeader)] string? session, IDispatcher dispatcher, CancellationToken ct) =>
                ContentAsync(dispatcher.QueryAsync(new GetPublicLinkPageQuery(token, session, page, ForPrint: false), ct), attachment: false))
            .RequireRateLimiting(ContentRateLimitPolicy)
            .WithSummary("One preview page, watermarked with the link.");

        links.MapPost("/print", async (string token, [FromHeader(Name = SessionHeader)] string? session, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new StartPublicLinkPrintCommand(token, session), ct)).ToHttpResult())
            .RequireRateLimiting(ContentRateLimitPolicy)
            .WithSummary("Start printing (when the link includes print). Audited.");

        links.MapGet("/print/{page:int}", (string token, int page, [FromHeader(Name = SessionHeader)] string? session, IDispatcher dispatcher, CancellationToken ct) =>
                ContentAsync(dispatcher.QueryAsync(new GetPublicLinkPageQuery(token, session, page, ForPrint: true), ct), attachment: false))
            .RequireRateLimiting(ContentRateLimitPolicy)
            .WithSummary("One print page (when the link includes print).");

        links.MapGet("/content", (string token, [FromHeader(Name = SessionHeader)] string? session, IDispatcher dispatcher, CancellationToken ct) =>
                ContentAsync(dispatcher.SendAsync(new DownloadPublicLinkCommand(token, session), ct), attachment: true))
            .RequireRateLimiting(ContentRateLimitPolicy)
            .WithSummary("Download the original file (when the link includes download). Audited.");
    }

    private static async Task<IResult> ContentAsync(Task<Result<StoredContent>> pending, bool attachment)
    {
        var result = await pending;
        if (result.IsFailure)
        {
            return ApiResults.Problem(result.Error);
        }

        var content = result.Value;
        // Phase 9: never serve browser-executable MIME types from share links on our origin.
        var mime = DownloadContentType(content.MimeType);
        return attachment
            ? Results.Stream(content.Content, mime, content.FileName, enableRangeProcessing: content.Content.CanSeek)
            : Results.Stream(content.Content, mime);
    }

    private static string DownloadContentType(string mimeType) =>
        mimeType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
        || mimeType.StartsWith("application/xhtml", StringComparison.OrdinalIgnoreCase)
        || mimeType.StartsWith("image/svg", StringComparison.OrdinalIgnoreCase)
        || mimeType.Equals("text/javascript", StringComparison.OrdinalIgnoreCase)
        || mimeType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
        || mimeType.Equals("text/xml", StringComparison.OrdinalIgnoreCase)
        || mimeType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            ? "application/octet-stream"
            : mimeType;
}

public sealed class SharingDbContextFactory : IDesignTimeDbContextFactory<SharingDbContext>
{
    public SharingDbContext CreateDbContext(string[] args) => new(
        DesignTimeSupport.CreateOptions<SharingDbContext>(SharingDbContext.Schema),
        DesignTimeSupport.CreateSession());
}
