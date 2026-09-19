using Dms.Application;
using Dms.Audit.Application;
using Dms.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Dms.Audit.Infrastructure;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var audit = endpoints.MapGroup("/api/v1/audit")
            .WithTags("Audit")
            .RequireAuthorization();

        // No system permission filter here: the handler decides, because AUDIT_VIEW can come from a
        // role (whole archive) or from an ACL entry on one document.
        audit.MapGet("", async (
                DateTimeOffset? from,
                DateTimeOffset? to,
                string? action,
                Guid? userId,
                Guid? documentId,
                Guid? entityId,
                int? skip,
                int? take,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var query = new ListAuditEntriesQuery(
                    from,
                    to,
                    action,
                    userId,
                    documentId,
                    entityId,
                    skip ?? 0,
                    take ?? 100);

                var result = await dispatcher.QueryAsync(query, ct);
                return result.ToHttpResult();
            })
            .WithSummary("Read the audit log. Requires AUDIT_VIEW globally or on the document.");

        return endpoints;
    }
}
