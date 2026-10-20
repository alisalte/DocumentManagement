using Dms.Audit.Contracts;
using Dms.Documents.Contracts;
using Dms.Documents.Infrastructure.Persistence;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Dms.Documents.Infrastructure;

/// <summary>
/// Names for the audit viewer: document titles (deleted ones too, their history still matters),
/// version labels, folders, records, retention policies, legal holds and import jobs.
/// </summary>
public sealed class DocumentAuditNames(DocumentsDbContext context) : IAuditNameSource
{
    public async Task<IReadOnlyDictionary<Guid, string>> FindNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        var lookups = new Func<List<Guid>, Task<IEnumerable<(Guid Id, string Name)>>>[]
        {
            async wanted =>
            {
                var keys = wanted.Select(id => new DocumentId(id)).ToList();
                return (await context.Documents.IgnoreQueryFilters().AsNoTracking()
                        .Where(row => keys.Contains(row.Id))
                        .Select(row => new { row.Id, row.Title })
                        .ToListAsync(cancellationToken))
                    .Select(row => (row.Id.Value, row.Title));
            },
            async wanted =>
            {
                var keys = wanted.Select(id => new DocumentVersionId(id)).ToList();
                return (await context.DocumentVersions.IgnoreQueryFilters().AsNoTracking()
                        .Where(row => keys.Contains(row.Id))
                        .Select(row => new { row.Id, row.VersionNumber, row.RevisionNumber })
                        .ToListAsync(cancellationToken))
                    .Select(row => (row.Id.Value, $"V{row.VersionNumber}.{row.RevisionNumber}"));
            },
            async wanted =>
            {
                var keys = wanted.Select(id => new CategoryId(id)).ToList();
                return (await context.Categories.IgnoreQueryFilters().AsNoTracking()
                        .Where(row => keys.Contains(row.Id))
                        .Select(row => new { row.Id, row.Name })
                        .ToListAsync(cancellationToken))
                    .Select(row => (row.Id.Value, row.Name));
            },
            async wanted =>
            {
                var keys = wanted.Select(id => new RecordId(id)).ToList();
                return (await context.Records.IgnoreQueryFilters().AsNoTracking()
                        .Where(row => keys.Contains(row.Id))
                        .Select(row => new { row.Id, row.Title })
                        .ToListAsync(cancellationToken))
                    .Select(row => (row.Id.Value, row.Title));
            },
            async wanted =>
            {
                var keys = wanted.Select(id => new RecordClassId(id)).ToList();
                return (await context.RecordClasses.IgnoreQueryFilters().AsNoTracking()
                        .Where(row => keys.Contains(row.Id))
                        .Select(row => new { row.Id, row.Name })
                        .ToListAsync(cancellationToken))
                    .Select(row => (row.Id.Value, row.Name));
            },
            async wanted =>
            {
                var keys = wanted.Select(id => new RecordSeriesId(id)).ToList();
                return (await context.RecordSeries.IgnoreQueryFilters().AsNoTracking()
                        .Where(row => keys.Contains(row.Id))
                        .Select(row => new { row.Id, row.Name })
                        .ToListAsync(cancellationToken))
                    .Select(row => (row.Id.Value, row.Name));
            },
            async wanted =>
            {
                var keys = wanted.Select(id => new RetentionPolicyId(id)).ToList();
                return (await context.RetentionPolicies.IgnoreQueryFilters().AsNoTracking()
                        .Where(row => keys.Contains(row.Id))
                        .Select(row => new { row.Id, row.Name })
                        .ToListAsync(cancellationToken))
                    .Select(row => (row.Id.Value, row.Name));
            },
            async wanted =>
            {
                var keys = wanted.Select(id => new LegalHoldId(id)).ToList();
                return (await context.LegalHolds.IgnoreQueryFilters().AsNoTracking()
                        .Where(row => keys.Contains(row.Id))
                        .Select(row => new { row.Id, row.Reason })
                        .ToListAsync(cancellationToken))
                    .Select(row => (row.Id.Value, row.Reason));
            },
            async wanted =>
            {
                var keys = wanted.Select(id => new ImportJobId(id)).ToList();
                return (await context.ImportJobs.IgnoreQueryFilters().AsNoTracking()
                        .Where(row => keys.Contains(row.Id))
                        .Select(row => new { row.Id, row.Name })
                        .ToListAsync(cancellationToken))
                    .Select(row => (row.Id.Value, row.Name));
            },
        };

        foreach (var lookup in lookups)
        {
            var wanted = ids.Where(id => !names.ContainsKey(id)).ToList();
            if (wanted.Count == 0)
            {
                break;
            }

            foreach (var (id, name) in await lookup(wanted))
            {
                names.TryAdd(id, name);
            }
        }

        return names;
    }
}
