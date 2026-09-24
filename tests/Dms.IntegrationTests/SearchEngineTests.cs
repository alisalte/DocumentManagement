using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dms.Search.Application;
using Dms.Search.Infrastructure.Engine;
using Dms.Search.Infrastructure.Extraction;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>
/// Phase 5 against the real engines: Persian analysis in OpenSearch, version-aware hits, facets
/// limited to the caller's scope, a live reindex, and OCR of a scanned Persian page through Tika
/// and Tesseract. Skipped unless the services are reachable:
/// <c>DMS_TEST_OPENSEARCH=http://localhost:9200</c> and <c>DMS_TEST_TIKA=http://localhost:9998</c>.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SearchEngineTests(DmsApiFactory factory)
{
    private static readonly string? OpenSearchUrl = Environment.GetEnvironmentVariable("DMS_TEST_OPENSEARCH");
    private static readonly string? TikaUrl = Environment.GetEnvironmentVariable("DMS_TEST_TIKA");

    private static OpenSearchEngine RealEngine() => new(
        new HttpClient(),
        Options.Create(new SearchOptions
        {
            OpenSearchUrl = OpenSearchUrl,
            IndexAlias = $"dms-test-{Guid.NewGuid():N}",
            Refresh = "wait_for",
        }));

    private async Task<(HttpClient Admin, HttpClient Owner, Guid OwnerId, Guid CategoryId)> ArrangeAsync(string who)
    {
        var admin = await factory.AdminAsync();
        var (owner, ownerId) = await factory.CreateUserAsync(who);
        var categoryId = await admin.CreateCategoryAsync();
        await admin.GrantManyAsync("Category", categoryId, ownerId, "DOCUMENT_VIEW", "DOCUMENT_CREATE");
        return (admin, owner, ownerId, categoryId);
    }

    private static async Task<JsonElement> SearchAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/search?{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("degraded").GetBoolean().ShouldBeFalse();
        return result;
    }

    private static IReadOnlyList<Guid> DocumentIds(JsonElement result) =>
        result.GetProperty("hits").EnumerateArray().Select(hit => hit.GetProperty("documentId").GetGuid()).ToList();

    [Fact]
    public async Task Persian_text_is_found_however_it_was_typed()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(OpenSearchUrl), "DMS_TEST_OPENSEARCH is not set.");
        var engine = RealEngine();
        try
        {
            await using var host = factory.WithSearchFakes(engine, new FakeTextExtractor());
            var (_, owner, _, categoryId) = await ArrangeAsync("fa.owner");
            var token = $"z{Guid.NewGuid():N}"[..12];

            // Arabic kaf and yeh, a zero-width non-joiner and Persian digits, as scanned text often has.
            var text = $"{token} كتابخانه مركزي مي‌شود سال ۱۴۰۳";
            var (documentId, _) = await owner.CreateDocumentAsync(categoryId, "سند فارسی", System.Text.Encoding.UTF8.GetBytes(text), "fa.txt");
            await host.Services.RunJobsAsync();

            var client = host.As(owner);
            foreach (var typed in new[] { "کتابخانه", "مرکزی", "میشود", "می شود", "1403" })
            {
                var result = await SearchAsync(client, $"q={Uri.EscapeDataString($"{token} {typed}")}");
                DocumentIds(result).ShouldContain(documentId, $"'{typed}' should match");
            }
        }
        finally
        {
            await engine.DropAllAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Hits_are_versions_and_facets_count_only_what_the_caller_may_see()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(OpenSearchUrl), "DMS_TEST_OPENSEARCH is not set.");
        var engine = RealEngine();
        try
        {
            await using var host = factory.WithSearchFakes(engine, new FakeTextExtractor());
            var (admin, owner, _, categoryId) = await ArrangeAsync("versions.owner");
            var (stranger, strangerId) = await factory.CreateUserAsync("versions.stranger");
            var hiddenCategory = await admin.CreateCategoryAsync();
            await admin.GrantManyAsync("Category", hiddenCategory, strangerId, "DOCUMENT_VIEW", "DOCUMENT_CREATE");

            var token = $"v{Guid.NewGuid():N}"[..12];
            var (documentId, v1) = await owner.CreateDocumentAsync(categoryId, "نسخه‌ها", DocumentTestKit.Pdf($"{token} alpha"), "v1.pdf");
            (await owner.AddVersionRawAsync(documentId, DocumentTestKit.Pdf($"{token} beta"))).StatusCode.ShouldBe(HttpStatusCode.Created);

            // Three documents the owner may not see, all containing the token.
            for (var i = 0; i < 3; i++)
            {
                await stranger.CreateDocumentAsync(hiddenCategory, $"پنهان {i}", DocumentTestKit.Pdf($"{token} alpha hidden"), "h.pdf");
            }

            await host.Services.RunJobsAsync();
            var client = host.As(owner);

            // "alpha" is only in V1: the default search (what a reader gets) does not find it...
            DocumentIds(await SearchAsync(client, $"q={token}+alpha")).ShouldBeEmpty();

            // ...a history search does, and says which version matched.
            var history = await SearchAsync(client, $"q={token}+alpha&allVersions=true");
            var hit = history.GetProperty("hits").EnumerateArray().ShouldHaveSingleItem();
            hit.GetProperty("versionId").GetGuid().ShouldBe(v1);
            hit.GetProperty("isEffective").GetBoolean().ShouldBeFalse();
            history.GetProperty("total").GetInt64().ShouldBe(1, "totals never include documents the caller cannot see");

            var facets = history.GetProperty("facets").GetProperty("category").EnumerateArray().ToList();
            facets.Select(bucket => bucket.GetProperty("key").GetString()).ShouldBe([categoryId.ToString()]);
            facets[0].GetProperty("count").GetInt64().ShouldBe(1);

            // Highlights are escaped by the engine; only our <mark> tags are markup.
            var current = await SearchAsync(client, $"q={token}+beta");
            current.GetProperty("hits")[0].GetProperty("highlights")[0].GetString()!.ShouldContain("<mark>");
        }
        finally
        {
            await engine.DropAllAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_reindex_swaps_to_a_new_index_while_search_keeps_answering()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(OpenSearchUrl), "DMS_TEST_OPENSEARCH is not set.");
        var engine = RealEngine();
        try
        {
            await using var host = factory.WithSearchFakes(engine, new FakeTextExtractor());
            var (admin, owner, _, categoryId) = await ArrangeAsync("reindex.real");
            var token = $"r{Guid.NewGuid():N}"[..12];
            var (documentId, _) = await owner.CreateDocumentAsync(categoryId, "بازسازی", DocumentTestKit.Pdf(token), "r.pdf");
            await host.Services.RunJobsAsync();

            var before = (await engine.StatusAsync(CancellationToken.None)).Index;
            (await host.As(admin).PostAsync("/api/v1/admin/search/reindex", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
            await host.Services.RunJobsAsync();

            var after = (await engine.StatusAsync(CancellationToken.None)).Index;
            after.ShouldNotBeNull();
            after.ShouldNotBe(before);
            DocumentIds(await SearchAsync(host.As(owner), $"q={token}")).ShouldContain(documentId);
        }
        finally
        {
            await engine.DropAllAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_scanned_Persian_page_is_read_by_OCR()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(TikaUrl), "DMS_TEST_TIKA is not set.");
        var engine = new FakeSearchEngine();
        var extractor = new TikaTextExtractor(
            new HttpClient { Timeout = TimeSpan.FromMinutes(5) },
            Options.Create(new SearchOptions { TikaUrl = TikaUrl }));

        await using var host = factory.WithSearchFakes(engine, extractor);
        var (_, owner, _, categoryId) = await ArrangeAsync("ocr.owner");
        var scan = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Samples", "persian-scan.png"));
        var (documentId, _) = await owner.CreateDocumentAsync(categoryId, "اسکن", scan, "scan.png");

        await host.Services.RunJobsAsync();

        var content = engine.VersionsOf(documentId).ShouldHaveSingleItem()["content"]?.GetValue<string>();
        content.ShouldNotBeNull("the scan should have produced text");
        content.ShouldContain("قرارداد");
        content.ShouldContain("تهران");

        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT method FROM search.content_extractions
             WHERE storage_object_id = (SELECT storage_object_id FROM documents.document_versions WHERE document_id = @id);
            """;
        command.Parameters.AddWithValue("id", documentId);
        ((string)(await command.ExecuteScalarAsync())!).ShouldBe("Ocr");
    }
}
