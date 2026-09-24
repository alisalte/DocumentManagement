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
        // Append-only: the runtime role may add audit rows and seals, never change or remove them.
        services.AddDmsModuleDbContext<AuditDbContext>(MigrationOrder, AuditDbContext.Schema, RuntimeAccess.AppendOnly);
        // A malformed key would otherwise surface only when the first seal is attempted.
        services.AddOptions<AuditOptions>()
            .BindConfiguration(AuditOptions.SectionName)
            .Validate(AuditOptions.HasValidKeys, "Dms:Audit:SealKey and PreviousSealKeys must be base64 of at least 32 bytes.")
            .ValidateOnStart();

        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditQueries, AuditQueries>();
        services.AddScoped<AuditUserNames>();
        services.AddSingleton<AuditSealKeys>();
        services.AddScoped<IAuditSealing, AuditSealing>();

        services.AddScoped<IQueryHandler<ListAuditEntriesQuery, Result<IReadOnlyList<AuditEntryDto>>>,
            ListAuditEntriesHandler>();
        services.AddScoped<IQueryHandler<ListAuditActionsQuery, Result<IReadOnlyList<string>>>, ListAuditActionsHandler>();
        services.AddScoped<ICommandHandler<ExportAuditCommand, Result<AuditExport>>, ExportAuditHandler>();
        services.AddScoped<IQueryHandler<GetSealStatusQuery, Result<SealStatusDto>>, GetSealStatusHandler>();
        services.AddScoped<ICommandHandler<VerifySealsCommand, Result<SealVerificationDto>>, VerifySealsHandler>();

        services.AddScoped<IJobHandler, EnsureAuditPartitionsJob>();
        services.AddRecurringJob(EnsureAuditPartitionsJob.Type, TimeSpan.FromHours(12));
        services.AddScoped<IJobHandler, AuditSealJob>();
        services.AddRecurringJob(AuditSealJob.Type, TimeSpan.FromMinutes(15));
        services.AddScoped<IJobHandler, AuditVerifyJob>();
        services.AddRecurringJob(AuditVerifyJob.Type, TimeSpan.FromDays(1));

        return services;
    }
}

public sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args) => new(
        DesignTimeSupport.CreateOptions<AuditDbContext>(AuditDbContext.Schema),
        DesignTimeSupport.CreateSession());
}
