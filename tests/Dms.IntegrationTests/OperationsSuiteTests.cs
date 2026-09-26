using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>Phase 10 cutover surfaces: build identity and retention-aware purge.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class OperationsSuiteTests(DmsApiFactory factory)
{
    [Fact]
    public async Task Version_endpoint_is_anonymous_and_names_the_build()
    {
        var response = await factory.CreateClient().GetAsync("/version");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("name").GetString().ShouldBe("dms");
        doc.RootElement.GetProperty("version").GetString().ShouldNotBeNullOrWhiteSpace();
        doc.RootElement.GetProperty("environment").GetString().ShouldNotBeNullOrWhiteSpace();
        doc.RootElement.GetProperty("role").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Purge_waits_for_type_retention_days_after_delete()
    {
        var admin = await factory.AdminAsync();
        var categoryId = await admin.CreateCategoryAsync();
        var (owner, ownerId) = await factory.CreateUserAsync("ret.owner");
        await admin.GrantManyAsync("Category", categoryId, ownerId, "DOCUMENT_VIEW", "DOCUMENT_CREATE", "DOCUMENT_DELETE");

        var code = $"RET{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/document-types",
            new { code, name = "با نگهداری", description = (string?)null });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var typeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await admin.PutAsJsonAsync(
            $"/api/v1/admin/document-types/{typeId}",
            new
            {
                name = "با نگهداری",
                description = (string?)null,
                isActive = true,
                settings = new
                {
                    workflowMode = "None",
                    metadataEditPolicy = "NewRevision",
                    allowExternalSharing = true,
                    retentionDaysAfterDelete = 30,
                    supportsLegalHold = true,
                },
            })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await admin.PutAsJsonAsync(
            $"/api/v1/admin/document-types/{typeId}/draft",
            new { fields = Array.Empty<object>(), rules = Array.Empty<object>() }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PostAsync($"/api/v1/admin/document-types/{typeId}/publish", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var upload = await owner.UploadAsync(DocumentTestKit.Pdf("retention"));
        var filed = await owner.PostAsJsonAsync("/api/v1/documents", new
        {
            title = "سند نگهداری‌شده",
            description = (string?)null,
            categoryId,
            documentTypeId = typeId,
            uploadId = upload.GetProperty("uploadId").GetGuid(),
            tags = Array.Empty<string>(),
            changeDescription = "نسخه‌ی اول",
        });
        filed.StatusCode.ShouldBe(HttpStatusCode.Created, await filed.Content.ReadAsStringAsync());
        var documentId = (await filed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();

        (await owner.DeleteAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var purge = await admin.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/purge",
            new { reason = "زود است" });
        purge.StatusCode.ShouldBe(HttpStatusCode.Conflict, await purge.Content.ReadAsStringAsync());
        (await purge.Content.ReadAsStringAsync()).ShouldContain("purge.retention");
    }
}
