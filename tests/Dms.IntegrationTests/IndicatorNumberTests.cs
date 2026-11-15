using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>
/// Indicator numbers (docs/requirements/indicator-number): patterns per document type with a
/// global default, one counter per type and fiscal year, manual registration, import and backfill.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class IndicatorNumberTests(DmsApiFactory factory)
{
    private const string Admin = "/api/v1/admin/document-types";

    /// <summary>A published, field-less type with a fresh code, optionally with its own pattern.</summary>
    private static async Task<(Guid Id, string Code)> CreateTypeAsync(HttpClient admin, string? pattern = null)
    {
        var code = $"I{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var created = await admin.PostAsJsonAsync(Admin, new { code, name = $"نوع {code}", description = (string?)null });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var published = await admin.PostAsync($"{Admin}/{id}/publish", null);
        published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync());

        if (pattern is not null)
        {
            (await SetTypePatternAsync(admin, id, code, pattern)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        return (id, code);
    }

    private static Task<HttpResponseMessage> SetTypePatternAsync(HttpClient admin, Guid id, string code, string? pattern) =>
        admin.PutAsJsonAsync($"{Admin}/{id}", new
        {
            name = $"نوع {code}",
            description = (string?)null,
            settings = new { indicatorPattern = pattern },
            isActive = true,
        });

    private static async Task<string?> TypePatternAsync(HttpClient admin, Guid id)
    {
        var type = await admin.GetFromJsonAsync<JsonElement>($"{Admin}/{id}");
        var settings = type.GetProperty("type").GetProperty("settings");
        return settings.TryGetProperty("indicatorPattern", out var pattern) ? pattern.GetString() : null;
    }

    private static async Task<Guid> CreateDocumentOfTypeAsync(HttpClient client, Guid categoryId, Guid typeId, string title = "نامه")
    {
        var upload = await client.UploadAsync(DocumentTestKit.Pdf($"{title} {Guid.NewGuid()}"));
        var response = await client.PostAsJsonAsync("/api/v1/documents", new
        {
            title,
            description = (string?)null,
            categoryId,
            documentTypeId = typeId,
            uploadId = upload.GetProperty("uploadId").GetGuid(),
            tags = Array.Empty<string>(),
            changeDescription = "نسخه‌ی اول",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();
    }

    private sealed record Desk(HttpClient Admin, HttpClient Clerk, Guid ClerkId, Guid CategoryId, Guid TypeId, string Code);

    /// <summary>A type, a category and a clerk who may file and register in it. Administrators get no ACL bypass.</summary>
    private async Task<Desk> ArrangeAsync(string? pattern = null)
    {
        var admin = await factory.AdminAsync();
        var (typeId, code) = await CreateTypeAsync(admin, pattern);
        var categoryId = await admin.CreateCategoryAsync();
        var (clerk, clerkId) = await factory.CreateUserAsync("indicator-clerk");
        await admin.GrantManyAsync("Category", categoryId, clerkId, "DOCUMENT_VIEW", "DOCUMENT_CREATE", "DOCUMENT_DELETE", "DOCUMENT_REGISTER_INDICATOR");
        return new Desk(admin, clerk, clerkId, categoryId, typeId, code);
    }

    private static Task<HttpResponseMessage> RegisterRawAsync(HttpClient client, Guid documentId) =>
        client.PostAsync($"/api/v1/documents/{documentId}/indicator", null);

    private static async Task<string> RegisterAsync(HttpClient client, Guid documentId)
    {
        var response = await RegisterRawAsync(client, documentId);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("number").GetString()!;
    }

    private static IEnumerable<string?> AllowedActions(JsonElement details) =>
        details.GetProperty("allowedActions").EnumerateArray().Select(action => action.GetString());

    private async Task<int> FiscalYearOfAsync(Guid documentId)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT fiscal_year FROM documents.documents WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", documentId);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private async Task MoveToFiscalYearAsync(Guid documentId, int year)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("UPDATE documents.documents SET fiscal_year = @year WHERE id = @id", connection);
        command.Parameters.AddWithValue("year", year);
        command.Parameters.AddWithValue("id", documentId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAuditAsync(string action, Guid documentId)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM audit.audit_logs WHERE action = @action AND document_id = @id",
            connection);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("id", documentId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Registering_gives_consecutive_numbers_per_type_and_never_twice()
    {
        var desk = await ArrangeAsync();
        var first = await CreateDocumentOfTypeAsync(desk.Clerk, desk.CategoryId, desk.TypeId);
        var second = await CreateDocumentOfTypeAsync(desk.Clerk, desk.CategoryId, desk.TypeId);
        var year = await FiscalYearOfAsync(first);

        (await RegisterAsync(desk.Clerk, first)).ShouldBe($"{desk.Code}-{year}-00001");
        (await RegisterAsync(desk.Clerk, second)).ShouldBe($"{desk.Code}-{year}-00002");

        var again = await RegisterRawAsync(desk.Clerk, first);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Content.ReadAsStringAsync()).ShouldContain("document.indicator_already_registered");

        var details = await desk.Clerk.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{first}");
        details.GetProperty("indicatorNumber").GetString().ShouldBe($"{desk.Code}-{year}-00001");
        details.GetProperty("indicatorRegisteredBy").GetGuid().ShouldBe(desk.ClerkId);
        AllowedActions(details).ShouldNotContain("DOCUMENT_REGISTER_INDICATOR");
        (await CountAuditAsync("DOCUMENT_INDICATOR_REGISTERED", first)).ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_registrations_get_distinct_consecutive_numbers()
    {
        var desk = await ArrangeAsync("{TYPE}/{YEAR}/{SEQ:3}");
        var ids = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            ids.Add(await CreateDocumentOfTypeAsync(desk.Clerk, desk.CategoryId, desk.TypeId));
        }

        var numbers = await Task.WhenAll(ids.Select(id => RegisterAsync(desk.Clerk, id)));

        numbers.Select(number => int.Parse(number[^3..])).Order().ShouldBe(Enumerable.Range(1, 12));
    }

    [Fact]
    public async Task Each_fiscal_year_starts_at_one_and_closed_years_can_still_register()
    {
        var desk = await ArrangeAsync();
        var current = await CreateDocumentOfTypeAsync(desk.Clerk, desk.CategoryId, desk.TypeId);
        var old = await CreateDocumentOfTypeAsync(desk.Clerk, desk.CategoryId, desk.TypeId);
        await MoveToFiscalYearAsync(old, 1400);

        await RegisterAsync(desk.Clerk, current);
        var details = await desk.Clerk.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{old}");
        AllowedActions(details).ShouldContain("DOCUMENT_REGISTER_INDICATOR", "registering does not change a closed year document");

        (await RegisterAsync(desk.Clerk, old)).ShouldBe($"{desk.Code}-1400-00001");
    }

    [Fact]
    public async Task A_deleted_documents_number_is_kept_and_never_reused()
    {
        var desk = await ArrangeAsync();
        var deleted = await CreateDocumentOfTypeAsync(desk.Clerk, desk.CategoryId, desk.TypeId);
        var next = await CreateDocumentOfTypeAsync(desk.Clerk, desk.CategoryId, desk.TypeId);
        var year = await FiscalYearOfAsync(deleted);

        await RegisterAsync(desk.Clerk, deleted);
        var removed = await desk.Clerk.DeleteAsync($"/api/v1/documents/{deleted}?reason=test");
        removed.IsSuccessStatusCode.ShouldBeTrue(await removed.Content.ReadAsStringAsync());

        (await RegisterAsync(desk.Clerk, next)).ShouldBe($"{desk.Code}-{year}-00002");
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT indicator_number FROM documents.documents WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", deleted);
        ((string?)await command.ExecuteScalarAsync()).ShouldBe($"{desk.Code}-{year}-00001");
    }

    [Fact]
    public async Task Registering_needs_its_own_permission()
    {
        var desk = await ArrangeAsync();
        var documentId = await CreateDocumentOfTypeAsync(desk.Clerk, desk.CategoryId, desk.TypeId);
        var (user, userId) = await factory.CreateUserAsync("indicator-reader");
        await desk.Admin.GrantManyAsync("Category", desk.CategoryId, userId, "DOCUMENT_VIEW", "DOCUMENT_EDIT");

        (await RegisterRawAsync(user, documentId)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        AllowedActions(await user.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}"))
            .ShouldNotContain("DOCUMENT_REGISTER_INDICATOR");

        await desk.Admin.GrantAsync("Category", desk.CategoryId, userId, "DOCUMENT_REGISTER_INDICATOR");
        AllowedActions(await user.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}"))
            .ShouldContain("DOCUMENT_REGISTER_INDICATOR");
        await RegisterAsync(user, documentId);
    }

    private async Task<string?> NumberOfAsync(Guid documentId)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT indicator_number FROM documents.documents WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", documentId);
        return (string?)await command.ExecuteScalarAsync();
    }

    private async Task SetCreatedAtAsync(Guid documentId, DateTimeOffset createdAt)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("UPDATE documents.documents SET created_at = @at WHERE id = @id", connection);
        command.Parameters.AddWithValue("at", createdAt);
        command.Parameters.AddWithValue("id", documentId);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Backfill_numbers_existing_documents_oldest_first_including_deleted_ones()
    {
        var desk = await ArrangeAsync();
        var newest = await CreateDocumentOfTypeAsync(desk.Clerk, desk.CategoryId, desk.TypeId);
        var oldest = await CreateDocumentOfTypeAsync(desk.Clerk, desk.CategoryId, desk.TypeId);
        var middle = await CreateDocumentOfTypeAsync(desk.Clerk, desk.CategoryId, desk.TypeId);
        var year = await FiscalYearOfAsync(newest);
        var now = DateTimeOffset.UtcNow;
        await SetCreatedAtAsync(oldest, now.AddHours(-3));
        await SetCreatedAtAsync(middle, now.AddHours(-2));
        await SetCreatedAtAsync(newest, now.AddHours(-1));
        (await desk.Clerk.DeleteAsync($"/api/v1/documents/{middle}?reason=test")).IsSuccessStatusCode.ShouldBeTrue();

        var status = await desk.Admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/indicator-numbers");
        status.GetProperty("unnumbered").GetInt32().ShouldBeGreaterThanOrEqualTo(3);

        var started = await desk.Admin.PostAsync("/api/v1/admin/indicator-numbers/backfill", null);
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted, await started.Content.ReadAsStringAsync());
        await using (var host = factory.WithSearchFakes(new FakeSearchEngine(), new FakeTextExtractor()))
        {
            await host.Services.RunJobsAsync();
        }

        (await NumberOfAsync(oldest)).ShouldBe($"{desk.Code}-{year}-00001");
        (await NumberOfAsync(middle)).ShouldBe($"{desk.Code}-{year}-00002", "deleted documents are numbered too");
        (await NumberOfAsync(newest)).ShouldBe($"{desk.Code}-{year}-00003");
        (await desk.Admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/indicator-numbers"))
            .GetProperty("unnumbered").GetInt32().ShouldBe(0);
        (await CountAuditAsync("DOCUMENT_INDICATOR_REGISTERED", oldest)).ShouldBe(1);
    }

    [Fact]
    public async Task Documents_can_be_found_by_their_number()
    {
        var desk = await ArrangeAsync();
        var documentId = await CreateDocumentOfTypeAsync(desk.Clerk, desk.CategoryId, desk.TypeId);
        await CreateDocumentOfTypeAsync(desk.Clerk, desk.CategoryId, desk.TypeId);
        var number = await RegisterAsync(desk.Clerk, documentId);

        foreach (var url in new[]
        {
            $"/api/v1/documents?categoryId={desk.CategoryId}&indicatorNumber={Uri.EscapeDataString(number.ToLowerInvariant())}",
            $"/api/v1/documents?categoryId={desk.CategoryId}&search={Uri.EscapeDataString(number)}",
        })
        {
            var page = await desk.Clerk.GetFromJsonAsync<JsonElement>(url);
            var item = page.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
            item.GetProperty("id").GetGuid().ShouldBe(documentId);
            item.GetProperty("indicatorNumber").GetString().ShouldBe(number);
        }
    }

    [Fact]
    public async Task Only_administrators_start_the_backfill()
    {
        var (user, _) = await factory.CreateUserAsync("indicator-backfill");

        (await user.PostAsync("/api/v1/admin/indicator-numbers/backfill", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Imported_documents_are_numbered_in_creation_order_when_the_import_finishes()
    {
        var admin = await factory.AdminAsync();
        var (_, code) = await CreateTypeAsync(admin);
        var folder = $"اندیکاتور/{Guid.NewGuid():N}"[..24];
        object Entry(string date)
        {
            var relative = $"indicator/{Guid.NewGuid():N}.txt";
            var bytes = System.Text.Encoding.UTF8.GetBytes($"indicator {date} {Guid.NewGuid()}");
            var fullPath = Path.Combine(factory.ImportFilesRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, bytes);
            return new
            {
                sourceId = $"IND-{Guid.NewGuid():N}"[..20],
                title = $"وارداتی {date}",
                categoryPath = folder,
                documentTypeCode = code,
                file = relative,
                contentType = "text/plain",
                size = bytes.Length,
                sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
                ownerUsername = "admin",
                createdAt = date,
            };
        }

        // Out of order on purpose: the numbers follow the creation dates, not the manifest.
        var manifest = new
        {
            schemaVersion = 2,
            source = "indicator-test",
            entries = new[] { Entry("2024-03-10T10:00:00Z"), Entry("2024-01-10T10:00:00Z"), Entry("2024-02-10T10:00:00Z") },
        };
        var created = await admin.PostAsJsonAsync("/api/v1/imports", new
        {
            name = "واردات اندیکاتور",
            sourceSystem = "indicator-test",
            manifestJson = JsonSerializer.Serialize(manifest),
            failurePolicy = "ContinueOnError",
            createMissingCategories = true,
            dryRunOnly = false,
            mappings = Array.Empty<object>(),
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var jobId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PostAsync($"/api/v1/imports/{jobId}/validate", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PostAsync($"/api/v1/imports/{jobId}/start", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using (var host = factory.WithSearchFakes(new FakeSearchEngine(), new FakeTextExtractor()))
        {
            await host.Services.RunJobsAsync();
        }

        var items = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/imports/{jobId}/items");
        var numbers = new List<(string Title, string? Number)>();
        foreach (var item in items.EnumerateArray())
        {
            item.GetProperty("status").GetString().ShouldBe("Succeeded");
            var documentId = item.GetProperty("targetDocumentId").GetGuid();
            numbers.Add((item.GetProperty("title").GetString()!, await NumberOfAsync(documentId)));
        }

        numbers.OrderBy(pair => pair.Title).Select(pair => pair.Number).ShouldBe(
        [
            $"{code}-1402-00001",
            $"{code}-1402-00002",
            $"{code}-1402-00003",
        ]);
    }

    [Fact]
    public async Task The_default_pattern_applies_until_an_administrator_changes_it()
    {
        var admin = await factory.AdminAsync();

        var settings = await admin.GetFromJsonAsync<JsonElement>($"{Admin}/indicator-settings");

        settings.GetProperty("defaultPattern").GetString().ShouldBe("{TYPE}-{YEAR}-{SEQ:5}");
    }

    [Fact]
    public async Task The_default_pattern_must_contain_the_type_code()
    {
        var admin = await factory.AdminAsync();

        var response = await admin.PutAsJsonAsync($"{Admin}/indicator-settings", new { defaultPattern = "DOC-{YEAR}-{SEQ:5}" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("indicator_pattern.default_needs_type");
    }

    [Fact]
    public async Task A_type_pattern_is_stored_canonically_and_blank_means_default()
    {
        var admin = await factory.AdminAsync();
        var (id, code) = await CreateTypeAsync(admin, " {type}/{year}/{seq:3} ");

        (await TypePatternAsync(admin, id)).ShouldBe("{TYPE}/{YEAR}/{SEQ:3}");

        (await SetTypePatternAsync(admin, id, code, "  ")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await TypePatternAsync(admin, id)).ShouldBeNull();
    }

    [Fact]
    public async Task An_invalid_type_pattern_is_rejected()
    {
        var admin = await factory.AdminAsync();
        var (id, code) = await CreateTypeAsync(admin);

        var response = await SetTypePatternAsync(admin, id, code, "{TYPE}-{SEQ:4}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("indicator_pattern.invalid");
    }

    [Fact]
    public async Task A_type_pattern_that_would_repeat_another_types_numbers_is_rejected()
    {
        var admin = await factory.AdminAsync();
        var (_, first) = await CreateTypeAsync(admin);
        var (secondId, second) = await CreateTypeAsync(admin);

        // The first type uses the default {TYPE}-{YEAR}-{SEQ:5}, which reads exactly like this.
        var response = await SetTypePatternAsync(admin, secondId, second, $"{first}-{{YEAR}}-{{SEQ:5}}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("indicator_pattern.collides");
    }

    [Fact]
    public async Task Preview_shows_a_sample_with_the_types_code()
    {
        var admin = await factory.AdminAsync();
        var (id, code) = await CreateTypeAsync(admin);

        var response = await admin.PostAsJsonAsync($"{Admin}/indicator-pattern/preview", new { pattern = "{TYPE}-{CAT}-{YEAR}-{SEQ:4}", documentTypeId = id });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var sample = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sample").GetString();
        sample.ShouldStartWith($"{code}-CAT-14");
        sample.ShouldEndWith("-0001");
    }

    [Fact]
    public async Task Only_administrators_manage_patterns()
    {
        var (user, _) = await factory.CreateUserAsync("indicator-pattern");

        (await user.GetAsync($"{Admin}/indicator-settings")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.PutAsJsonAsync($"{Admin}/indicator-settings", new { defaultPattern = "{TYPE}-{YEAR}-{SEQ:5}" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
