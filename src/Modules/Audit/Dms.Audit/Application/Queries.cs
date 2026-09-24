using System.Reflection;
using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Identity.Contracts;
using Dms.SharedKernel;
using Microsoft.Extensions.Options;

namespace Dms.Audit.Application;

public sealed record AuditEntryDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    string Outcome,
    string ActorType,
    Guid? UserId,
    string? UserName,
    Guid? ShareLinkId,
    string? EntityType,
    Guid? EntityId,
    Guid? DocumentId,
    Guid? VersionId,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId,
    string Metadata);

/// <summary>The filters the viewer and the export share. Every one is optional.</summary>
public sealed record AuditFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Action = null,
    string? Outcome = null,
    string? ActorType = null,
    Guid? UserId = null,
    Guid? DocumentId = null,
    Guid? EntityId = null,
    string? EntityType = null);

public sealed record ListAuditEntriesQuery(AuditFilter Filter, int Skip, int Take)
    : IQuery<Result<IReadOnlyList<AuditEntryDto>>>;

public enum AuditExportFormat
{
    Csv,
    JsonLines,
}

/// <summary>A command, not a query: the export is audited before a single row leaves.</summary>
public sealed record ExportAuditCommand(AuditFilter Filter, AuditExportFormat Format) : ICommand<Result<AuditExport>>;

/// <summary>The rows, streamed in time order as the response is written.</summary>
public sealed record AuditExport(AuditExportFormat Format, string FileName, IAsyncEnumerable<AuditEntryDto> Rows);

public sealed record ListAuditActionsQuery : IQuery<Result<IReadOnlyList<string>>>;

public interface IAuditQueries
{
    Task<IReadOnlyList<AuditEntryDto>> ListAsync(AuditFilter filter, int skip, int take, CancellationToken cancellationToken);

    /// <summary>Oldest first, unbounded: for exports only.</summary>
    IAsyncEnumerable<AuditEntryDto> StreamAsync(AuditFilter filter, CancellationToken cancellationToken);
}

internal static class AuditAccess
{
    /// <summary>
    /// AUDIT_VIEW is grantable both ways: through a role for the whole archive, or through an ACL
    /// entry for one document, which then only opens that document's history.
    /// </summary>
    public static async Task<Error?> CheckViewAsync(IDmsAuthorizer authorizer, AuditFilter filter, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AuditView, cancellationToken);
        if (!decision.Allowed && filter.DocumentId is { } documentId)
        {
            decision = await authorizer.AuthorizeAsync(PermissionCodes.AuditView, ResourceRef.Document(documentId), cancellationToken);
        }

