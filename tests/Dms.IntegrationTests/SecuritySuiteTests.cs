using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>
/// Phase 9 security regression suite: headers, anonymous surfaces, authorization boundaries and
/// title leakage. Complements Authentication, Authorization, Administration and Share tests.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SecuritySuiteTests(DmsApiFactory factory)
{
    [Fact]
    public async Task Every_response_carries_the_baseline_security_headers()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
        response.Headers.GetValues("Permissions-Policy").Single().ShouldContain("camera=()");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("frame-ancestors 'none'");
        response.Headers.GetValues("Cross-Origin-Opener-Policy").Single().ShouldBe("same-origin");
        // Development keeps HTTP usable: HSTS is for deployed TLS hosts.
        response.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }

    [Fact]
    public async Task Health_and_version_do_not_leak_secrets_or_connection_strings()
    {
        var client = factory.CreateClient();
        foreach (var path in new[] { "/health/live", "/health/ready", "/version" })
        {
            var body = await (await client.GetAsync(path)).Content.ReadAsStringAsync();
            body.ToLowerInvariant().ShouldNotContain("password");
            body.ToLowerInvariant().ShouldNotContain("signingkey");
            body.ToLowerInvariant().ShouldNotContain("sealkey");
            body.ShouldNotContain("Host=");
            body.ShouldNotContain("postgres://");
        }
    }

    [Fact]
    public async Task Administration_and_documents_refuse_anonymous_callers()
    {
        var client = factory.CreateClient();

        (await client.GetAsync("/api/v1/admin/users")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/documents")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/audit")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"/api/v1/documents/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_forged_bearer_token_is_rejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJmYWtlIn0.signature");

        (await client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/documents")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_stranger_cannot_read_another_users_document_by_id()
    {
        var admin = await factory.AdminAsync();
        var categoryId = await admin.CreateCategoryAsync();
        var (owner, ownerId) = await factory.CreateUserAsync("sec.owner");
        await admin.GrantManyAsync("Category", categoryId, ownerId, "DOCUMENT_VIEW", "DOCUMENT_CREATE");

        var (documentId, _) = await owner.CreateDocumentAsync(categoryId, title: "محرمانه");

        var (stranger, _) = await factory.CreateUserAsync("sec.stranger");
        var peek = await stranger.GetAsync($"/api/v1/documents/{documentId}");

        // Not Found, not Forbidden with a body that proves the id exists.
        peek.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var body = await peek.Content.ReadAsStringAsync();
        body.ShouldNotContain("محرمانه");
    }

    [Fact]
    public async Task Search_does_not_leak_titles_the_caller_may_not_see()
    {
        var admin = await factory.AdminAsync();
        var categoryId = await admin.CreateCategoryAsync();
        var (owner, ownerId) = await factory.CreateUserAsync("sec.search.owner");
        await admin.GrantManyAsync("Category", categoryId, ownerId, "DOCUMENT_VIEW", "DOCUMENT_CREATE");

        var secret = $"SECRET-{Guid.NewGuid():N}"[..20];
        await owner.CreateDocumentAsync(categoryId, title: secret);

        var (stranger, _) = await factory.CreateUserAsync("sec.search.stranger");
        var search = await stranger.GetAsync($"/api/v1/search?q={Uri.EscapeDataString(secret)}");
        search.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await search.Content.ReadAsStringAsync();
        payload.ShouldNotContain(secret);
    }

    [Fact]
    public async Task View_without_download_cannot_fetch_the_original_file()
    {
        var admin = await factory.AdminAsync();
        var categoryId = await admin.CreateCategoryAsync();
        var (owner, ownerId) = await factory.CreateUserAsync("sec.dl.owner");
        await admin.GrantManyAsync("Category", categoryId, ownerId, "DOCUMENT_VIEW", "DOCUMENT_CREATE");

        var (documentId, _) = await owner.CreateDocumentAsync(categoryId, title: "فقط مشاهده");

        var (viewer, viewerId) = await factory.CreateUserAsync("sec.dl.viewer");
        await admin.GrantManyAsync("Category", categoryId, viewerId, "DOCUMENT_VIEW");

        (await viewer.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var download = await viewer.GetAsync($"/api/v1/documents/{documentId}/content");
        download.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Uploaded_html_downloads_as_attachment_with_declared_type_not_executable_html()
    {
        var admin = await factory.AdminAsync();
        var categoryId = await admin.CreateCategoryAsync();
        var (owner, ownerId) = await factory.CreateUserAsync("sec.html.owner");
        await admin.GrantManyAsync("Category", categoryId, ownerId, "DOCUMENT_VIEW", "DOCUMENT_CREATE", "DOCUMENT_DOWNLOAD");

        var html = System.Text.Encoding.UTF8.GetBytes("<!doctype html><script>alert(1)</script>");
        var upload = await owner.UploadAsync(html, "note.html");
        var filed = await owner.PostAsJsonAsync("/api/v1/documents", new
        {
            title = "html upload",
            description = (string?)null,
            categoryId,
            documentTypeId = await owner.GeneralTypeIdAsync(),
            uploadId = upload.GetProperty("uploadId").GetGuid(),
            tags = Array.Empty<string>(),
            changeDescription = "n1",
        });
        filed.StatusCode.ShouldBe(HttpStatusCode.Created, await filed.Content.ReadAsStringAsync());
        var documentId = (await filed.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("documentId").GetGuid();

        var response = await owner.GetAsync($"/api/v1/documents/{documentId}/content");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        // Sniffed/stored type must not be served as text/html on our origin.
        response.Content.Headers.ContentType!.MediaType.ShouldNotBe("text/html");
        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
    }
}
