using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>Phase 10.1: Record declaration, immutability through document APIs, audited transitions.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class RecordManagementTests(DmsApiFactory factory)
{
    [Fact]
    public async Task Declaring_a_Record_freezes_the_document_and_pins_the_final_version()
    {
        var admin = await factory.AdminAsync();
        var categoryId = await admin.CreateCategoryAsync();
        var (owner, ownerId) = await factory.CreateUserAsync("rec.owner");
        await admin.GrantManyAsync(
            "Category",
            categoryId,
            ownerId,
            "DOCUMENT_VIEW",
            "DOCUMENT_CREATE",
            "DOCUMENT_EDIT",
            "DOCUMENT_CREATE_VERSION",
            "DOCUMENT_DELETE",
            "RECORD_DECLARE");

        var classId = await CreateClassAsync(admin, "CONTRACTS", "قراردادها");
        var seriesId = await CreateSeriesAsync(admin, classId, "FY1405", "سال ۱۴۰۵");

        var (documentId, versionId) = await owner.CreateDocumentAsync(categoryId, title: "قرارداد نمونه");

        var declared = await owner.PostAsJsonAsync(
            $"/api/v1/records/declare/{documentId}",
            new { recordClassId = classId, recordSeriesId = seriesId, finalVersionId = (Guid?)null, reason = "بایگانی دائمی" });
        declared.StatusCode.ShouldBe(HttpStatusCode.Created, await declared.Content.ReadAsStringAsync());
        var recordId = (await declared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var summary = await owner.GetAsync($"/api/v1/records/{recordId}");
        summary.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await summary.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("finalVersionId").GetGuid().ShouldBe(versionId);
        body.RootElement.GetProperty("status").GetString().ShouldBe("Active");
        body.RootElement.GetProperty("finalVersionLabel").GetString().ShouldBe("V1.1");
        body.RootElement.GetProperty("recordClassCode").GetString().ShouldBe("CONTRACTS");

        var details = await owner.GetAsync($"/api/v1/documents/{documentId}");
        details.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await details.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("record").GetProperty("recordId").GetGuid().ShouldBe(recordId);

        var edit = await owner.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}",
            new { title = "تغییر ممنوع", description = (string?)null, categoryId });
        edit.StatusCode.ShouldBe(HttpStatusCode.Conflict, await edit.Content.ReadAsStringAsync());
        (await edit.Content.ReadAsStringAsync()).ShouldContain("record.immutable");

        var delete = await owner.DeleteAsync($"/api/v1/documents/{documentId}");
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await delete.Content.ReadAsStringAsync()).ShouldContain("record.immutable");

        await factory.ShouldHaveAuditAsync("RECORD_DECLARED");
    }

    [Fact]
    public async Task Record_status_transitions_are_audited()
    {
        var admin = await factory.AdminAsync();
        var categoryId = await admin.CreateCategoryAsync();
        var (owner, ownerId) = await factory.CreateUserAsync("rec.trans");
        await admin.GrantManyAsync(
            "Category",
            categoryId,
            ownerId,
            "DOCUMENT_VIEW",
            "DOCUMENT_CREATE",
            "RECORD_DECLARE");

        var classId = await CreateClassAsync(admin, "HR", "منابع انسانی");
        var (documentId, _) = await owner.CreateDocumentAsync(categoryId, title: "پرونده پرسنلی");

        var declared = await owner.PostAsJsonAsync(
            $"/api/v1/records/declare/{documentId}",
            new { recordClassId = classId, recordSeriesId = (Guid?)null, finalVersionId = (Guid?)null, reason = "اعلام" });
        declared.StatusCode.ShouldBe(HttpStatusCode.Created, await declared.Content.ReadAsStringAsync());
        var recordId = (await declared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var transition = await admin.PostAsJsonAsync(
            $"/api/v1/records/{recordId}/transition",
            new { status = "UnderRetention", reason = "شروع نگهداری" });
        transition.StatusCode.ShouldBe(HttpStatusCode.NoContent, await transition.Content.ReadAsStringAsync());

        var bad = await admin.PostAsJsonAsync(
            $"/api/v1/records/{recordId}/transition",
            new { status = "Destroyed", reason = "میان‌بر ممنوع" });
        bad.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await factory.ShouldHaveAuditAsync("RECORD_STATUS_CHANGED");
    }

    private static async Task<Guid> CreateClassAsync(HttpClient admin, string code, string name)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/records/classes",
            new { code, name, description = (string?)null });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateSeriesAsync(HttpClient admin, Guid classId, string code, string name)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/records/series",
            new { recordClassId = classId, code, name, description = (string?)null });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
