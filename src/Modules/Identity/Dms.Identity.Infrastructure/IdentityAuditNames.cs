using Dms.Audit.Contracts;
using Dms.Identity.Infrastructure.Persistence;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Dms.Identity.Infrastructure;

/// <summary>Names of users and groups for the audit viewer.</summary>
public sealed class IdentityAuditNames(IdentityDbContext context) : IAuditNameSource
{
    public async Task<IReadOnlyDictionary<Guid, string>> FindNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        var lookups = new Func<List<Guid>, Task<IEnumerable<(Guid Id, string Name)>>>[]
        {
            async wanted =>
            {
                var keys = wanted.Select(id => new UserId(id)).ToList();
                return (await context.Users.IgnoreQueryFilters().AsNoTracking()
                        .Where(row => keys.Contains(row.Id))
                        .Select(row => new { row.Id, row.DisplayName })
                        .ToListAsync(cancellationToken))
                    .Select(row => (row.Id.Value, row.DisplayName));
            },
            async wanted =>
            {
                var keys = wanted.Select(id => new GroupId(id)).ToList();
                return (await context.Groups.IgnoreQueryFilters().AsNoTracking()
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
