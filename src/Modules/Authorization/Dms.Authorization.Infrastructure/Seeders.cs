using Dms.Authorization.Contracts;
using Dms.Authorization.Domain;
using Dms.Authorization.Infrastructure.Persistence;
using Dms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dms.Authorization.Infrastructure;

/// <summary>
/// Mirrors the code-defined catalog into the database. The code is the source of truth; this table
/// exists so that ACL and role rows can have a real foreign key.
/// </summary>
public sealed class PermissionCatalogSeeder(AuthorizationDbContext context) : IDataSeeder
{
    public int Order => 5;

    public string Name => "permission catalog";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var existing = await context.Permissions.ToDictionaryAsync(
            permission => permission.Code,
            cancellationToken);

        foreach (var definition in PermissionCatalog.All)
        {
            if (existing.TryGetValue(definition.Code, out var record))
            {
                record.Scope = definition.Scope.ToString();
                record.RequiresView = definition.RequiresView;
                record.Description = definition.Description;
                continue;
            }

            context.Permissions.Add(new PermissionRecord
            {
                Code = definition.Code,
                Scope = definition.Scope.ToString(),
                RequiresView = definition.RequiresView,
                Description = definition.Description,
            });
        }
    }
}

/// <summary>Built-in roles. Administrators are still separate: being in a role grants no ACL bypass.</summary>
public sealed class SystemRolesSeeder(AuthorizationDbContext context, TimeProvider timeProvider) : IDataSeeder
{
    public int Order => 10;

    public string Name => "system roles";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        await EnsureRoleAsync(
            "ADMINISTRATOR",
            "Administrator",
            "Manages users, groups, roles and configuration.",
            PermissionCatalog.All
                .Where(definition => definition.Scope is PermissionScope.System or PermissionScope.Both)
                .Select(definition => definition.Code),
            now,
            cancellationToken);

        await EnsureRoleAsync(
            "AUDITOR",
            "Auditor",
            "Reads and exports the audit log.",
            [PermissionCodes.AuditView, PermissionCodes.AuditExport],
            now,
            cancellationToken);
    }

    private async Task EnsureRoleAsync(
        string code,
        string name,
        string description,
        IEnumerable<string> permissions,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var role = await context.Roles.FirstOrDefaultAsync(candidate => candidate.Code == code, cancellationToken);
        if (role is null)
        {
            role = Role.Create(code, name, description, isSystem: true, now);
            context.Roles.Add(role);
        }

        foreach (var permission in permissions)
        {
            role.Grant(permission);
        }
    }
}
