using System.Net;
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
        // Development keeps HTTP usable: HSTS is for deployed TLS hosts.
        response.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
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
}
