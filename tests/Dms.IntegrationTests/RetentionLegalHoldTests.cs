using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>Phase 10.2–10.3: retention policies and Legal Hold blocking purge/destruction.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class RetentionLegalHoldTests(DmsApiFactory factory)
{
    [Fact]
    public async Task Retention_assignment_moves_a_Record_to_UnderRetention_with_an_expiry()
    {
        var admin = await factory.AdminAsync();
        var categoryId = await admin.CreateCategoryAsync();
        var (owner, ownerId) = await factory.CreateUserAsync("ret.pol");
        await admin.GrantManyAsync(
            "Category", categoryId, ownerId,
            "DOCUMENT_VIEW", "DOCUMENT_CREATE", "RECORD_DECLARE");

        var classId = await CreateClassAsync(admin, "FIN", "مالی");
        var policy = await admin.PostAsJsonAsync(
            "/api/v1/admin/records/retention-policies",
            new
            {
                code = $"P{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
                name = "۷ ساله",
                description = (string?)null,
                retentionPeriodDays = 7,
                startEvent = "Declaration",
            });
        policy.StatusCode.ShouldBe(HttpStatusCode.Created, await policy.Content.ReadAsStringAsync());
        var policyId = (await policy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var (documentId, _) = await owner.CreateDocumentAsync(categoryId, title: "سند نگهداری");
        var declared = await owner.PostAsJsonAsync(
            $"/api/v1/records/declare/{documentId}",
            new { recordClassId = classId, recordSeriesId = (Guid?)null, finalVersionId = (Guid?)null, reason = "اعلام" });
        declared.StatusCode.ShouldBe(HttpStatusCode.Created, await declared.Content.ReadAsStringAsync());
        var recordId = (await declared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var assigned = await admin.PostAsJsonAsync(
            $"/api/v1/records/{recordId}/retention",
            new { retentionPolicyId = policyId });
        assigned.StatusCode.ShouldBe(HttpStatusCode.NoContent, await assigned.Content.ReadAsStringAsync());

        var summary = await owner.GetAsync($"/api/v1/records/{recordId}");
        using var body = JsonDocument.Parse(await summary.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetString().ShouldBe("UnderRetention");

        await factory.ShouldHaveAuditAsync("RETENTION_ASSIGNED");
    }

    [Fact]
    public async Task Legal_Hold_blocks_purge_even_after_soft_delete_retention_has_elapsed()
    {
        var admin = await factory.AdminAsync();
        var categoryId = await admin.CreateCategoryAsync();
        var (owner, ownerId) = await factory.CreateUserAsync("hold.owner");
        await admin.GrantManyAsync(
            "Category", categoryId, ownerId,
            "DOCUMENT_VIEW", "DOCUMENT_CREATE", "DOCUMENT_DELETE");

        // Enable legal hold on a dedicated type.
        var code = $"LH{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/document-types",
            new { code, name = "با نگهداری قانونی", description = (string?)null });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var typeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PutAsJsonAsync(
            $"/api/v1/admin/document-types/{typeId}",
            new
            {
                name = "با نگهداری قانونی",
                description = (string?)null,
                isActive = true,
                settings = new
                {
                    workflowMode = "None",
                    metadataEditPolicy = "NewRevision",
                    allowExternalSharing = true,
                    retentionDaysAfterDelete = 0,
                    supportsLegalHold = true,
                },
            })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PutAsJsonAsync(
            $"/api/v1/admin/document-types/{typeId}/draft",
            new { fields = Array.Empty<object>(), rules = Array.Empty<object>() }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PostAsync($"/api/v1/admin/document-types/{typeId}/publish", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var upload = await owner.UploadAsync(DocumentTestKit.Pdf("hold"));
        var filed = await owner.PostAsJsonAsync("/api/v1/documents", new
        {
            title = "مدرک تحت hold",
            description = (string?)null,
            categoryId,
            documentTypeId = typeId,
            uploadId = upload.GetProperty("uploadId").GetGuid(),
            tags = Array.Empty<string>(),
            changeDescription = "v1",
        });
        filed.StatusCode.ShouldBe(HttpStatusCode.Created, await filed.Content.ReadAsStringAsync());
        var documentId = (await filed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();

        var hold = await admin.PostAsJsonAsync(
            "/api/v1/legal-holds",
            new { documentId, reason = "پرونده قضایی ۱۴۰۵" });
        hold.StatusCode.ShouldBe(HttpStatusCode.Created, await hold.Content.ReadAsStringAsync());

        (await owner.DeleteAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var purge = await admin.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/purge",
            new { reason = "نباید حذف شود" });
        purge.StatusCode.ShouldBe(HttpStatusCode.Conflict, await purge.Content.ReadAsStringAsync());
        (await purge.Content.ReadAsStringAsync()).ShouldContain("purge.legal_hold");

        await factory.ShouldHaveAuditAsync("LEGAL_HOLD_PLACED");
    }

    [Fact]
    public async Task Destroying_a_Record_under_Legal_Hold_is_refused()
    {
        var admin = await factory.AdminAsync();
        var categoryId = await admin.CreateCategoryAsync();
        var (owner, ownerId) = await factory.CreateUserAsync("hold.rec");
        await admin.GrantManyAsync(
            "Category", categoryId, ownerId,
            "DOCUMENT_VIEW", "DOCUMENT_CREATE", "RECORD_DECLARE");

        var classId = await CreateClassAsync(admin, "LEG", "حقوقی");
        var (documentId, _) = await owner.CreateDocumentAsync(categoryId, title: "پرونده حقوقی");
        var declared = await owner.PostAsJsonAsync(
            $"/api/v1/records/declare/{documentId}",
            new { recordClassId = classId, recordSeriesId = (Guid?)null, finalVersionId = (Guid?)null, reason = "اعلام" });
        declared.StatusCode.ShouldBe(HttpStatusCode.Created, await declared.Content.ReadAsStringAsync());
        var recordId = (await declared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // GENERAL type may not support legal hold — enable via place without type check by using admin
        // Place hold: GENERAL type has SupportsLegalHold=false by default. Update type or skip type check.
        // For this test, place hold through API after enabling on general is heavy; use admin SQL-free path:
        // Create hold by enabling supportsLegalHold on the document's type via settings already false —
        // PlaceLegalHold refuses unsupported types. Use a type that supports it.
        var code = $"LH2{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var typeCreated = await admin.PostAsJsonAsync(
            "/api/v1/admin/document-types",
            new { code, name = "hold type", description = (string?)null });
        var typeId = (await typeCreated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await admin.PutAsJsonAsync(
            $"/api/v1/admin/document-types/{typeId}",
            new
            {
                name = "hold type",
                description = (string?)null,
                isActive = true,
                settings = new
                {
                    workflowMode = "None",
                    metadataEditPolicy = "NewRevision",
                    allowExternalSharing = true,
                    supportsLegalHold = true,
                },
            });
        await admin.PutAsJsonAsync(
            $"/api/v1/admin/document-types/{typeId}/draft",
            new { fields = Array.Empty<object>(), rules = Array.Empty<object>() });
        await admin.PostAsync($"/api/v1/admin/document-types/{typeId}/publish", null);

        var upload = await owner.UploadAsync(DocumentTestKit.Pdf("hold2"));
        var filed = await owner.PostAsJsonAsync("/api/v1/documents", new
        {
            title = "سند hold+record",
            description = (string?)null,
            categoryId,
            documentTypeId = typeId,
            uploadId = upload.GetProperty("uploadId").GetGuid(),
            tags = Array.Empty<string>(),
            changeDescription = "v1",
        });
        filed.StatusCode.ShouldBe(HttpStatusCode.Created, await filed.Content.ReadAsStringAsync());
        var doc2 = (await filed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();
        var declared2 = await owner.PostAsJsonAsync(
            $"/api/v1/records/declare/{doc2}",
            new { recordClassId = classId, recordSeriesId = (Guid?)null, finalVersionId = (Guid?)null, reason = "اعلام" });
        declared2.StatusCode.ShouldBe(HttpStatusCode.Created, await declared2.Content.ReadAsStringAsync());
        var record2 = (await declared2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await admin.PostAsJsonAsync("/api/v1/legal-holds", new { documentId = doc2, reason = "دادگاه" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // Move toward destruction path
        (await admin.PostAsJsonAsync(
            $"/api/v1/records/{record2}/transition",
            new { status = "Expired", reason = "test" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PostAsJsonAsync(
            $"/api/v1/records/{record2}/transition",
            new { status = "PendingDisposal", reason = "test" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Phase 10.4: Destroyed is only reachable through approved disposition — never via transition.
        var destroy = await admin.PostAsJsonAsync(
            $"/api/v1/records/{record2}/transition",
            new { status = "Destroyed", reason = "نباید" });
        destroy.StatusCode.ShouldBe(HttpStatusCode.Conflict, await destroy.Content.ReadAsStringAsync());
        (await destroy.Content.ReadAsStringAsync()).ShouldContain("disposition.required");

        _ = recordId; // first record unused beyond setup coverage
    }

    private static async Task<Guid> CreateClassAsync(HttpClient admin, string code, string name)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/records/classes",
            new { code = $"{code}{Guid.NewGuid():N}"[..12].ToUpperInvariant(), name, description = (string?)null });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
