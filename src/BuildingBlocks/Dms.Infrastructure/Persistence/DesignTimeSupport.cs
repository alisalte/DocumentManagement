using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Dms.Infrastructure.Persistence;

/// <summary>
/// Support for <c>dotnet ef migrations add</c>. Design time never connects to a database, but EF
/// still has to construct the context, and our contexts require a <see cref="DbSession"/>.
/// </summary>
public static class DesignTimeSupport
{
    public const string FallbackConnectionString =
        "Host=localhost;Port=5433;Database=dms;Username=dms;Password=dms";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("DMS_DESIGN_CONNECTION") ?? FallbackConnectionString;

    public static DbSession CreateSession() => new(NpgsqlDataSource.Create(ConnectionString));

    public static DbContextOptions<TContext> CreateOptions<TContext>(string schema)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(
                ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema))
            .Options;
}
