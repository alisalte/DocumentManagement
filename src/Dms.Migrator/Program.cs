using System.Data.Common;
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
using Npgsql;

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

var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Dms.Migrator");
var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();

LogConnectionTarget(logger, connectionString);

try
{
    // Compose marks Postgres healthy before other containers can always reach it (slow bridge,
    // nested Docker). Wait for a real TCP+auth handshake before migrating.
    await WaitForDatabaseAsync(connectionString, logger, CancellationToken.None);

    await initializer.MigrateAsync(CancellationToken.None);
    await initializer.SeedAsync(CancellationToken.None);
    await initializer.GrantRuntimeAccessAsync(CancellationToken.None);
    logger.LogInformation("Database is up to date.");
    return 0;
}
catch (Exception exception)
{
    logger.LogCritical(exception, "Migration failed.");
    logger.LogCritical(
        "If this is Docker Compose and the error is a connection timeout to Host=postgres, " +
        "inter-container networking may be broken (common in nested Docker). From deploy/: " +
        "docker compose -f docker-compose.yml -f compose.nested.yml up -d --build");
    return 1;
}

static void LogConnectionTarget(ILogger logger, string connectionString)
{
    try
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        logger.LogInformation(
            "Migrator connecting to Host={Host} Port={Port} Database={Database} Username={Username}.",
            builder.Host,
            builder.Port,
            builder.Database,
            builder.Username);
    }
    catch (Exception exception)
    {
        logger.LogWarning(exception, "Could not parse the database connection string for logging.");
    }
}

static async Task WaitForDatabaseAsync(string connectionString, ILogger logger, CancellationToken cancellationToken)
{
    const int attempts = 45;
    var delay = TimeSpan.FromSeconds(2);

    for (var attempt = 1; attempt <= attempts; attempt++)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            logger.LogInformation("Database is reachable (attempt {Attempt}/{Attempts}).", attempt, attempts);
            return;
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or DbException)
        {
            logger.LogWarning(
                exception,
                "Database not reachable yet (attempt {Attempt}/{Attempts}); retrying in {DelaySeconds}s.",
                attempt,
                attempts,
                delay.TotalSeconds);
            if (attempt == attempts)
            {
                throw;
            }

            await Task.Delay(delay, cancellationToken);
        }
    }
}
