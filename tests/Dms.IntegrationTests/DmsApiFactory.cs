using Dms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Dms.IntegrationTests;

/// <summary>
/// Boots the real host against a throwaway PostgreSQL database, applies the real migrations and
/// runs the real seeders. Nothing is faked: these tests exercise the same code path as production.
///
/// The server is picked up from DMS_TEST_POSTGRES, defaulting to the development container on
/// port 5433 (see deploy/docker-compose.yml). Each run gets its own database, which is dropped
/// afterwards, so runs never interfere with each other.
/// </summary>
public sealed class DmsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = "BootstrapAdminPassword!1";

    private readonly string _databaseName = $"dms_test_{Guid.NewGuid():N}";

    /// <summary>Filesystem storage in a throwaway directory: real bytes on a real disk, no object store needed.</summary>
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"dms_test_{Guid.NewGuid():N}");

    private static string AdminConnectionString =>
        Environment.GetEnvironmentVariable("DMS_TEST_POSTGRES")
        ?? "Host=localhost;Port=5433;Username=dms;Password=dms;Database=postgres";

    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString);
        await using (var connection = new NpgsqlConnection(builder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""CREATE DATABASE "{_databaseName}";""";
            await command.ExecuteNonQueryAsync();
        }

        builder.Database = _databaseName;
        ConnectionString = builder.ConnectionString;

        // The host reads configuration before WebApplicationFactory can inject any, so the test
        // settings go through the environment, exactly like a real deployment.
        Environment.SetEnvironmentVariable("ConnectionStrings__Dms", ConnectionString);
        Environment.SetEnvironmentVariable("Dms__Jwt__SigningKey", new string('k', 64));
        Environment.SetEnvironmentVariable("Dms__Bootstrap__AdminPassword", AdminPassword);

        // API role only: the background worker would compete with the tests for jobs.
        Environment.SetEnvironmentVariable("Dms__Role", "api");

        Environment.SetEnvironmentVariable("Dms__Storage__Provider", "filesystem");
        Environment.SetEnvironmentVariable("Dms__Storage__FileSystem__RootPath", StorageRoot);

        // Every test signs in, and they all come from the same address. The limiter itself is
        // covered by its own test rather than by throttling the whole suite.
        Environment.SetEnvironmentVariable("Dms__RateLimits__LoginPerMinute", "10000");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        // Development logs every SQL statement, which drowns the test output.
        Environment.SetEnvironmentVariable(
            "Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command",
            "Warning");
        Environment.SetEnvironmentVariable("Logging__LogLevel__Microsoft.Hosting.Lifetime", "Warning");

        await using var scope = Services.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        await initializer.MigrateAsync(CancellationToken.None);
        await initializer.SeedAsync(CancellationToken.None);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""DROP DATABASE IF EXISTS "{_databaseName}" WITH (FORCE);""";
        await command.ExecuteNonQueryAsync();

        if (Directory.Exists(StorageRoot))
        {
            Directory.Delete(StorageRoot, recursive: true);
        }
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DmsApiFactory>
{
    public const string Name = "database";
}
