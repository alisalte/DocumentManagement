using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dms.Application;
using Dms.Audit.Application;
using Dms.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>
/// Phase 7 exit criteria: append-only enforcement at the database role level, the seal chain
/// catching edits and back-dated rows, the audited export, and an audit row for every operation.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AuditHardeningTests(DmsApiFactory factory)
{
    private static async Task<PostgresException> ShouldFailAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteScalarAsync();
    }

    private static Task ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters) =>
        ScalarAsync(connection, sql, parameters);

    [Fact]
    public async Task The_host_runs_as_the_runtime_role()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<DbSession>();
        await session.Connection.OpenAsync();
        await using var command = session.Connection.CreateCommand();
        command.CommandText = "SELECT current_user";
        (await command.ExecuteScalarAsync()).ShouldBe(DmsApiFactory.AppRole);
    }

    [Fact]
    public async Task The_runtime_role_can_only_append_to_the_audit_log()
    {
        await (await factory.AdminAsync()).GetAsync("/api/v1/auth/me");
        await using var app = await factory.OpenAppConnectionAsync();

        // The rights are missing, not merely blocked by a trigger: 42501 insufficient_privilege.
        (await ShouldFailAsync(app, "UPDATE audit.audit_logs SET action = 'X'")).SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await ShouldFailAsync(app, "DELETE FROM audit.audit_logs")).SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await ShouldFailAsync(app, "TRUNCATE audit.audit_logs")).SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await ShouldFailAsync(app, "DELETE FROM audit.audit_seals")).SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);

        // Nor any DDL, in any module schema.
        (await ShouldFailAsync(app, "CREATE TABLE audit.sneaky (id int)")).SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await ShouldFailAsync(app, "CREATE TABLE documents.sneaky (id int)")).SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await ShouldFailAsync(app, "ALTER TABLE audit.audit_logs DISABLE TRIGGER USER")).SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await ShouldFailAsync(app, "TRUNCATE infra.jobs")).SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);

        // But it can read, and add next months' partitions through the definer function.
        ((long)(await ScalarAsync(app, "SELECT count(*) FROM audit.audit_logs"))!).ShouldBeGreaterThan(0);
        await ExecuteAsync(app, "SELECT audit.ensure_partition((now() + interval '5 months')::date)");

        // Even the owner meets the triggers, on the table and on each partition.
        await using var owner = await factory.OpenConnectionAsync();
        (await ShouldFailAsync(owner, "UPDATE audit.audit_logs SET action = 'X'")).MessageText.ShouldContain("append-only");
        (await ShouldFailAsync(owner, "TRUNCATE audit.audit_logs")).MessageText.ShouldContain("append-only");
        var partition = (string)(await ScalarAsync(owner,
            "SELECT inhrelid::regclass::text FROM pg_inherits WHERE inhparent = 'audit.audit_logs'::regclass ORDER BY 1 DESC LIMIT 1"))!;
        (await ShouldFailAsync(owner, $"TRUNCATE {partition}")).MessageText.ShouldContain("append-only");
    }

    [Fact]
    public async Task Seals_catch_edited_deleted_and_back_dated_rows()
    {
        await using var owner = await factory.OpenConnectionAsync();

        // Rows a few hours old, so their periods are complete and can be sealed now.
        var id = Guid.CreateVersion7();
        var at = DateTimeOffset.UtcNow.AddHours(-5);
        await ExecuteAsync(
            owner,
            """
            INSERT INTO audit.audit_logs (id, occurred_at, actor_type, action, outcome, metadata)
            VALUES (@id, @at, 'SYSTEM', 'SEAL_PROBE', 'SUCCESS', '{"n": 1}')
            """,
            ("id", id),
            ("at", at));

        await SealAsync();

        var admin = await factory.AdminAsync();
        var status = await admin.GetFromJsonAsync<JsonElement>("/api/v1/audit/seals/status");
        status.GetProperty("keyed").GetBoolean().ShouldBeTrue();
        status.GetProperty("algorithm").GetString().ShouldBe("HMAC-SHA256");
        status.GetProperty("sealedUntil").GetDateTimeOffset().ShouldBeGreaterThan(at);

        var window = new { from = at.AddHours(-1), to = at.AddHours(1) };
        var intact = await VerifyAsync(admin, window);
        intact.GetProperty("intact").GetBoolean().ShouldBeTrue(intact.GetRawText());
        intact.GetProperty("sealsChecked").GetInt32().ShouldBeGreaterThan(0);

        // An edit, made the only way left: as a superuser with the triggers switched off.
        await AsReplicaAsync(owner, "UPDATE audit.audit_logs SET metadata = '{\"n\": 2}' WHERE id = @id", ("id", id));
        var edited = await VerifyAsync(admin, window);
        edited.GetProperty("intact").GetBoolean().ShouldBeFalse();
        edited.GetProperty("problems")[0].GetProperty("kind").GetString().ShouldBe("RowsChanged");

        // The runtime role can insert audit rows, but not a verification record the status trusts.
        await using (var app = await factory.OpenAppConnectionAsync())
        {
            await ExecuteAsync(
                app,
                $$"""
                INSERT INTO audit.audit_logs (id, occurred_at, actor_type, action, outcome, metadata)
                VALUES (@id, now(), 'SYSTEM', 'AUDIT_SEALS_VERIFIED', 'SUCCESS',
                        '{"checkedAt": {{DateTimeOffset.UtcNow.UtcTicks}}, "proof": "Zm9yZ2Vk"}')
                """,
                ("id", Guid.CreateVersion7()));
        }

        (await admin.GetFromJsonAsync<JsonElement>("/api/v1/audit/seals/status")).GetProperty("lastVerificationIntact").GetBoolean().ShouldBeFalse();
        await AsReplicaAsync(owner, "UPDATE audit.audit_logs SET metadata = '{\"n\": 1}' WHERE id = @id", ("id", id));
        (await VerifyAsync(admin, window)).GetProperty("intact").GetBoolean().ShouldBeTrue();

        // A back-dated insert, which the runtime role could make: the period no longer adds up.
        var forged = Guid.CreateVersion7();
        await using (var app = await factory.OpenAppConnectionAsync())
        {
            await ExecuteAsync(
                app,
                """
                INSERT INTO audit.audit_logs (id, occurred_at, actor_type, action, outcome, metadata)
                VALUES (@id, @at, 'SYSTEM', 'SEAL_PROBE', 'SUCCESS', '{}')
                """,
                ("id", forged),
                ("at", at.AddMinutes(1)));
        }

        var backDated = await VerifyAsync(admin, window);
        backDated.GetProperty("intact").GetBoolean().ShouldBeFalse();
        backDated.GetProperty("problems")[0].GetProperty("detail").GetString()!.ShouldContain("found");
        await AsReplicaAsync(owner, "DELETE FROM audit.audit_logs WHERE id = @id", ("id", forged));

        // A deleted row is caught the same way; put it back afterwards.
        await AsReplicaAsync(owner, "DELETE FROM audit.audit_logs WHERE id = @id", ("id", id));
        (await VerifyAsync(admin, window)).GetProperty("intact").GetBoolean().ShouldBeFalse();
        await AsReplicaAsync(
            owner,
            """
            INSERT INTO audit.audit_logs (id, occurred_at, actor_type, action, outcome, metadata)
            VALUES (@id, @at, 'SYSTEM', 'SEAL_PROBE', 'SUCCESS', '{"n": 1}')
            """,
            ("id", id),
            ("at", at));

        var restored = await VerifyAsync(admin, window);
        restored.GetProperty("intact").GetBoolean().ShouldBeTrue(restored.GetRawText());

        // Each verification is audited, and the status reports the last one.
        (await admin.GetFromJsonAsync<JsonElement>("/api/v1/audit/seals/status")).GetProperty("lastVerificationIntact").GetBoolean().ShouldBeTrue();
        await factory.ShouldHaveAuditAsync("AUDIT_SEALS_VERIFIED");

        // A seal cannot be rewritten or removed by the application.
        await using var appConnection = await factory.OpenAppConnectionAsync();
        (await ShouldFailAsync(appConnection, "UPDATE audit.audit_seals SET row_count = 0")).SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task The_export_is_permission_checked_bounded_audited_and_spreadsheet_safe()
    {
        var admin = await factory.AdminAsync();
        var (user, _) = await factory.CreateUserAsync("audit.export.user");
        var from = DateTimeOffset.UtcNow.AddHours(-1).ToString("O");
        var to = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O");
        var range = $"from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";

        (await user.GetAsync($"/api/v1/audit/export?{range}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.GetAsync("/api/v1/audit/export")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.GetAsync($"/api/v1/audit/export?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddYears(2).ToString("O"))}"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A user agent that would be a formula in a spreadsheet.
        var hostile = factory.CreateClient();
        hostile.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "=cmd");
        (await hostile.PostAsJsonAsync("/api/v1/auth/login", new { username = "nobody.here", password = "wrong-password-1" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var csv = await admin.GetAsync($"/api/v1/audit/export?{range}&action=LOGIN_FAILED");
        csv.StatusCode.ShouldBe(HttpStatusCode.OK, await csv.Content.ReadAsStringAsync());
        csv.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        var bytes = await csv.Content.ReadAsByteArrayAsync();
        bytes.Take(3).ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
        var text = Encoding.UTF8.GetString(bytes);
        text.ShouldStartWith("﻿occurred_at,action,outcome");
        text.ShouldContain(",'=cmd,");
        text.ShouldNotContain(",=cmd,");

        var jsonl = await admin.GetStringAsync($"/api/v1/audit/export?{range}&format=jsonl&action=LOGIN_FAILED");
        var lines = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.ShouldNotBeEmpty();
        JsonDocument.Parse(lines[0]).RootElement.GetProperty("action").GetString().ShouldBe("LOGIN_FAILED");

        await factory.ShouldHaveAuditAsync("AUDIT_EXPORTED");
    }

    [Fact]
    public async Task The_viewer_filters_and_names_the_actors()
    {
        var admin = await factory.AdminAsync();
        var (user, userId) = await factory.CreateUserAsync("audit.viewer.subject");
        await user.GetAsync("/api/v1/auth/me");

        var entries = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/audit?userId={userId}&action=LOGIN");
        var login = entries.EnumerateArray().First();
        login.GetProperty("userName").GetString().ShouldNotBeNullOrEmpty();
        login.GetProperty("outcome").GetString().ShouldBe("SUCCESS");

        var actions = (await admin.GetFromJsonAsync<string[]>("/api/v1/audit/actions"))!;
        actions.ShouldContain("DOCUMENT_VIEWED");
        actions.ShouldContain("AUDIT_EXPORTED");

        (await user.GetAsync("/api/v1/audit/actions")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.GetAsync("/api/v1/audit/seals/status")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Section 4.9's catalog, operation by operation: each one leaves a row that names the actor
    /// and what was acted on.
    /// </summary>
    [Fact]
    public async Task Every_operation_leaves_its_audit_row()
    {
        var admin = await factory.AdminAsync();
        var adminId = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetGuid();
        var (owner, ownerId) = await factory.CreateUserAsync("audit.complete.owner");
        var (stranger, strangerId) = await factory.CreateUserAsync("audit.complete.stranger");
        var categoryId = await admin.CreateCategoryAsync();
        var grantId = await admin.GrantAsync("Category", categoryId, ownerId, "DOCUMENT_VIEW", inherit: true);
        await admin.GrantManyAsync("Category", categoryId, ownerId,
            "DOCUMENT_CREATE", "DOCUMENT_CREATE_VERSION", "DOCUMENT_DOWNLOAD", "DOCUMENT_EDIT", "DOCUMENT_DELETE", "DOCUMENT_RESTORE", "DOCUMENT_SHARE");

        var (documentId, v1) = await owner.CreateDocumentAsync(categoryId, "سند ممیزی", DocumentTestKit.Pdf($"audit {Guid.NewGuid()}"));
        (await owner.GetAsync($"/api/v1/documents/{documentId}/content")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.AddVersionRawAsync(documentId, DocumentTestKit.Pdf($"v2 {Guid.NewGuid()}"))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await owner.PutAsJsonAsync($"/api/v1/documents/{documentId}/tags", new { tags = new[] { "ممیزی" } })).IsSuccessStatusCode.ShouldBeTrue();
        (await stranger.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var share = await owner.PostAsJsonAsync($"/api/v1/documents/{documentId}/shares", new { versionId = v1, recipientId = strangerId, permissions = new[] { "View" } });
        share.StatusCode.ShouldBe(HttpStatusCode.Created, await share.Content.ReadAsStringAsync());
        var shareId = (await share.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await owner.DeleteAsync($"/api/v1/shares/{shareId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await owner.DeleteAsync($"/api/v1/documents/{documentId}")).IsSuccessStatusCode.ShouldBeTrue();
        (await owner.PostAsync($"/api/v1/documents/{documentId}/restore", null)).IsSuccessStatusCode.ShouldBeTrue();
        (await admin.DeleteAsync($"/api/v1/permissions/{grantId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var connection = await factory.OpenConnectionAsync();
        async Task Expect(string action, Guid actor, Guid? documentOrEntity = null, string outcome = "SUCCESS")
        {
            var count = (long)(await ScalarAsync(
                connection,
                """
                SELECT count(*) FROM audit.audit_logs
                 WHERE action = @action AND user_id = @actor AND outcome = @outcome
                   AND (@target::uuid IS NULL OR document_id = @target OR entity_id = @target)
                """,
                ("action", action),
                ("actor", actor),
                ("outcome", outcome),
                ("target", (object?)documentOrEntity ?? DBNull.Value)))!;
            count.ShouldBeGreaterThan(0, $"{action} by {actor} on {documentOrEntity} ({outcome})");
        }

        await Expect("LOGIN", ownerId);
        await Expect("CATEGORY_CREATED", adminId, categoryId);
        await Expect("PERMISSION_GRANTED", adminId, grantId);
        await Expect("DOCUMENT_CREATED", ownerId, documentId);
        await Expect("DOCUMENT_DOWNLOADED", ownerId, documentId);
        await Expect("VERSION_CREATED", ownerId, documentId);
        await Expect("DOCUMENT_TAGS_CHANGED", ownerId, documentId);
        await Expect("ACCESS_DENIED", strangerId, documentId, "DENIED");
        await Expect("DOCUMENT_SHARED", ownerId, documentId);
        await Expect("SHARE_REVOKED", ownerId, documentId);
        await Expect("DOCUMENT_DELETED", ownerId, documentId);
        await Expect("DOCUMENT_RESTORED", ownerId, documentId);
        await Expect("PERMISSION_REVOKED", adminId, grantId);
    }

    private async Task SealAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.BeginAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IAuditSealing>().SealAsync(CancellationToken.None);
        await unitOfWork.CommitAsync(CancellationToken.None);
    }

    private static async Task<JsonElement> VerifyAsync(HttpClient admin, object window)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/audit/seals/verify", window);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Tampering as only a superuser can: with every trigger switched off for the session.</summary>
    private static async Task AsReplicaAsync(NpgsqlConnection owner, string sql, params (string Name, object Value)[] parameters)
    {
        await ExecuteAsync(owner, "SET session_replication_role = replica");
        try
        {
            await ExecuteAsync(owner, sql, parameters);
        }
        finally
        {
            await ExecuteAsync(owner, "SET session_replication_role = DEFAULT");
        }
    }
}
