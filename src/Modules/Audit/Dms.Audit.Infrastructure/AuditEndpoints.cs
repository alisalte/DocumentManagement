using System.Globalization;
using System.Text;
using System.Text.Json;
using Dms.Application;
using Dms.Audit.Application;
using Dms.SharedKernel;
using Dms.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Dms.Audit.Infrastructure;

public static class AuditEndpoints
{
    public sealed record VerifySealsRequest(DateTimeOffset? From, DateTimeOffset? To);

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var audit = endpoints.MapGroup("/api/v1/audit")
            .WithTags("Audit")
            .RequireAuthorization();

        // No system permission filter here: the handlers decide, because AUDIT_VIEW can come from
        // a role (whole archive) or from an ACL entry on one document.
        audit.MapGet("", async (
                [AsParameters] FilterParameters filter,
                int? skip,
                int? take,
                IDispatcher dispatcher,
                CancellationToken ct) =>
                (await dispatcher.QueryAsync(new ListAuditEntriesQuery(filter.ToFilter(), skip ?? 0, take ?? 100), ct)).ToHttpResult())
            .WithSummary("Read the audit log, newest first. Requires AUDIT_VIEW globally or on the document. 'to' is exclusive.");

        audit.MapGet("/actions", async (IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new ListAuditActionsQuery(), ct)).ToHttpResult())
            .WithSummary("The known action codes, for filtering.");

        audit.MapGet("/export", async (
                [AsParameters] FilterParameters filter,
                string? format,
                IDispatcher dispatcher,
                HttpContext http,
                CancellationToken ct) =>
            {
                var parsed = format?.ToLowerInvariant() switch
                {
                    null or "" or "csv" => AuditExportFormat.Csv,
                    "jsonl" or "ndjson" => AuditExportFormat.JsonLines,
                    _ => (AuditExportFormat?)null,
                };
                if (parsed is not { } exportFormat)
                {
                    return ApiResults.Problem(Error.Validation("audit.export_format", "The format is csv or jsonl."));
                }

                var result = await dispatcher.SendAsync(new ExportAuditCommand(filter.ToFilter(), exportFormat), ct);
                if (result.IsFailure)
                {
                    return ApiResults.Problem(result.Error);
                }

                var export = result.Value;
                http.Response.Headers.CacheControl = "private, no-store";
                return export.Format == AuditExportFormat.Csv
                    ? Results.Stream(stream => AuditExportWriter.WriteCsvAsync(stream, export.Rows, ct), "text/csv; charset=utf-8", export.FileName)
                    : Results.Stream(stream => AuditExportWriter.WriteJsonLinesAsync(stream, export.Rows, ct), "application/x-ndjson", export.FileName);
            })
            .WithSummary("Export audit rows (CSV or JSON lines) for a time range of at most a year. Requires AUDIT_EXPORT; audited.");

        audit.MapGet("/seals/status", async (IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new GetSealStatusQuery(), ct)).ToHttpResult())
            .WithSummary("How far the log is sealed, with which kind of key, and the last verification.");

        audit.MapPost("/seals/verify", async (VerifySealsRequest? request, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new VerifySealsCommand(request?.From, request?.To), ct)).ToHttpResult())
            .WithSummary("Re-check the seals (all of them, or those overlapping the range). Requires AUDIT_VIEW; audited.");

        return endpoints;
    }

    public sealed record FilterParameters(
        DateTimeOffset? From,
        DateTimeOffset? To,
        string? Action,
        string? Outcome,
        string? ActorType,
        Guid? UserId,
        Guid? DocumentId,
        Guid? EntityId,
        string? EntityType)
    {
        public AuditFilter ToFilter() => new(From, To, Action, Outcome, ActorType, UserId, DocumentId, EntityId, EntityType);
    }
}

/// <summary>Writes an export as it is read, so a year of audit never sits in memory.</summary>
public static class AuditExportWriter
{
    private static readonly string[] Columns =
    [
        "occurred_at", "action", "outcome", "actor_type", "user_id", "share_link_id", "entity_type", "entity_id",
        "document_id", "version_id", "ip_address", "user_agent", "correlation_id", "metadata", "id",
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task WriteCsvAsync(Stream stream, IAsyncEnumerable<AuditEntryDto> rows, CancellationToken cancellationToken)
    {
        // The byte order mark makes Excel read UTF-8, which Persian text needs.
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: 64 * 1024);
        await writer.WriteLineAsync(string.Join(',', Columns));

        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            await writer.WriteLineAsync(string.Join(',', new[]
            {
                row.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture),
                row.Action,
                row.Outcome,
                row.ActorType,
                row.UserId?.ToString(),
                row.ShareLinkId?.ToString(),
                row.EntityType,
                row.EntityId?.ToString(),
                row.DocumentId?.ToString(),
                row.VersionId?.ToString(),
                row.IpAddress,
                row.UserAgent,
                row.CorrelationId,
                row.Metadata,
                row.Id.ToString(),
            }.Select(Cell)));
        }
    }

    public static async Task WriteJsonLinesAsync(Stream stream, IAsyncEnumerable<AuditEntryDto> rows, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 64 * 1024);
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            using var metadata = JsonDocument.Parse(row.Metadata);
            await writer.WriteLineAsync(JsonSerializer.Serialize(
                new
                {
                    row.Id,
                    row.OccurredAt,
                    row.Action,
                    row.Outcome,
                    row.ActorType,
                    row.UserId,
                    row.ShareLinkId,
                    row.EntityType,
                    row.EntityId,
                    row.DocumentId,
                    row.VersionId,
                    row.IpAddress,
                    row.UserAgent,
                    row.CorrelationId,
                    Metadata = metadata.RootElement,
                },
                Json));
        }
    }

    /// <summary>
    /// RFC 4180 quoting, and no formula injection: a user agent or a metadata value that starts
    /// with = + - @ would otherwise run as a formula when the file is opened in a spreadsheet.
    /// </summary>
    internal static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }
}
