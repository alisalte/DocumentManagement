using Dms.Audit.Application;
using Microsoft.EntityFrameworkCore;

namespace Dms.Audit.Infrastructure.Persistence;

public sealed class AuditQueries(AuditDbContext context) : IAuditQueries
{
    public async Task<IReadOnlyList<AuditEntryDto>> ListAsync(
        ListAuditEntriesQuery query,
        CancellationToken cancellationToken)
    {
        var entries = context.AuditEntries.AsNoTracking();

        if (query.From is { } from)
        {
            entries = entries.Where(entry => entry.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            entries = entries.Where(entry => entry.OccurredAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            entries = entries.Where(entry => entry.Action == query.Action);
        }

        if (query.UserId is { } userId)
        {
            entries = entries.Where(entry => entry.UserId!.Value.Value == userId);
        }

        if (query.DocumentId is { } documentId)
        {
            entries = entries.Where(entry => entry.DocumentId == documentId);
        }

        if (query.EntityId is { } entityId)
        {
            entries = entries.Where(entry => entry.EntityId == entityId);
        }

        return await entries
            .OrderByDescending(entry => entry.OccurredAt)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(entry => new AuditEntryDto(
                entry.Id.Value,
                entry.OccurredAt,
                entry.Action,
                entry.Outcome,
                entry.ActorType,
                entry.UserId!.Value.Value,
                entry.EntityType,
                entry.EntityId,
                entry.DocumentId,
                entry.IpAddress,
                entry.Metadata))
            .ToListAsync(cancellationToken);
    }
}
