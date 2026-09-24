using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>
/// Phase 5, search (section 8). The engine here is a fake that returns every document it holds
/// for any query, like an index that lags a revoked grant: whatever reaches the caller must have
/// passed the application's own checks. The query sent to the engine is inspected as well.
/// Tests against a real OpenSearch and Tika are in <see cref="SearchEngineTests"/>.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SearchApiTests(DmsApiFactory factory)
{
    private static async Task<JsonElement> SearchAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/search?{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static IReadOnlyList<Guid> DocumentIds(JsonElement result) =>
        result.GetProperty("hits").EnumerateArray().Select(hit => hit.GetProperty("documentId").GetGuid()).ToList();

    private async Task<(HttpClient Client, Guid UserId, Guid CategoryId)> OwnerOfNewCategoryAsync(HttpClient admin, string who, Guid? parentId = null)
    {
        var (client, userId) = await factory.CreateUserAsync(who);
        var categoryId = await admin.CreateCategoryAsync(parentId);
        await admin.GrantManyAsync("Category", categoryId, userId, "DOCUMENT_VIEW", "DOCUMENT_CREATE", "DOCUMENT_DELETE");
        return (client, userId, categoryId);
    }

    [Fact]
    public async Task Every_version_is_indexed_with_its_own_text_and_state()
    {
        var engine = new FakeSearchEngine();
        await using var host = factory.WithSearchFakes(engine, new FakeTextExtractor());
        var admin = await factory.AdminAsync();
        var (owner, _, categoryId) = await OwnerOfNewCategoryAsync(admin, "index.owner");

        var alpha = $"alpha{Guid.NewGuid():N}";
        var beta = $"beta{Guid.NewGuid():N}";
        var (documentId, v1) = await owner.CreateDocumentAsync(categoryId, "قرارداد نسخه‌دار", DocumentTestKit.Pdf(alpha), "v1.pdf");
        var added = await owner.AddVersionRawAsync(documentId, DocumentTestKit.Pdf(beta));
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync());
        var v2 = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("versionId").GetGuid();

        await host.Services.RunJobsAsync();

        var versions = engine.VersionsOf(documentId);
        versions.Count.ShouldBe(2, "one search document per version (section 8.2)");

        versions[0]["version_id"]!.GetValue<string>().ShouldBe(v1.ToString());
        versions[0]["is_current"]!.GetValue<bool>().ShouldBeFalse();
        versions[0]["is_effective"]!.GetValue<bool>().ShouldBeFalse();
        versions[0]["content"]!.GetValue<string>().ShouldContain(alpha);
        versions[0]["content"]!.GetValue<string>().ShouldNotContain(beta);

        versions[1]["version_id"]!.GetValue<string>().ShouldBe(v2.ToString());
        versions[1]["is_current"]!.GetValue<bool>().ShouldBeTrue();
        versions[1]["is_effective"]!.GetValue<bool>().ShouldBeTrue();
        versions[1]["content"]!.GetValue<string>().ShouldContain(beta);
        versions[1]["category_path"]!.AsArray().Select(node => node!.GetValue<string>()).ShouldContain(categoryId.ToString());

        // The text is kept as a derived object of the file, so a rebuild never extracts again.
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.status, e.method, t.derived_from_id = e.storage_object_id
              FROM search.content_extractions e
              JOIN storage.storage_objects t ON t.id = e.text_object_id
             WHERE e.storage_object_id = (SELECT storage_object_id FROM documents.document_versions WHERE id = @v2);
            """;
        command.Parameters.AddWithValue("v2", v2);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue("the extraction is recorded");
        reader.GetString(0).ShouldBe("Completed");
        reader.GetString(1).ShouldBe("TextLayer");
        reader.GetBoolean(2).ShouldBeTrue();
    }

    [Fact]
    public async Task Search_returns_only_what_the_caller_may_see_even_when_the_engine_returns_more()
    {
        var engine = new FakeSearchEngine();
        await using var host = factory.WithSearchFakes(engine, new FakeTextExtractor());
        var admin = await factory.AdminAsync();

        // Visible: category A. Invisible: category B (no grant) and C (VIEW on A, DENY on C below it).
        var (ownerA, _, categoryA) = await OwnerOfNewCategoryAsync(admin, "scope.a");
        var (ownerB, _, categoryB) = await OwnerOfNewCategoryAsync(admin, "scope.b");
        var (ownerC, _, categoryC) = await OwnerOfNewCategoryAsync(admin, "scope.c", categoryA);

        var (visible, _) = await ownerA.CreateDocumentAsync(categoryA, "سند دیدنی");
        var (hidden, _) = await ownerB.CreateDocumentAsync(categoryB, "سند پنهان");
        var (denied, _) = await ownerC.CreateDocumentAsync(categoryC, "سند ممنوع");
        await host.Services.RunJobsAsync();

        var (reader, readerId) = await factory.CreateUserAsync("scope.reader");
        await admin.GrantAsync("Category", categoryA, readerId, "DOCUMENT_VIEW", inherit: true);
        await admin.GrantAsync("Category", categoryC, readerId, "DOCUMENT_VIEW", effect: "Deny", inherit: true);

        var result = await SearchAsync(host.As(reader), "q=%D8%B3%D9%86%D8%AF");
        var ids = DocumentIds(result);
        ids.ShouldContain(visible);
        ids.ShouldNotContain(hidden);
        ids.ShouldNotContain(denied);
        result.GetProperty("degraded").GetBoolean().ShouldBeFalse();

        // The engine is told the same rules, so totals and facets come from visible documents only.
        var sent = engine.Queries[^1].ToJsonString();
        sent.ShouldContain(categoryA.ToString());
        sent.ShouldNotContain(categoryB.ToString());
        var mustNot = engine.Queries[^1]["query"]!["bool"]!["filter"]![0]!["bool"]!["must_not"]!.ToJsonString();
        mustNot.ShouldContain(categoryC.ToString());
        sent.ShouldContain("\"is_deleted\":false");
    }

    [Fact]
    public async Task A_draft_is_found_only_by_its_author()
    {
        var engine = new FakeSearchEngine();
        await using var host = factory.WithSearchFakes(engine, new FakeTextExtractor());
        var admin = await factory.AdminAsync();
        var (author, authorId, categoryId) = await OwnerOfNewCategoryAsync(admin, "draft.author");
        var (reader, readerId) = await factory.CreateUserAsync("draft.reader");
        await admin.GrantAsync("Category", categoryId, readerId, "DOCUMENT_VIEW", inherit: true);

        var (documentId, _) = await author.CreateDocumentAsync(categoryId, "سند با پیش‌نویس");
        await host.Services.RunJobsAsync();

        // Make the indexed version look like an unpublished draft; the application must drop it.
        foreach (var version in engine.VersionsOf(documentId))
        {
            version["is_published"] = false;
            version["approval_status"] = "Draft";
        }

        DocumentIds(await SearchAsync(host.As(reader), "q=x")).ShouldNotContain(documentId);
        DocumentIds(await SearchAsync(host.As(author), "q=x")).ShouldContain(documentId);

        // And the engine is asked for published versions or the caller's own only.
        var sent = engine.Queries[^1].ToJsonString();
        sent.ShouldContain("\"is_published\":true");
        sent.ShouldContain(authorId.ToString());
    }

    [Fact]
    public async Task Metadata_conditions_are_typed_and_versioned()
    {
        var engine = new FakeSearchEngine();
        await using var host = factory.WithSearchFakes(engine, new FakeTextExtractor());
        var admin = await factory.AdminAsync();
        var (owner, _, categoryId) = await OwnerOfNewCategoryAsync(admin, "meta.owner");

        var code = $"S{Guid.NewGuid():N}"[..14].ToUpperInvariant();
        var created = await admin.PostAsJsonAsync("/api/v1/admin/document-types", new { code, name = "قرارداد", description = (string?)null });
        var typeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PutAsJsonAsync($"/api/v1/admin/document-types/{typeId}/draft", new
        {
            fields = new object[]
            {
                new { code = "amount", label = new { fa = "مبلغ" }, type = "Decimal", isSearchable = true },
                new { code = "vendor", label = new { fa = "طرف" }, type = "Text", isSearchable = true },
            },
            rules = Array.Empty<object>(),
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PostAsync($"/api/v1/admin/document-types/{typeId}/publish", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var upload = await owner.UploadAsync(DocumentTestKit.Pdf("amounts"));
        var filed = await owner.PostAsJsonAsync("/api/v1/documents", new
        {
            title = "قرارداد مبلغ‌دار",
            description = (string?)null,
            categoryId,
            documentTypeId = typeId,
            uploadId = upload.GetProperty("uploadId").GetGuid(),
            tags = Array.Empty<string>(),
            changeDescription = (string?)null,
            metadata = new { amount = "5000000000", vendor = "شرکت الف" },
        });
        filed.StatusCode.ShouldBe(HttpStatusCode.Created, await filed.Content.ReadAsStringAsync());
        var documentId = (await filed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();

        // V1.1: twelve billion, written with Persian digits and separators.
        (await owner.PutAsJsonAsync($"/api/v1/documents/{documentId}/metadata", new
        {
            metadata = new { amount = "۱۲٬۰۰۰٬۰۰۰٬۰۰۰", vendor = "شرکت الف" },
            changeDescription = "اصلاح مبلغ",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        await host.Services.RunJobsAsync();

        var versions = engine.VersionsOf(documentId);
        versions.Count.ShouldBe(2);
        versions[0]["meta"]![code]!["amount__num"]!.GetValue<double>().ShouldBe(5e9);
        versions[1]["meta"]![code]!["amount__num"]!.GetValue<double>().ShouldBe(12e9);
        versions[1]["meta_text"]!.GetValue<string>().ShouldContain("شرکت الف");

        var search = await host.As(owner).PostAsJsonAsync("/api/v1/search", new
        {
            documentTypeId = typeId,
            allVersions = true,
            metadata = new[] { new { field = "amount", op = "gte", value = "۱۲٬۰۰۰٬۰۰۰٬۰۰۰" } },
        });
        search.StatusCode.ShouldBe(HttpStatusCode.OK, await search.Content.ReadAsStringAsync());

        var range = engine.Queries[^1].ToJsonString();
        range.ShouldContain($"\"meta.{code}.amount__num\":{{\"gte\":12000000000");

        // Codes only mean something within a type.
        var untyped = await host.As(owner).PostAsJsonAsync("/api/v1/search", new
        {
            metadata = new[] { new { field = "amount", op = "gte", value = 1 } },
        });
        untyped.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var unknownOp = await host.As(owner).PostAsJsonAsync("/api/v1/search", new
        {
            documentTypeId = typeId,
            metadata = new[] { new { field = "amount", op = "regexp", value = ".*" } },
        });
        unknownOp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_reindex_builds_a_new_generation_and_then_swaps_the_alias()
    {
        var engine = new FakeSearchEngine();
        await using var host = factory.WithSearchFakes(engine, new FakeTextExtractor());
        var admin = await factory.AdminAsync();
        var (owner, _, categoryId) = await OwnerOfNewCategoryAsync(admin, "reindex.owner");
        var (documentId, _) = await owner.CreateDocumentAsync(categoryId, "بازسازی نمایه");

        (await host.As(owner).PostAsync("/api/v1/admin/search/reindex", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var started = await host.As(admin).PostAsync("/api/v1/admin/search/reindex", null);
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted, await started.Content.ReadAsStringAsync());
        await host.Services.RunJobsAsync();

        engine.SwappedTo.ShouldNotBeNull();
        engine.VersionsOf(documentId, engine.SwappedTo).Count.ShouldBe(1);
        await factory.ShouldHaveAuditAsync("SEARCH_REINDEX_STARTED");

        var status = await host.As(admin).GetFromJsonAsync<JsonElement>("/api/v1/admin/search/status");
        status.GetProperty("engineEnabled").GetBoolean().ShouldBeTrue();
        status.GetProperty("index").GetString().ShouldBe(engine.SwappedTo);
    }

    [Fact]
    public async Task Without_an_engine_search_falls_back_to_titles_with_the_same_access_rules()
    {
        var admin = await factory.AdminAsync();
        var (owner, _, categoryId) = await OwnerOfNewCategoryAsync(admin, "degraded.owner");
        var (stranger, _, strangerCategory) = await OwnerOfNewCategoryAsync(admin, "degraded.stranger");

        var marker = $"degraded{Guid.NewGuid():N}"[..20];
        var (mine, _) = await owner.CreateDocumentAsync(categoryId, $"گزارش {marker}");
        var (theirs, _) = await stranger.CreateDocumentAsync(strangerCategory, $"گزارش {marker}");

        // The main test host has no OpenSearch configured.
        var result = await SearchAsync(owner, $"q={marker}");
        result.GetProperty("degraded").GetBoolean().ShouldBeTrue();
        DocumentIds(result).ShouldBe([mine]);
        DocumentIds(result).ShouldNotContain(theirs);
    }

    [Fact]
    public async Task Moving_a_category_reindexes_every_document_below_it()
    {
        var engine = new FakeSearchEngine();
        await using var host = factory.WithSearchFakes(engine, new FakeTextExtractor());
        var admin = await factory.AdminAsync();
        var (owner, ownerId, parent) = await OwnerOfNewCategoryAsync(admin, "move.owner");
        var child = await admin.CreateCategoryAsync(parent);
        await admin.GrantManyAsync("Category", child, ownerId, "DOCUMENT_VIEW", "DOCUMENT_CREATE");
        var target = await admin.CreateCategoryAsync();
        var (documentId, _) = await owner.CreateDocumentAsync(child, "سند در زیرپوشه");
        await host.Services.RunJobsAsync();

        IEnumerable<string> Path() => engine.VersionsOf(documentId).Single()["category_path"]!.AsArray().Select(node => node!.GetValue<string>());
        Path().ShouldContain(parent.ToString());

        var moved = await admin.PostAsJsonAsync($"/api/v1/admin/categories/{parent}/move", new { newParentId = target });
        moved.StatusCode.ShouldBe(HttpStatusCode.NoContent, await moved.Content.ReadAsStringAsync());
        await host.Services.RunJobsAsync();

        // "This folder and below" filters on the ancestor list, so it must follow the move.
        Path().ShouldContain(target.ToString());
        Path().ShouldContain(parent.ToString());
        Path().ShouldContain(child.ToString());
    }

    [Fact]
    public async Task Search_needs_a_signed_in_caller()
    {
        var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/search?q=x")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
