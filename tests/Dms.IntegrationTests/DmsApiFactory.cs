using Dms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Dms.IntegrationTests;

/// <summary>
/// Boots the real host against a throwaway PostgreSQL database, applies the real migrations and
/// runs the real seeders. Nothing is faked: these tests exercise the same code path as production.
///
/// Also like production, the schema is applied by the owner and the host then runs as the
/// runtime role with only the rights the migrator granted it (section 4.10), so every test also
/// checks that those grants are enough, and the audit tests that they are no more than that.
///
/// The server is picked up from DMS_TEST_POSTGRES, defaulting to the development container on
/// port 5433 (see deploy/docker-compose.yml). Each run gets its own database, which is dropped
/// afterwards, so runs never interfere with each other.
/// </summary>
public sealed class DmsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = "BootstrapAdminPassword!1";

    /// <summary>Roles are per server, not per database: every test run shares this one.</summary>
    public const string AppRole = "dms_app_test";
    public const string AppRolePassword = "dms-app-test-password";

    private readonly string _databaseName = $"dms_test_{Guid.NewGuid():N}";

    /// <summary>Filesystem storage in a throwaway directory: real bytes on a real disk, no object store needed.</summary>
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"dms_test_{Guid.NewGuid():N}");

    private static string AdminConnectionString =>
        Environment.GetEnvironmentVariable("DMS_TEST_POSTGRES")
        ?? "Host=localhost;Port=5433;Username=dms;Password=dms;Database=postgres";

    /// <summary>The owner's connection, for tests that arrange or inspect rows directly.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>What the seeder set, before the factory cleared it for the rest of the suite.</summary>
    public bool BootstrapAdminHadToChangePassword { get; private set; }

    /// <summary>The runtime role's connection, which the host uses.</summary>
    public string AppConnectionString { get; private set; } = string.Empty;

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
        AppConnectionString = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Username = AppRole,
            Password = AppRolePassword,
        }.ConnectionString;

        // The host reads configuration before WebApplicationFactory can inject any, so the test
        // settings go through the environment, exactly like a real deployment.
        Environment.SetEnvironmentVariable("ConnectionStrings__Dms", ConnectionString);
        Environment.SetEnvironmentVariable("Dms__Database__AppRole", AppRole);
        Environment.SetEnvironmentVariable("Dms__Database__AppRolePassword", AppRolePassword);

        // A keyed seal chain, as production should have (32 random-looking bytes).
        Environment.SetEnvironmentVariable("Dms__Audit__SealKey", Convert.ToBase64String("test-audit-seal-key-0123456789ab"u8.ToArray()));
        Environment.SetEnvironmentVariable("Dms__Jwt__SigningKey", new string('k', 64));
        Environment.SetEnvironmentVariable("Dms__Bootstrap__AdminPassword", AdminPassword);

        // API role only: the background worker would compete with the tests for jobs.
        Environment.SetEnvironmentVariable("Dms__Role", "api");

        Environment.SetEnvironmentVariable("Dms__Storage__Provider", "filesystem");
        Environment.SetEnvironmentVariable("Dms__Storage__FileSystem__RootPath", StorageRoot);

        // Every test signs in, and they all come from the same address. The limiter itself is
        // covered by its own test rather than by throttling the whole suite.
        Environment.SetEnvironmentVariable("Dms__RateLimits__LoginPerMinute", "10000");
        Environment.SetEnvironmentVariable("Dms__RateLimits__ShareLinkOpensPerMinute", "10000");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        // Development logs every SQL statement, which drowns the test output.
        Environment.SetEnvironmentVariable(
            "Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command",
            "Warning");
        Environment.SetEnvironmentVariable("Logging__LogLevel__Microsoft.Hosting.Lifetime", "Warning");

        // The migrator's job, as the owner, in a host of its own; the tests' host is built after
        // this, as the runtime role.
        await using (var migrator = new WebApplicationFactory<Program>())
        {
            await using var scope = migrator.Services.CreateAsyncScope();
            var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
            await initializer.MigrateAsync(CancellationToken.None);
            await initializer.SeedAsync(CancellationToken.None);
            await initializer.GrantRuntimeAccessAsync(CancellationToken.None);
        }

        // The seeded administrator must change the password at first sign-in, and the server
        // enforces it. Tests stand for an installation where that has happened; the gate itself
        // is covered by its own test.
        await using (var connection = await OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE identity.users u SET must_change_password = false
                  FROM identity.users before
                 WHERE u.id = before.id AND u.normalized_username = @admin
                RETURNING before.must_change_password
                """;
            command.Parameters.AddWithValue("admin", AdminUsername.ToUpperInvariant());
            BootstrapAdminHadToChangePassword = (bool)(await command.ExecuteScalarAsync())!;
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__Dms", AppConnectionString);
        _ = Services;
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

    /// <summary>A connection as the runtime role, to show what the application itself can and cannot do.</summary>
    public async Task<NpgsqlConnection> OpenAppConnectionAsync()
    {
        var connection = new NpgsqlConnection(AppConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DmsApiFactory>
{
    public const string Name = "database";
}
