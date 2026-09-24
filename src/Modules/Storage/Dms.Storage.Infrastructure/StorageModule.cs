using Dms.Application;
using Dms.Infrastructure;
using Dms.Infrastructure.Persistence;
using Dms.Storage.Application;
using Dms.Storage.Contracts;
using Dms.Storage.Infrastructure.Persistence;
using Dms.Storage.Infrastructure.Processing;
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

        // ClamAV when scanning is switched on, otherwise files are marked "skipped" (decision D9).
        var scanning = configuration.GetValue<bool>($"{StorageOptions.SectionName}:Scanning:Enabled");
        if (scanning)
        {
            services.AddSingleton<IMalwareScanner, ClamAvScanner>();
        }
        else
        {
            services.AddSingleton<IMalwareScanner, NullMalwareScanner>();
        }

        // Renderers are asked in order; the first that recognises the type renders it.
        services.AddSingleton<IPageRenderer, PdfPageRenderer>();
        services.AddSingleton<IPageRenderer, ImagePageRenderer>();
        services.AddHttpClient<OfficePageRenderer>(client => client.Timeout = TimeSpan.FromMinutes(5));
        services.AddSingleton<IPageRenderer>(sp => sp.GetRequiredService<OfficePageRenderer>());
        services.AddSingleton<IWatermarker, SkiaWatermarker>();

        services.AddScoped<IRenditionRepository, RenditionRepository>();
        services.AddScoped<IRenditionService, RenditionService>();
        services.AddScoped<IJobHandler, ProcessObjectJob>();

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
