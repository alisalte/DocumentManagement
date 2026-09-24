using Dms.Application;
using Dms.Audit.Infrastructure;
using Dms.Authorization.Infrastructure;
using Dms.DocumentTypes.Infrastructure;
using Dms.Documents.Infrastructure;
using Dms.Workflow.Infrastructure;
using Dms.Identity.Infrastructure;
using Dms.Infrastructure;
using Dms.Infrastructure.Persistence;
using Dms.Migrator;
using Dms.Notifications.Infrastructure;
using Dms.Search.Infrastructure;
using Dms.Sharing.Infrastructure;
using Dms.Storage.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Schema changes run here, never from the API host: the API's database role has no DDL rights, and
// a rolling deployment must not have several instances migrating at once.
var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Dms")
    ?? throw new InvalidOperationException(
        "No database connection string. Set ConnectionStrings__Dms in the environment.");

builder.Services.AddDmsBuildingBlocks(connectionString);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddAuthorizationModule();
builder.Services.AddAuditModule();
builder.Services.AddStorageModule(builder.Configuration);
builder.Services.AddDocumentTypesModule();
builder.Services.AddDocumentsModule();
builder.Services.AddWorkflowModule();
builder.Services.AddSharingModule(builder.Configuration);
builder.Services.AddNotificationsModule();
builder.Services.AddSearchModule(builder.Configuration);
builder.Services.AddScoped<ICurrentUser, SystemCurrentUser>();

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();

var logger = scope.ServiceProvider.GetRequiredService<ILogger<DatabaseInitializer>>();
var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();

try
{
    await initializer.MigrateAsync(CancellationToken.None);
    await initializer.SeedAsync(CancellationToken.None);
    await initializer.GrantRuntimeAccessAsync(CancellationToken.None);
    logger.LogInformation("Database is up to date.");
    return 0;
}
catch (Exception exception)
{
    logger.LogCritical(exception, "Migration failed.");
    return 1;
}
