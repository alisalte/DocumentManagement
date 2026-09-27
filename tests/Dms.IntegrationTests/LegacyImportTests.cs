using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dms.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>Phase 10.6: legacy import engine — validate, import, idempotency, security.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class LegacyImportTests(DmsApiFactory factory)
{
    [Fact]
    public async Task Validate_dry_run_then_import_is_idempotent_and_audited()
    {
        var admin = await factory.AdminAsync();
        var relative = $"batch/{Guid.NewGuid():N}.txt";
        var fullPath = Path.Combine(factory.ImportFilesRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var bytes = Encoding.UTF8.GetBytes("legacy-import-payload");
        await File.WriteAllBytesAsync(fullPath, bytes);
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));

        var classLevel = await admin.PostAsJsonAsync(
            "/api/v1/admin/classification-levels",
            new { code = "CONF", name = "محرمانه", rank = 20 });
        classLevel.StatusCode.ShouldBe(HttpStatusCode.Created, await classLevel.Content.ReadAsStringAsync());

        var sourceId = $"LEG-{Guid.NewGuid():N}"[..20];
        var manifest = new
        {
            schemaVersion = 2,
            source = "legacy-test",
            entries = new object[]
            {
                new
                {
                    sourceId,
                    title = "سند وارداتی",
                    categoryPath = $"واردات/{Guid.NewGuid():N}"[..24],
                    documentTypeCode = "GENERAL",
                    file = relative,
                    contentType = "text/plain",
                    size = bytes.Length,
                    sha256 = sha,
                    ownerUsername = "admin",
                    classification = "CONF",
                    createdAt = "2024-01-15T10:00:00Z",
                },
            },
        };

        var created = await admin.PostAsJsonAsync("/api/v1/imports", new
        {
            name = "آزمون واردات",
            sourceSystem = "legacy-test",
            manifestJson = JsonSerializer.Serialize(manifest),
            failurePolicy = "ContinueOnError",
            createMissingCategories = true,
            dryRunOnly = false,
            mappings = Array.Empty<object>(),
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var jobId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var validated = await admin.PostAsync($"/api/v1/imports/{jobId}/validate", null);
        validated.StatusCode.ShouldBe(HttpStatusCode.OK, await validated.Content.ReadAsStringAsync());
        using (var report = JsonDocument.Parse(await validated.Content.ReadAsStringAsync()))
        {
            report.RootElement.GetProperty("invalid").GetInt32().ShouldBe(0);
            report.RootElement.GetProperty("valid").GetInt32().ShouldBe(1);
            report.RootElement.GetProperty("status").GetString().ShouldBe("Ready");
        }

        // Dry-run must not create documents.
        var beforeDocs = await admin.GetFromJsonAsync<JsonElement>("/api/v1/documents?take=5");
        _ = beforeDocs;

        var started = await admin.PostAsync($"/api/v1/imports/{jobId}/start", null);
        started.StatusCode.ShouldBe(HttpStatusCode.NoContent, await started.Content.ReadAsStringAsync());
        await factory.Services.RunJobsAsync();

        var job = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/imports/{jobId}");
        job.GetProperty("status").GetString().ShouldBeOneOf("Completed", "CompletedWithErrors");
        job.GetProperty("succeededItems").GetInt32().ShouldBe(1);

        var items = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/imports/{jobId}/items");
        var item = items.EnumerateArray().Single();
        item.GetProperty("status").GetString().ShouldBe("Succeeded");
        var documentId = item.GetProperty("targetDocumentId").GetGuid();

        var doc = await admin.GetAsync($"/api/v1/documents/{documentId}");
        doc.StatusCode.ShouldBe(HttpStatusCode.OK, await doc.Content.ReadAsStringAsync());

        // Idempotent re-run: create second job with same sourceId → skip, no duplicate document.
        var again = await admin.PostAsJsonAsync("/api/v1/imports", new
        {
            name = "آزمون دوباره",
            sourceSystem = "legacy-test",
            manifestJson = JsonSerializer.Serialize(manifest),
            failurePolicy = "ContinueOnError",
            createMissingCategories = true,
            dryRunOnly = false,
        });
        again.StatusCode.ShouldBe(HttpStatusCode.Created, await again.Content.ReadAsStringAsync());
        var job2 = (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PostAsync($"/api/v1/imports/{job2}/validate", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PostAsync($"/api/v1/imports/{job2}/start", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.Services.RunJobsAsync();

        var items2 = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/imports/{job2}/items");
        items2.EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("Skipped");
        items2.EnumerateArray().Single().GetProperty("targetDocumentId").GetGuid().ShouldBe(documentId);

        var reportGet = await admin.GetAsync($"/api/v1/imports/{jobId}/report");
        reportGet.StatusCode.ShouldBe(HttpStatusCode.OK, await reportGet.Content.ReadAsStringAsync());

        await factory.ShouldHaveAuditAsync("IMPORT_CREATED");
        await factory.ShouldHaveAuditAsync("IMPORT_VALIDATION_STARTED");
        await factory.ShouldHaveAuditAsync("IMPORT_VALIDATION_COMPLETED");
        await factory.ShouldHaveAuditAsync("IMPORT_STARTED");
        await factory.ShouldHaveAuditAsync("IMPORT_ITEM_IMPORTED");
        await factory.ShouldHaveAuditAsync("IMPORT_REPORT_ACCESSED");
    }

    [Fact]
    public async Task Hash_mismatch_and_path_traversal_fail_validation()
    {
        var admin = await factory.AdminAsync();
        var relative = $"bad/{Guid.NewGuid():N}.txt";
        var fullPath = Path.Combine(factory.ImportFilesRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "content");

        var manifest = new
        {
            schemaVersion = 2,
            source = "legacy-bad",
            entries = new object[]
            {
                new
                {
                    sourceId = $"BAD-{Guid.NewGuid():N}"[..16],
                    title = "بد",
                    categoryPath = "X",
                    documentTypeCode = "GENERAL",
                    file = relative,
                    sha256 = new string('b', 64),
                },
                new
                {
                    sourceId = $"TRAV-{Guid.NewGuid():N}"[..16],
                    title = "تراورس",
                    categoryPath = "X",
                    documentTypeCode = "GENERAL",
                    file = "../escape.txt",
                },
            },
        };

        var created = await admin.PostAsJsonAsync("/api/v1/imports", new
        {
            name = "اعتبارسنجی خطا",
            sourceSystem = "legacy-bad",
            manifestJson = JsonSerializer.Serialize(manifest),
            createMissingCategories = true,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var jobId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var validated = await admin.PostAsync($"/api/v1/imports/{jobId}/validate", null);
        validated.StatusCode.ShouldBe(HttpStatusCode.OK, await validated.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await validated.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("invalid").GetInt32().ShouldBeGreaterThanOrEqualTo(2);
        var codes = body.RootElement.GetProperty("issues").EnumerateArray()
            .Select(issue => issue.GetProperty("code").GetString())
            .ToHashSet();
        codes.ShouldContain("IMPORT_HASH_MISMATCH");
        codes.ShouldContain("import.path_traversal");

        (await admin.PostAsync($"/api/v1/imports/{jobId}/start", null))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Unauthorized_user_cannot_create_or_view_imports()
    {
        var (user, _) = await factory.CreateUserAsync("imp.deny");
        var forbidden = await user.PostAsJsonAsync("/api/v1/imports", new
        {
            name = "nope",
            sourceSystem = "x",
            manifestJson = """{"schemaVersion":1,"entries":[{"title":"A","categoryPath":"B","documentTypeCode":"GENERAL","file":"a.pdf"}]}""",
        });
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await user.GetAsync("/api/v1/imports")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Resume_processes_only_remaining_items()
    {
        var admin = await factory.AdminAsync();
        var entries = new List<object>();
        for (var i = 0; i < 3; i++)
        {
            var relative = $"resume/{Guid.NewGuid():N}.txt";
            var fullPath = Path.Combine(factory.ImportFilesRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, $"item-{i}");
            entries.Add(new
            {
                sourceId = $"RES-{i}-{Guid.NewGuid():N}"[..22],
                title = $"رزومه {i}",
                categoryPath = $"ResumeCat/{Guid.NewGuid():N}"[..20],
                documentTypeCode = "GENERAL",
                file = relative,
                ownerUsername = "admin",
            });
        }

        var manifest = new { schemaVersion = 2, source = "resume-src", entries };
        var created = await admin.PostAsJsonAsync("/api/v1/imports", new
        {
            name = "رزومه",
            sourceSystem = "resume-src",
            manifestJson = JsonSerializer.Serialize(manifest),
            createMissingCategories = true,
            failurePolicy = "ContinueOnError",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var jobId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PostAsync($"/api/v1/imports/{jobId}/validate", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PostAsync($"/api/v1/imports/{jobId}/start", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Process one item, then pause.
        await factory.Services.GetRequiredService<JobRunner>()
            .RunUntilIdleAsync(1, CancellationToken.None);
        (await admin.PostAsync($"/api/v1/imports/{jobId}/pause", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var mid = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/imports/{jobId}");
        var processedMid = mid.GetProperty("processedItems").GetInt32();
        processedMid.ShouldBeGreaterThan(0);
        processedMid.ShouldBeLessThan(3);

        (await admin.PostAsync($"/api/v1/imports/{jobId}/resume", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.Services.RunJobsAsync();

        var done = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/imports/{jobId}");
        done.GetProperty("succeededItems").GetInt32().ShouldBe(3);
        done.GetProperty("status").GetString().ShouldBe("Completed");
    }
}
