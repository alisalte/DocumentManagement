using Dms.Audit.Contracts;
using Dms.Workflow.Contracts;
using Dms.Workflow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dms.Workflow.Infrastructure;

/// <summary>Names of workflows for the audit viewer.</summary>
public sealed class WorkflowAuditNames(WorkflowDbContext context) : IAuditNameSource
{
    public async Task<IReadOnlyDictionary<Guid, string>> FindNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        var lookups = new Func<List<Guid>, Task<IEnumerable<(Guid Id, string Name)>>>[]
        {
            async wanted =>
            {
                var keys = wanted.Select(id => new WorkflowId(id)).ToList();
                return (await context.Workflows.IgnoreQueryFilters().AsNoTracking()
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
