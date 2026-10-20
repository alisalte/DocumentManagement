using Dms.Audit.Contracts;
using Dms.Authorization.Infrastructure.Persistence;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Dms.Authorization.Infrastructure;

/// <summary>Names of roles for the audit viewer.</summary>
public sealed class AuthorizationAuditNames(AuthorizationDbContext context) : IAuditNameSource
{
    public async Task<IReadOnlyDictionary<Guid, string>> FindNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        var lookups = new Func<List<Guid>, Task<IEnumerable<(Guid Id, string Name)>>>[]
        {
            async wanted =>
            {
                var keys = wanted.Select(id => new RoleId(id)).ToList();
                return (await context.Roles.IgnoreQueryFilters().AsNoTracking()
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
