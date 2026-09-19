using Dms.Application;
using Dms.Audit.Application;
using Dms.Audit.Contracts;
using Dms.Audit.Infrastructure.Persistence;
using Dms.Infrastructure;
using Dms.Infrastructure.Persistence;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Dms.Audit.Infrastructure;

public static class AuditModule
{
    public const int MigrationOrder = 40;

    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        services.AddDmsModuleDbContext<AuditDbContext>(MigrationOrder, AuditDbContext.Schema);

        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditQueries, AuditQueries>();
        services.AddScoped<IQueryHandler<ListAuditEntriesQuery, Result<IReadOnlyList<AuditEntryDto>>>,
            ListAuditEntriesHandler>();

        services.AddScoped<IJobHandler, EnsureAuditPartitionsJob>();
        services.AddRecurringJob(EnsureAuditPartitionsJob.Type, TimeSpan.FromHours(12));

        return services;
    }
}

public sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args) => new(
        DesignTimeSupport.CreateOptions<AuditDbContext>(AuditDbContext.Schema),
        DesignTimeSupport.CreateSession());
}
