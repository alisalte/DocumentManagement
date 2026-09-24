using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using SkiaSharp;

namespace Dms.IntegrationTests;

/// <summary>
/// Phase 5, previews and print (section 7.4): page images rendered once by the worker, served
/// through the API with the same gates as a download, and never the original file.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PreviewApiTests(DmsApiFactory factory)
{
    private async Task<(HttpClient Admin, HttpClient Owner, Guid CategoryId)> ArrangeAsync(string who)
    {
        var admin = await factory.AdminAsync();
        var (owner, ownerId) = await factory.CreateUserAsync(who);
        var categoryId = await admin.CreateCategoryAsync();
        foreach (var permission in new[] { "DOCUMENT_VIEW", "DOCUMENT_CREATE", "DOCUMENT_DELETE" })
        {
            await admin.GrantAsync("Category", categoryId, ownerId, permission, inherit: true);
        }

        return (admin, owner, categoryId);
    }

    private static async Task<JsonElement> OpenPreviewAsync(HttpClient client, Guid documentId)
    {
        var response = await client.PostAsync($"/api/v1/documents/{documentId}/preview", null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<long> CountAuditAsync(string action, Guid documentId)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM audit.audit_logs WHERE action = @action AND document_id = @id;";
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("id", documentId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Previews_are_watermarked_page_images_and_opening_them_is_audited()
    {
        var (admin, owner, categoryId) = await ArrangeAsync("preview.owner");
        var (documentId, versionId) = await owner.CreateDocumentAsync(categoryId, "پیش‌نمایش", ProcessingTestKit.RealPdf("Page one", "Page two"), "two-pages.pdf");
        await factory.Services.RunJobsAsync();

        // A reader with VIEW alone: pages yes, the original file no.
        var (reader, readerId) = await factory.CreateUserAsync("preview.reader");
        await admin.GrantAsync("Category", categoryId, readerId, "DOCUMENT_VIEW", inherit: true);

        var preview = await OpenPreviewAsync(reader, documentId);
        preview.GetProperty("status").GetString().ShouldBe("Ready");
        preview.GetProperty("pageCount").GetInt32().ShouldBe(2);
        preview.GetProperty("versionId").GetGuid().ShouldBe(versionId);
        preview.GetProperty("canDownload").GetBoolean().ShouldBeFalse();
        preview.GetProperty("canPrint").GetBoolean().ShouldBeFalse();

        var page = await reader.GetAsync($"/api/v1/documents/{documentId}/versions/{versionId}/pages/1");
        page.StatusCode.ShouldBe(HttpStatusCode.OK, await page.Content.ReadAsStringAsync());
        page.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        page.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var readerBytes = await page.Content.ReadAsByteArrayAsync();
        using (var image = SKBitmap.Decode(readerBytes))
        {
            image.ShouldNotBeNull();
            image.Width.ShouldBe(1240);
        }

        // The watermark names the viewer, so two people never get the same bytes.
        var ownerBytes = await owner.GetByteArrayAsync($"/api/v1/documents/{documentId}/versions/{versionId}/pages/1");
        ownerBytes.ShouldNotBe(readerBytes);

        (await reader.GetAsync($"/api/v1/documents/{documentId}/versions/{versionId}/pages/3")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/documents/{documentId}/content")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Printing is its own permission, checked on the start and on every page.
        (await reader.PostAsync($"/api/v1/documents/{documentId}/versions/{versionId}/print", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync($"/api/v1/documents/{documentId}/versions/{versionId}/print/1")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await admin.GrantAsync("Category", categoryId, readerId, "DOCUMENT_PRINT", inherit: true);
        var print = await reader.PostAsync($"/api/v1/documents/{documentId}/versions/{versionId}/print", null);
        print.StatusCode.ShouldBe(HttpStatusCode.OK, await print.Content.ReadAsStringAsync());
        (await reader.GetAsync($"/api/v1/documents/{documentId}/versions/{versionId}/print/2")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await CountAuditAsync("DOCUMENT_VIEWED", documentId)).ShouldBe(1, "opening the viewer is recorded once, not per page");
        (await CountAuditAsync("DOCUMENT_PRINTED", documentId)).ShouldBe(1);

        // Somebody without VIEW learns nothing, not even that the document exists.
        var (outsider, _) = await factory.CreateUserAsync("preview.outsider");
        (await outsider.PostAsync($"/api/v1/documents/{documentId}/preview", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetAsync($"/api/v1/documents/{documentId}/versions/{versionId}/pages/1")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetAsync($"/api/v1/documents/{documentId}/thumbnail")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_image_gets_a_scaled_preview_and_a_thumbnail()
    {
        var (_, owner, categoryId) = await ArrangeAsync("preview.photo");
        var (documentId, _) = await owner.CreateDocumentAsync(categoryId, "عکس", ProcessingTestKit.Png(2000, 1000), "photo.png");
        await factory.Services.RunJobsAsync();

        var preview = await OpenPreviewAsync(owner, documentId);
        preview.GetProperty("status").GetString().ShouldBe("Ready");
        preview.GetProperty("pageCount").GetInt32().ShouldBe(1);

        var thumbnail = await owner.GetAsync($"/api/v1/documents/{documentId}/thumbnail");
        thumbnail.StatusCode.ShouldBe(HttpStatusCode.OK, await thumbnail.Content.ReadAsStringAsync());
        using var image = SKBitmap.Decode(await thumbnail.Content.ReadAsByteArrayAsync());
        image.Width.ShouldBe(320);
        image.Height.ShouldBe(160);
    }

    [Fact]
    public async Task Files_without_a_renderer_are_download_only()
    {
        var (_, owner, categoryId) = await ArrangeAsync("preview.text");
        var (documentId, _) = await owner.CreateDocumentAsync(categoryId, "یادداشت", "plain notes, no office converter configured"u8.ToArray(), "notes.txt");
        await factory.Services.RunJobsAsync();

        var preview = await OpenPreviewAsync(owner, documentId);
        preview.GetProperty("status").GetString().ShouldBe("NotSupported");
        preview.GetProperty("pageCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task A_page_is_only_served_for_a_version_of_the_same_document()
    {
        var (admin, owner, categoryId) = await ArrangeAsync("preview.mixed");
        var (documentId, versionId) = await owner.CreateDocumentAsync(categoryId, "اول", ProcessingTestKit.RealPdf("first"), "first.pdf");
        var (_, otherVersion) = await owner.CreateDocumentAsync(categoryId, "دوم", ProcessingTestKit.RealPdf("second"), "second.pdf");
        await factory.Services.RunJobsAsync();

        var (reader, readerId) = await factory.CreateUserAsync("preview.mixed.reader");
        await admin.GrantAsync("Document", documentId, readerId, "DOCUMENT_VIEW");

        // VIEW on one document must not open another document's pages by pairing ids.
        (await reader.GetAsync($"/api/v1/documents/{documentId}/versions/{otherVersion}/pages/1")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/documents/{documentId}/versions/{versionId}/pages/1")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Purging_a_document_deletes_its_page_images_with_the_original()
    {
        var (admin, owner, categoryId) = await ArrangeAsync("preview.purge");
        var (documentId, versionId) = await owner.CreateDocumentAsync(categoryId, "حذف کامل", ProcessingTestKit.RealPdf("gone"), "gone.pdf");
        await factory.Services.RunJobsAsync();

        await using var connection = await factory.OpenConnectionAsync();
        Guid source;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT storage_object_id FROM documents.document_versions WHERE id = @id;";
            command.Parameters.AddWithValue("id", versionId);
            source = (Guid)(await command.ExecuteScalarAsync())!;
        }

        async Task<(long Derived, long Alive)> CountAsync()
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT count(*), count(*) FILTER (WHERE status NOT IN ('PendingDeletion', 'Deleted'))
                  FROM storage.storage_objects WHERE derived_from_id = @source;
                """;
            command.Parameters.AddWithValue("source", source);
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            return (reader.GetInt64(0), reader.GetInt64(1));
        }

        (await CountAsync()).Derived.ShouldBe(2, "one page image and one thumbnail");

        (await owner.DeleteAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var purged = await admin.PostAsJsonAsync($"/api/v1/documents/{documentId}/purge", new { reason = "retention" });
        purged.StatusCode.ShouldBe(HttpStatusCode.NoContent, await purged.Content.ReadAsStringAsync());

        (await CountAsync()).Alive.ShouldBe(0, "nothing of a purged document may survive in derived form");
    }
}