        return decision.Allowed ? null : Error.Forbidden("auth.forbidden", decision.Explanation);
    }

    public static AuditFilter Normalize(AuditFilter filter) => filter with
    {
        Action = Blank(filter.Action),
        Outcome = Blank(filter.Outcome)?.ToUpperInvariant(),
        ActorType = Blank(filter.ActorType)?.ToUpperInvariant(),
        EntityType = Blank(filter.EntityType),
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ListAuditEntriesHandler(IDmsAuthorizer authorizer, IAuditQueries queries, AuditUserNames names)
    : IQueryHandler<ListAuditEntriesQuery, Result<IReadOnlyList<AuditEntryDto>>>
{
    public async Task<Result<IReadOnlyList<AuditEntryDto>>> HandleAsync(
        ListAuditEntriesQuery query,
        CancellationToken cancellationToken)
    {
        if (await AuditAccess.CheckViewAsync(authorizer, query.Filter, cancellationToken) is { } error)
        {
            return Result.Failure<IReadOnlyList<AuditEntryDto>>(error);
        }

        var take = Math.Clamp(query.Take, 1, 500);
        var results = await queries.ListAsync(AuditAccess.Normalize(query.Filter), Math.Max(query.Skip, 0), take, cancellationToken);
        return Result.Success(await names.AddAsync(results, cancellationToken));
    }
}

/// <summary>
/// Exports audit rows as CSV or JSON lines. Needs the system permission AUDIT_EXPORT and a
/// bounded time range, and is itself audited in the command's transaction before anything is
/// streamed (fail closed, as for reads of content).
/// </summary>
public sealed class ExportAuditHandler(
    IDmsAuthorizer authorizer,
    IAuditQueries queries,
    IAuditWriter audit,
    IOptions<AuditOptions> options) : ICommandHandler<ExportAuditCommand, Result<AuditExport>>
{
    public async Task<Result<AuditExport>> HandleAsync(ExportAuditCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AuditExport, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<AuditExport>(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var filter = AuditAccess.Normalize(command.Filter);
        if (filter.From is not { } from || filter.To is not { } to)
        {
            return Result.Failure<AuditExport>(Error.Validation("audit.export_range_required", "An export needs a start and an end."));
        }

        if (to <= from)
        {
            return Result.Failure<AuditExport>(Error.Validation("audit.export_range", "The end must be after the start."));
        }

        var maxDays = options.Value.MaxExportDays;
        if ((to - from).TotalDays > maxDays)
        {
            return Result.Failure<AuditExport>(Error.Validation("audit.export_range_too_long", $"An export covers at most {maxDays} days."));
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.AuditExported,
                EntityType = "AuditLog",
                DocumentId = filter.DocumentId,
                Metadata = new Dictionary<string, object?>
                {
                    ["format"] = command.Format.ToString(),
                    ["from"] = from,
                    ["to"] = to,
                    ["action"] = filter.Action,
                    ["outcome"] = filter.Outcome,
                    ["actorType"] = filter.ActorType,
                    ["userId"] = filter.UserId,
                    ["documentId"] = filter.DocumentId,
                    ["entityId"] = filter.EntityId,
                    ["entityType"] = filter.EntityType,
                },
            },
            cancellationToken);

        var extension = command.Format == AuditExportFormat.Csv ? "csv" : "jsonl";
        var fileName = $"audit_{from.UtcDateTime:yyyyMMdd'T'HHmm}_{to.UtcDateTime:yyyyMMdd'T'HHmm}.{extension}";
        return Result.Success(new AuditExport(command.Format, fileName, queries.StreamAsync(filter, cancellationToken)));
    }
}

/// <summary>The known action codes, for the viewer's filter.</summary>
public sealed class ListAuditActionsHandler(IDmsAuthorizer authorizer) : IQueryHandler<ListAuditActionsQuery, Result<IReadOnlyList<string>>>
{
    private static readonly IReadOnlyList<string> Actions = typeof(AuditActions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .Order(StringComparer.Ordinal)
        .ToList();

    public async Task<Result<IReadOnlyList<string>>> HandleAsync(ListAuditActionsQuery query, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AuditView, cancellationToken);
        return decision.Allowed
            ? Result.Success(Actions)
            : Result.Failure<IReadOnlyList<string>>(Error.Forbidden("auth.forbidden", decision.Explanation));
    }
}

/// <summary>Display names for the actors on a page of results.</summary>
public sealed class AuditUserNames(IUserDirectory users)
{
    public async Task<IReadOnlyList<AuditEntryDto>> AddAsync(IReadOnlyList<AuditEntryDto> entries, CancellationToken cancellationToken)
    {
        var ids = entries.Where(entry => entry.UserId is not null).Select(entry => new UserId(entry.UserId!.Value)).Distinct().ToList();
        if (ids.Count == 0)
        {
            return entries;
        }

        var names = (await users.FindManyAsync(ids, cancellationToken)).ToDictionary(user => user.Id.Value, user => user.DisplayName);
        return entries
            .Select(entry => entry.UserId is { } id && names.TryGetValue(id, out var name) ? entry with { UserName = name } : entry)
            .ToList();
    }
}
