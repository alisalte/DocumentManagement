using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dms.Infrastructure.Persistence;

/// <summary>A module's schema. <see cref="Order"/> fixes the deterministic migration order.</summary>
public interface IModuleDatabase
{
    int Order { get; }

    string Name { get; }

    Task MigrateAsync(CancellationToken cancellationToken);
}

public sealed record ModuleDatabaseDescriptor<TContext>(int Order, string Name)
    where TContext : DbContext;

public sealed class ModuleDatabase<TContext>(TContext context, ModuleDatabaseDescriptor<TContext> descriptor)
    : IModuleDatabase
    where TContext : DbContext
{
    public int Order => descriptor.Order;

    public string Name => descriptor.Name;

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
/// Applies migrations and seeds. Used by the migrator container and by the integration tests.
/// The API host never migrates on startup.
/// </summary>
public sealed class DatabaseInitializer(
    IEnumerable<IModuleDatabase> modules,
    IEnumerable<IDataSeeder> seeders,
    DbSession session,
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
}
