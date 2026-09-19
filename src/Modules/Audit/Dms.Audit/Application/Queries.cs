using Dms.Application;
using Dms.Authorization.Contracts;
using Dms.SharedKernel;

namespace Dms.Audit.Application;

public sealed record AuditEntryDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    string Outcome,
    string ActorType,
    Guid? UserId,
    string? EntityType,
    Guid? EntityId,
    Guid? DocumentId,
    string? IpAddress,
    string Metadata);

public sealed record ListAuditEntriesQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Action,
    Guid? UserId,
    Guid? DocumentId,
    Guid? EntityId,
    int Skip,
    int Take) : IQuery<Result<IReadOnlyList<AuditEntryDto>>>;

public interface IAuditQueries
{
    Task<IReadOnlyList<AuditEntryDto>> ListAsync(ListAuditEntriesQuery query, CancellationToken cancellationToken);
}

public sealed class ListAuditEntriesHandler(IDmsAuthorizer authorizer, IAuditQueries queries)
    : IQueryHandler<ListAuditEntriesQuery, Result<IReadOnlyList<AuditEntryDto>>>
{
    public async Task<Result<IReadOnlyList<AuditEntryDto>>> HandleAsync(
        ListAuditEntriesQuery query,
        CancellationToken cancellationToken)
    {
        // AUDIT_VIEW is grantable both ways: through a role for the whole archive, or through an
        // ACL entry for one document.
        var decision = query.DocumentId is { } documentId
            ? await authorizer.AuthorizeAsync(
                PermissionCodes.AuditView,
                ResourceRef.Document(documentId),
                cancellationToken)
            : await authorizer.AuthorizeSystemAsync(PermissionCodes.AuditView, cancellationToken);

        if (!decision.Allowed)
        {
            return Result.Failure<IReadOnlyList<AuditEntryDto>>(
                Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var take = Math.Clamp(query.Take, 1, 500);
        var results = await queries.ListAsync(query with { Take = take, Skip = Math.Max(query.Skip, 0) }, cancellationToken);
        return Result.Success(results);
    }
}
