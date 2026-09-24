using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dms.Infrastructure.Persistence;

/// <summary>A module's schema. <see cref="Order"/> fixes the deterministic migration order.</summary>
public interface IModuleDatabase
{
    int Order { get; }

    /// <summary>The module's schema.</summary>
    string Name { get; }

    RuntimeAccess RuntimeAccess { get; }

    Task MigrateAsync(CancellationToken cancellationToken);
}

/// <summary>What the runtime database role may do with a module's tables (section 4.10).</summary>
public enum RuntimeAccess
{
    /// <summary>SELECT, INSERT, UPDATE and DELETE. Never TRUNCATE or DDL.</summary>
    ReadWrite,

    /// <summary>SELECT and INSERT only: the audit log can be added to but never rewritten.</summary>
    AppendOnly,
}

public sealed record ModuleDatabaseDescriptor<TContext>(int Order, string Name, RuntimeAccess RuntimeAccess = RuntimeAccess.ReadWrite)
    where TContext : DbContext;

public sealed class ModuleDatabase<TContext>(TContext context, ModuleDatabaseDescriptor<TContext> descriptor)
    : IModuleDatabase
    where TContext : DbContext
{
    public int Order => descriptor.Order;

    public string Name => descriptor.Name;

    public RuntimeAccess RuntimeAccess => descriptor.RuntimeAccess;

    public Task MigrateAsync(CancellationToken cancellationToken) =>
        context.Database.MigrateAsync(cancellationToken);
}

/// <summary>Idempotent reference data. Runs after every module has migrated.</summary>
public interface IDataSeeder
{
    int Order { get; }

    string Name { get; }

    Task SeedAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Applies migrations, seeds, and grants the runtime role its rights. Used by the migrator
/// container and by the integration tests. The API host never migrates on startup.
/// </summary>
public sealed class DatabaseInitializer(
    IEnumerable<IModuleDatabase> modules,
    IEnumerable<IDataSeeder> seeders,
    DbSession session,
    IOptions<DatabaseRoleOptions> roleOptions,
    ILogger<DatabaseInitializer> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        foreach (var module in modules.OrderBy(module => module.Order))
        {
            logger.LogInformation("Migrating module schema {Module}.", module.Name);
            await module.MigrateAsync(cancellationToken);
        }
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await session.BeginAsync(cancellationToken);
        try
        {
            foreach (var seeder in seeders.OrderBy(seeder => seeder.Order))
            {
                logger.LogInformation("Seeding {Seeder}.", seeder.Name);
                await seeder.SeedAsync(cancellationToken);
            }

            await session.SaveChangesAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);
        }
        catch
        {
            await session.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Creates the runtime role when it is missing and grants it exactly what section 4.10 allows:
    /// DML on the module schemas, INSERT and SELECT on append-only ones, no DDL and no TRUNCATE
    /// anywhere. Re-running it resets the grants, so a right given by hand does not linger.
    /// </summary>
    public async Task GrantRuntimeAccessAsync(CancellationToken cancellationToken)
    {
        var options = roleOptions.Value;
        if (string.IsNullOrWhiteSpace(options.AppRole))
        {
            logger.LogWarning("No runtime database role configured (Dms:Database:AppRole); the API keeps the owner's rights.");
            return;
        }

        var role = RuntimeRoleSql.QuoteIdentifier(options.AppRole);
        var connection = session.Connection;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        logger.LogInformation("Granting runtime access to role {Role}.", options.AppRole);
        foreach (var statement in RuntimeRoleSql.Build(role, options, modules.OrderBy(module => module.Order).ToList()))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
