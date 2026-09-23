using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>
/// Phase 2 exit criteria (docs/architecture.md section 10): upload; V1/V2; current and previous
/// version; concurrent version creation; the immutability trigger; soft delete and restore; audit
/// events; and 404/403 for callers without the right permission.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DocumentApiTests(DmsApiFactory factory)
{
    /// <summary>A category where one fresh user may browse, file, delete and restore.</summary>
    private async Task<(HttpClient Admin, HttpClient User, Guid UserId, Guid CategoryId)> ArrangeAsync(string who)
    {
        var admin = await factory.AdminAsync();
        var (user, userId) = await factory.CreateUserAsync(who);
        var categoryId = await admin.CreateCategoryAsync();

        foreach (var permission in new[] { "DOCUMENT_VIEW", "DOCUMENT_CREATE", "DOCUMENT_DELETE", "DOCUMENT_RESTORE" })
        {
            await admin.GrantAsync("Category", categoryId, userId, permission, inherit: true);
        }

        return (admin, user, userId, categoryId);
    }

    [Fact]
    public async Task An_upload_is_hashed_and_typed_by_its_content_not_by_the_client()
    {
        var (_, user, _, _) = await ArrangeAsync("uploader");
        var bytes = DocumentTestKit.Pdf("hash me");

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new("text/html");
        form.Add(file, "file", "../../etc/گزارش.pdf");

        var response = await user.PostAsync("/api/v1/uploads", form);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var upload = await response.Content.ReadFromJsonAsync<JsonElement>();
        upload.GetProperty("sha256").GetString().ShouldBe(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        upload.GetProperty("mimeType").GetString().ShouldBe("application/pdf");
        upload.GetProperty("size").GetInt64().ShouldBe(bytes.Length);

        // Path segments never survive: the name is a leaf, and it never reaches the storage key.
        upload.GetProperty("fileName").GetString().ShouldBe("گزارش.pdf");
    }

    [Fact]
    public async Task An_empty_upload_is_refused()
    {
        var (_, user, _, _) = await ArrangeAsync("empty.uploader");

        var response = await user.UploadRawAsync([], "empty.pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Versions_are_numbered_and_each_one_stays_downloadable()
    {
        var (_, user, _, categoryId) = await ArrangeAsync("versioner");
        var v1Bytes = DocumentTestKit.Pdf("first");
        var v2Bytes = DocumentTestKit.Pdf("second");

        var (documentId, v1) = await user.CreateDocumentAsync(categoryId, content: v1Bytes);
        var added = await user.AddVersionRawAsync(documentId, v2Bytes, baseVersionId: v1);
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync());
        var v2Body = await added.Content.ReadFromJsonAsync<JsonElement>();
        v2Body.GetProperty("label").GetString().ShouldBe("V2.1");
        var v2 = v2Body.GetProperty("versionId").GetGuid();

        // Current: the plain content endpoint serves the latest (effective) version.
        (await user.GetByteArrayAsync($"/api/v1/documents/{documentId}/content")).ShouldBe(v2Bytes);

        // Previous: V1 is untouched and still addressable.
        (await user.GetByteArrayAsync($"/api/v1/documents/{documentId}/versions/{v1}/content")).ShouldBe(v1Bytes);

        var versions = await user.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}/versions");
        var rows = versions.EnumerateArray().ToList();
        rows.Count.ShouldBe(2);
        rows[0].GetProperty("id").GetGuid().ShouldBe(v2);
        rows[0].GetProperty("isCurrent").GetBoolean().ShouldBeTrue();
        rows[0].GetProperty("isEffective").GetBoolean().ShouldBeTrue();
        rows[1].GetProperty("label").GetString().ShouldBe("V1.1");
        rows[1].GetProperty("isCurrent").GetBoolean().ShouldBeFalse();
        rows[1].GetProperty("sha256").GetString().ShouldBe(Convert.ToHexStringLower(SHA256.HashData(v1Bytes)));

        var details = await user.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}");
        details.GetProperty("latestVersionNumber").GetInt32().ShouldBe(2);
        details.GetProperty("currentVersionId").GetGuid().ShouldBe(v2);
    }

    [Fact]
    public async Task Concurrent_version_uploads_each_get_their_own_number()
    {
        var (_, user, _, categoryId) = await ArrangeAsync("racer");
        var (documentId, _) = await user.CreateDocumentAsync(categoryId);

        // Stage first, then fire the attach calls together: the race is on number allocation.
        var uploads = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            var upload = await user.UploadAsync(DocumentTestKit.Pdf($"race {i}"), $"race-{i}.pdf");
            uploads.Add(upload.GetProperty("uploadId").GetGuid());
        }

        var responses = await Task.WhenAll(uploads.Select(uploadId =>
            user.PostAsJsonAsync($"/api/v1/documents/{documentId}/versions", new { uploadId, changeDescription = "race" })));

        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        }

        var labels = new List<string>();
        foreach (var response in responses)
        {
            labels.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("label").GetString()!);
        }

        labels.Order().ShouldBe(["V2.1", "V3.1", "V4.1", "V5.1", "V6.1", "V7.1"]);

        var details = await user.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}");
        details.GetProperty("latestVersionNumber").GetInt32().ShouldBe(7);
    }

    [Fact]
    public async Task A_stale_base_version_is_a_conflict()
    {
        var (_, user, _, categoryId) = await ArrangeAsync("stale");
        var (documentId, v1) = await user.CreateDocumentAsync(categoryId);
        (await user.AddVersionRawAsync(documentId, DocumentTestKit.Pdf("v2"), v1)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // Someone who opened V1 and uploads without noticing V2.
        var stale = await user.AddVersionRawAsync(documentId, DocumentTestKit.Pdf("v2 again"), v1);

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_database_refuses_to_change_or_delete_a_version()
    {
        var (_, user, _, categoryId) = await ArrangeAsync("immutable");
        var (_, versionId) = await user.CreateDocumentAsync(categoryId);

        await using var connection = await factory.OpenConnectionAsync();

        async Task<PostgresException?> TryAsync(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("id", versionId);
            try
            {
                await command.ExecuteNonQueryAsync();
                return null;
            }
            catch (PostgresException exception)
            {
                return exception;
            }
        }

        (await TryAsync("UPDATE documents.document_versions SET file_name = 'forged.pdf' WHERE id = @id"))
            .ShouldNotBeNull().MessageText.ShouldContain("immutable");
        (await TryAsync("UPDATE documents.document_versions SET sha256 = sha256(sha256) WHERE id = @id"))
            .ShouldNotBeNull();
        (await TryAsync("DELETE FROM documents.document_versions WHERE id = @id"))
            .ShouldNotBeNull().MessageText.ShouldContain("purge");
        (await TryAsync("TRUNCATE documents.document_versions CASCADE"))
            .ShouldNotBeNull();

        // The one legitimate change, driven by the workflow in phase 4.
        (await TryAsync("UPDATE documents.document_versions SET approval_status = 'Approved' WHERE id = @id"))
            .ShouldBeNull();
    }

    [Fact]
    public async Task Soft_delete_hides_the_document_and_restore_brings_it_back()
    {
        var (_, user, _, categoryId) = await ArrangeAsync("deleter");
        var (documentId, _) = await user.CreateDocumentAsync(categoryId, title: "برای حذف");

        var deleted = await user.DeleteAsync($"/api/v1/documents/{documentId}?reason=duplicate");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await user.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await user.GetAsync($"/api/v1/documents/{documentId}/content")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var listed = await user.GetFromJsonAsync<JsonElement>($"/api/v1/documents?categoryId={categoryId}");
        listed.GetProperty("items").EnumerateArray()
            .Any(item => item.GetProperty("id").GetGuid() == documentId).ShouldBeFalse();

        var bin = await user.GetFromJsonAsync<JsonElement>("/api/v1/recycle-bin");
        var binned = bin.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == documentId);
        binned.GetProperty("deleteReason").GetString().ShouldBe("duplicate");

        var restored = await user.PostAsync($"/api/v1/documents/{documentId}/restore", null);
        restored.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await user.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await factory.ShouldHaveAuditAsync("DOCUMENT_DELETED");
        await factory.ShouldHaveAuditAsync("DOCUMENT_RESTORED");
    }

    [Fact]
    public async Task Purge_needs_the_recycle_bin_first_and_then_removes_everything()
    {
        var (admin, user, _, categoryId) = await ArrangeAsync("purger");
        var (documentId, _) = await user.CreateDocumentAsync(categoryId);
        await user.AddVersionRawAsync(documentId, DocumentTestKit.Pdf("v2"));

        // A live document is never purged in one step.
        var tooEarly = await admin.PostAsJsonAsync($"/api/v1/documents/{documentId}/purge", new { reason = "test" });
        tooEarly.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Deleting is the owner's business; purging is not.
        (await user.DeleteAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var byUser = await user.PostAsJsonAsync($"/api/v1/documents/{documentId}/purge", new { reason = "test" });
        byUser.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var purged = await admin.PostAsJsonAsync($"/api/v1/documents/{documentId}/purge", new { reason = "retention" });
        purged.StatusCode.ShouldBe(HttpStatusCode.NoContent, await purged.Content.ReadAsStringAsync());

        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT (SELECT count(*) FROM documents.documents WHERE id = @id),
                   (SELECT count(*) FROM documents.document_versions WHERE document_id = @id),
                   (SELECT count(*) FROM storage.storage_objects
                     WHERE status = 'PendingDeletion'
                       AND id IN (SELECT (metadata -> 'files' -> 0 ->> 'storageObjectId')::uuid
                                    FROM audit.audit_logs
                                   WHERE action = 'DOCUMENT_PURGED' AND document_id = @id));
            """;
        command.Parameters.AddWithValue("id", documentId);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        reader.GetInt64(0).ShouldBe(0);
        reader.GetInt64(1).ShouldBe(0);
        reader.GetInt64(2).ShouldBe(1, "the purged file is queued for deletion and kept as a tombstone");
    }

    [Fact]
    public async Task Creating_a_document_writes_the_expected_audit_trail()
    {
        var (_, user, _, categoryId) = await ArrangeAsync("audited");
        var (documentId, versionId) = await user.CreateDocumentAsync(categoryId);
        await user.GetByteArrayAsync($"/api/v1/documents/{documentId}/content");

        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT action FROM audit.audit_logs WHERE document_id = @id ORDER BY occurred_at, id;
            """;
        command.Parameters.AddWithValue("id", documentId);

        var actions = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                actions.Add(reader.GetString(0));
            }
        }

        actions.ShouldContain("DOCUMENT_CREATED");
        actions.ShouldContain("VERSION_CREATED");
        actions.ShouldContain("DOCUMENT_DOWNLOADED");

        // Owner defaults are ordinary, audited grants (section 5.5), not hidden rights.
        actions.Count(action => action == "PERMISSION_GRANTED").ShouldBe(4);

        await using var versionAudit = connection.CreateCommand();
        versionAudit.CommandText = "SELECT count(*) FROM audit.audit_logs WHERE version_id = @v AND action = 'DOCUMENT_DOWNLOADED'";
        versionAudit.Parameters.AddWithValue("v", versionId);
        ((long)(await versionAudit.ExecuteScalarAsync())!).ShouldBe(1);
    }

    [Fact]
    public async Task Without_view_a_document_does_not_exist_and_the_attempt_is_audited()
    {
        var (_, owner, _, categoryId) = await ArrangeAsync("secret.owner");
        var (documentId, versionId) = await owner.CreateDocumentAsync(categoryId, title: "محرمانه");
        var (stranger, _) = await factory.CreateUserAsync("stranger");

        (await stranger.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.GetAsync($"/api/v1/documents/{documentId}/versions")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.GetAsync($"/api/v1/documents/{documentId}/content")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.GetAsync($"/api/v1/documents/{documentId}/versions/{versionId}/content"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.DeleteAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Same answer as for an id that never existed: probing reveals nothing.
        (await stranger.GetAsync($"/api/v1/documents/{Guid.CreateVersion7()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var listed = await stranger.GetFromJsonAsync<JsonElement>("/api/v1/documents?search=محرمانه");
        listed.GetProperty("total").GetInt32().ShouldBe(0);

        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*) FROM audit.audit_logs
             WHERE action = 'ACCESS_DENIED' AND outcome = 'DENIED' AND document_id = @id;
            """;
        command.Parameters.AddWithValue("id", documentId);
        ((long)(await command.ExecuteScalarAsync())!).ShouldBeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public async Task View_without_download_sees_the_document_but_not_the_bytes()
    {
        var (admin, owner, _, categoryId) = await ArrangeAsync("dl.owner");
        var (documentId, _) = await owner.CreateDocumentAsync(categoryId);
        var (reader, readerId) = await factory.CreateUserAsync("viewer.only");
        await admin.GrantAsync("Document", documentId, readerId, "DOCUMENT_VIEW");

        var details = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}");
        details.GetProperty("allowedActions").EnumerateArray().Select(action => action.GetString())
            .ShouldBe(["DOCUMENT_VIEW"]);

        (await reader.GetAsync($"/api/v1/documents/{documentId}/content")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.AddVersionRawAsync(documentId, DocumentTestKit.Pdf("sneaky")))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_explicit_deny_on_the_document_beats_the_category_allow()
    {
        var (admin, user, userId, categoryId) = await ArrangeAsync("denied");
        var (documentId, _) = await user.CreateDocumentAsync(categoryId, title: "ممنوع");
        var (otherId, _) = await user.CreateDocumentAsync(categoryId, title: "مجاز");

        await admin.GrantAsync("Document", documentId, userId, "DOCUMENT_VIEW", effect: "Deny");

        (await user.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await user.GetAsync($"/api/v1/documents/{otherId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var listed = await user.GetFromJsonAsync<JsonElement>($"/api/v1/documents?categoryId={categoryId}");
        var ids = listed.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
        ids.ShouldContain(otherId);
        ids.ShouldNotContain(documentId);
    }

    [Fact]
    public async Task Filing_needs_create_on_the_category()
    {
        var admin = await factory.AdminAsync();
        var (user, userId) = await factory.CreateUserAsync("no.create");
        var categoryId = await admin.CreateCategoryAsync();
        await admin.GrantAsync("Category", categoryId, userId, "DOCUMENT_VIEW");

        var upload = await user.UploadAsync(DocumentTestKit.Pdf("x"));
        var response = await user.CreateDocumentRawAsync(categoryId, upload.GetProperty("uploadId").GetGuid(), "بدون مجوز");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Nobody_can_attach_someone_elses_upload()
    {
        var (_, victim, _, _) = await ArrangeAsync("victim");
        var (_, thief, _, thiefCategory) = await ArrangeAsync("thief");
        var upload = await victim.UploadAsync(DocumentTestKit.Pdf("private"));

        var response = await thief.CreateDocumentRawAsync(thiefCategory, upload.GetProperty("uploadId").GetGuid(), "stolen");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_upload_can_be_attached_only_once()
    {
        var (_, user, _, categoryId) = await ArrangeAsync("once");
        var upload = await user.UploadAsync(DocumentTestKit.Pdf("once"));
        var uploadId = upload.GetProperty("uploadId").GetGuid();

        (await user.CreateDocumentRawAsync(categoryId, uploadId, "first")).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await user.CreateDocumentRawAsync(categoryId, uploadId, "second")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_retried_create_with_the_same_idempotency_key_files_one_document()
    {
        var (_, user, _, categoryId) = await ArrangeAsync("retrier");
        var upload = await user.UploadAsync(DocumentTestKit.Pdf("retry"));
        var uploadId = upload.GetProperty("uploadId").GetGuid();
        var key = Guid.NewGuid().ToString();

        var first = await user.CreateDocumentRawAsync(categoryId, uploadId, "تکرار", idempotencyKey: key);
        var second = await user.CreateDocumentRawAsync(categoryId, uploadId, "تکرار", idempotencyKey: key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created, await second.Content.ReadAsStringAsync());
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();
        secondId.ShouldBe(firstId);

        var misuse = await user.CreateDocumentRawAsync(categoryId, uploadId, "چیز دیگر", idempotencyKey: key);
        misuse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Listing_follows_inheritance_down_the_tree()
    {
        var admin = await factory.AdminAsync();
        var (owner, ownerId) = await factory.CreateUserAsync("tree.owner");
        var (reader, readerId) = await factory.CreateUserAsync("tree.reader");

        var parent = await admin.CreateCategoryAsync();
        var child = await admin.CreateCategoryAsync(parent);
        await admin.GrantManyAsync("Category", child, ownerId, "DOCUMENT_VIEW", "DOCUMENT_CREATE");
        var (documentId, _) = await owner.CreateDocumentAsync(child, title: "در زیرشاخه");

        // Not inherited: an entry on the parent stops at the parent.
        await admin.GrantAsync("Category", parent, readerId, "DOCUMENT_VIEW", inherit: false);
        (await reader.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Inherited: now it reaches the child and its documents, in single reads and in lists.
        await admin.GrantAsync("Category", parent, readerId, "DOCUMENT_VIEW", inherit: true);
        (await reader.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var listed = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/v1/documents?categoryId={parent}&includeSubcategories=true");
        listed.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid()).ShouldContain(documentId);
    }

    [Fact]
    public async Task Moving_a_category_moves_its_subtree_and_the_inherited_access_with_it()
    {
        var admin = await factory.AdminAsync();
        var (reader, readerId) = await factory.CreateUserAsync("mover");
        var source = await admin.CreateCategoryAsync();
        var target = await admin.CreateCategoryAsync();
        var moving = await admin.CreateCategoryAsync(source);
        var grandchild = await admin.CreateCategoryAsync(moving);
        await admin.GrantAsync("Category", target, readerId, "DOCUMENT_VIEW", inherit: true);

        (await admin.PostAsJsonAsync($"/api/v1/admin/categories/{moving}/move", new { newParentId = target }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT path::text, depth FROM documents.categories WHERE id = @id";
        command.Parameters.AddWithValue("id", grandchild);
        await using (var row = await command.ExecuteReaderAsync())
        {
            await row.ReadAsync();
            row.GetString(0).ShouldEndWith($".{target:N}.{moving:N}.{grandchild:N}");
            row.GetInt32(1).ShouldBe(3);
        }

        var tree = await reader.GetFromJsonAsync<JsonElement>("/api/v1/categories");
        tree.EnumerateArray().Single(node => node.GetProperty("id").GetGuid() == grandchild)
            .GetProperty("canView").GetBoolean().ShouldBeTrue();

        // And no cycles: a category cannot move under its own descendant.
        (await admin.PostAsJsonAsync($"/api/v1/admin/categories/{moving}/move", new { newParentId = grandchild }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Tags_written_with_arabic_or_persian_letters_are_one_tag()
    {
        var (_, user, _, categoryId) = await ArrangeAsync("tagger");
        var (documentId, _) = await user.CreateDocumentAsync(categoryId);

        var set = await user.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}/tags",
            new { tags = new[] { "كتاب", "کتاب", "قرارداد ۱۴۰۳" } });
        set.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var details = await user.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}");
        details.GetProperty("tags").GetArrayLength().ShouldBe(2);

        var suggestions = await user.GetFromJsonAsync<JsonElement>("/api/v1/tags?search=قرارداد 1403");
        suggestions.EnumerateArray().Select(tag => tag.GetProperty("name").GetString()).ShouldContain("قرارداد ۱۴۰۳");
    }

    [Fact]
    public async Task A_persian_file_name_survives_the_download_header()
    {
        var (_, user, _, categoryId) = await ArrangeAsync("persian.name");
        var (documentId, _) = await user.CreateDocumentAsync(categoryId, fileName: "صورت‌جلسه.pdf");

        var response = await user.GetAsync($"/api/v1/documents/{documentId}/content");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("صورت‌جلسه.pdf");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
    }

    [Fact]
    public async Task The_duplicate_notice_only_names_documents_the_uploader_can_see()
    {
        var (_, owner, _, categoryId) = await ArrangeAsync("dup.owner");
        var bytes = Encoding.UTF8.GetBytes($"%PDF-1.7\n% same file {Guid.NewGuid()}\n");
        var (documentId, _) = await owner.CreateDocumentAsync(categoryId, content: bytes);

        var again = await owner.UploadAsync(bytes);
        again.GetProperty("duplicates").EnumerateArray()
            .Select(item => item.GetProperty("documentId").GetGuid()).ShouldContain(documentId);

        var (stranger, _) = await factory.CreateUserAsync("dup.stranger");
        var probe = await stranger.UploadAsync(bytes);
        probe.GetProperty("duplicates").GetArrayLength().ShouldBe(0);
    }
}
