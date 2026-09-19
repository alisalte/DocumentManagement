using Npgsql;
using Shouldly;

namespace Dms.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class SchemaTests(DmsApiFactory factory)
{
    [Theory]
    [InlineData("infra")]
    [InlineData("identity")]
    [InlineData("authz")]
    [InlineData("audit")]
    public async Task Every_module_schema_is_migrated(string schema)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""SELECT count(*) FROM "{schema}".__ef_migrations_history;""";

        var applied = (long)(await command.ExecuteScalarAsync())!;
        applied.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_audit_table_is_partitioned_by_month()
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*) FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'audit' AND c.relname LIKE 'audit_logs\_%';
            """;

        var partitions = (long)(await command.ExecuteScalarAsync())!;

        // Three monthly partitions plus the default catch-all.
        partitions.ShouldBeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task The_permission_catalog_is_seeded_from_code()
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM authz.permissions;";

        var permissions = (long)(await command.ExecuteScalarAsync())!;
        permissions.ShouldBe(Authorization.Contracts.PermissionCatalog.All.Count);
    }

    [Fact]
    public async Task Roles_only_ever_hold_system_scoped_permissions()
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*) FROM authz.role_permissions rp
            JOIN authz.permissions p ON p.code = rp.permission_code
            WHERE p.scope = 'Resource';
            """;

        var offenders = (long)(await command.ExecuteScalarAsync())!;
        offenders.ShouldBe(0);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class AuditAppendOnlyTests(DmsApiFactory factory)
{
    private async Task<Guid> InsertRowAsync(NpgsqlConnection connection)
    {
        var id = Guid.CreateVersion7();
        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO audit.audit_logs (id, occurred_at, actor_type, action, outcome, metadata)
            VALUES (@id, now(), 'SYSTEM', 'TEST_EVENT', 'SUCCESS', '{}'::jsonb);
            """;
        insert.Parameters.AddWithValue("id", id);
        await insert.ExecuteNonQueryAsync();
        return id;
    }

    [Fact]
    public async Task An_audit_row_cannot_be_updated()
    {
        await using var connection = await factory.OpenConnectionAsync();
        var id = await InsertRowAsync(connection);

        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE audit.audit_logs SET action = 'TAMPERED' WHERE id = @id;";
        update.Parameters.AddWithValue("id", id);

        var exception = await Should.ThrowAsync<PostgresException>(update.ExecuteNonQueryAsync());
        exception.SqlState.ShouldBe("42501");
        exception.MessageText.ShouldContain("append-only");
    }

    [Fact]
    public async Task An_audit_row_cannot_be_deleted()
    {
        await using var connection = await factory.OpenConnectionAsync();
        var id = await InsertRowAsync(connection);

        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM audit.audit_logs WHERE id = @id;";
        delete.Parameters.AddWithValue("id", id);

        var exception = await Should.ThrowAsync<PostgresException>(delete.ExecuteNonQueryAsync());
        exception.SqlState.ShouldBe("42501");
    }
}
