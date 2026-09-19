using Dms.Application;
using Dms.Infrastructure;
using Dms.Infrastructure.Persistence;
using Dms.Storage.Application;
using Dms.Storage.Contracts;
using Dms.Storage.Infrastructure.Persistence;
using Dms.Storage.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dms.Storage.Infrastructure;

public static class StorageModule
{
    public const int MigrationOrder = 25;

    public static IServiceCollection AddStorageModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

        services.AddDmsModuleDbContext<StorageDbContext>(MigrationOrder, StorageDbContext.Schema);
        services.AddScoped<IStorageObjectRepository, StorageObjectRepository>();
        services.AddScoped<IStorageService, StorageService>();

        var provider = configuration[$"{StorageOptions.SectionName}:Provider"] ?? "s3";
        if (string.Equals(provider, "filesystem", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IFileStorage, FileSystemFileStorage>();
        }
        else
        {
            services.AddSingleton<S3FileStorage>();
            services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<S3FileStorage>());
        }

        services.AddSingleton<IMalwareScanner, NullMalwareScanner>();

        services.AddScoped<IJobHandler, ScanStorageObjectJob>();
        services.AddScoped<IJobHandler, PurgeStorageObjectsJob>();
        services.AddScoped<IJobHandler, CollectStagedUploadsJob>();
        services.AddRecurringJob(CollectStagedUploadsJob.Type, TimeSpan.FromHours(1));
        services.AddRecurringJob(PurgeStorageObjectsJob.Type, TimeSpan.FromHours(6));

        return services;
    }
}

public sealed class StorageDbContextFactory : IDesignTimeDbContextFactory<StorageDbContext>
{
    public StorageDbContext CreateDbContext(string[] args) => new(
        DesignTimeSupport.CreateOptions<StorageDbContext>(StorageDbContext.Schema),
        DesignTimeSupport.CreateSession());
}
