using Dms.Application;
using Dms.Infrastructure.Events;
using Dms.Infrastructure.Jobs;
using Dms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Dms.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the shared building blocks: one data source, the per-scope session/unit of work,
    /// the dispatcher, and the job queue with its <c>infra</c> schema.
    /// </summary>
    public static IServiceCollection AddDmsBuildingBlocks(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(_ =>
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            return builder.Build();
        });

        services.TryAddTimeProvider();

        services.AddScoped<DbSession>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDispatcher, Dispatcher>();

        services.AddDmsModuleDbContext<InfraDbContext>(order: 0, name: "infra");
        services.AddSingleton<JobStore>();
        services.AddScoped<IJobQueue, JobQueue>();
        services.AddScoped<DatabaseInitializer>();

        return services;
    }

    /// <summary>
    /// Registers a module DbContext on the shared connection, together with its position in the
    /// deterministic migration order.
    /// </summary>
    public static IServiceCollection AddDmsModuleDbContext<TContext>(
        this IServiceCollection services,
        int order,
        string name)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>((serviceProvider, options) =>
        {
            var session = serviceProvider.GetRequiredService<DbSession>();
            options.UseNpgsql(
                session.Connection,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", name));
        });

        services.AddSingleton(new ModuleDatabaseDescriptor<TContext>(order, name));
        services.AddScoped<IModuleDatabase, ModuleDatabase<TContext>>();
        return services;
    }

    public static IServiceCollection AddRecurringJob(this IServiceCollection services, string type, TimeSpan interval)
    {
        services.AddSingleton(new RecurringJob(type, interval));
        return services;
    }

    private static IServiceCollection TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(descriptor => descriptor.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }

        return services;
    }
}
