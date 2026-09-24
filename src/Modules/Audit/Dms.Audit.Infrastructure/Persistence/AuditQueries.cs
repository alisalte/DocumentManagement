using System.Runtime.CompilerServices;
using Dms.Audit.Application;
using Dms.Audit.Domain;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Dms.Audit.Infrastructure.Persistence;

public sealed class AuditQueries(AuditDbContext context) : IAuditQueries
{
    public async Task<IReadOnlyList<AuditEntryDto>> ListAsync(
        AuditFilter filter,
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        await Project(Filter(filter)
                .OrderByDescending(entry => entry.OccurredAt)
                .ThenByDescending(entry => entry.Id)
                .Skip(skip)
                .Take(take))
            .ToListAsync(cancellationToken);

    public async IAsyncEnumerable<AuditEntryDto> StreamAsync(
        AuditFilter filter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = Project(Filter(filter).OrderBy(entry => entry.OccurredAt).ThenBy(entry => entry.Id)).AsAsyncEnumerable();
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            yield return row;
        }
    }

    private IQueryable<AuditEntry> Filter(AuditFilter filter)
    {
        var entries = context.AuditEntries.AsNoTracking();

        if (filter.From is { } from)
        {
            entries = entries.Where(entry => entry.OccurredAt >= from);
        }

        if (filter.To is { } to)
        {
            entries = entries.Where(entry => entry.OccurredAt < to);
        }

        if (filter.Action is { } action)
        {
            entries = entries.Where(entry => entry.Action == action);
        }

        if (filter.Outcome is { } outcome)
        {
            entries = entries.Where(entry => entry.Outcome == outcome);
        }

        if (filter.ActorType is { } actorType)
        {
            entries = entries.Where(entry => entry.ActorType == actorType);
        }

        if (filter.UserId is { } userId)
        {
            entries = entries.Where(entry => entry.UserId == new UserId(userId));
        }

        if (filter.DocumentId is { } documentId)
        {
            entries = entries.Where(entry => entry.DocumentId == documentId);
        }

        if (filter.EntityId is { } entityId)
        {
            entries = entries.Where(entry => entry.EntityId == entityId);
        }

        if (filter.EntityType is { } entityType)
        {
            entries = entries.Where(entry => entry.EntityType == entityType);
        }

        return entries;
    }

    private static IQueryable<AuditEntryDto> Project(IQueryable<AuditEntry> entries) =>
        entries.Select(entry => new AuditEntryDto(
            entry.Id.Value,
            entry.OccurredAt,
            entry.Action,
            entry.Outcome,
            entry.ActorType,
            (Guid?)entry.UserId!.Value.Value,
            null,
            entry.ShareLinkId,
            entry.EntityType,
            entry.EntityId,
            entry.DocumentId,
            entry.VersionId,
            entry.IpAddress,
            entry.UserAgent,
            entry.CorrelationId,
            entry.Metadata));
}
