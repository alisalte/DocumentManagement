using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>
/// Phase 3 exit criteria (docs/architecture.md section 10): the validation matrix end to end,
/// conditional fields, and old documents still read with their original schema version. Also
/// covers metadata revisions (ADR 0001) and the immutability of published schemas.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DocumentTypeApiTests(DmsApiFactory factory)
{
    /// <summary>
    /// A contract type: an approval-relevant amount, a kind that decides whether a vendor is shown,
    /// and an approver required above ten billion.
    /// </summary>
    private static object ContractSchema(bool withDepartment = false)
    {
        var fields = new List<object>
        {
            new
            {
                code = "kind", label = new { fa = "نوع" }, type = "Select", isRequired = true,
                options = new[] { new { value = "contract", label = new { fa = "قرارداد" } }, new { value = "letter", label = new { fa = "نامه" } } },
            },
            new { code = "amount", label = new { fa = "مبلغ" }, type = "Decimal", isApprovalRelevant = true, validation = new { min = 0, scale = 2 } },
            new { code = "vendor", label = new { fa = "طرف قرارداد" }, type = "Text", validation = new { maxLength = 100 } },
            new { code = "approver", label = new { fa = "تأییدکننده" }, type = "User" },
            new { code = "signed_on", label = new { fa = "تاریخ امضا" }, type = "Date" },
            new { code = "related", label = new { fa = "سند مرتبط" }, type = "DocumentReference" },
            new { code = "note", label = new { fa = "یادداشت" }, type = "LongText" },
        };

        if (withDepartment)
        {
            fields.Add(new { code = "department", label = new { fa = "واحد" }, type = "Text", isRequired = true });
        }

        return new
        {
            fields,
            rules = new object[]
            {
                new
                {
                    kind = "Show",
                    targets = new[] { "vendor" },
                    condition = new { field = "kind", op = "eq", value = "contract" },
                },
                new
                {
                    kind = "Require",
                    targets = new[] { "approver" },
                    condition = new { field = "amount", op = "gt", value = 10_000_000_000m },
                },
            },
        };
    }

    private sealed record Arranged(HttpClient Admin, HttpClient User, Guid UserId, Guid CategoryId, Guid TypeId, Guid SchemaV1);

    private async Task<Arranged> ArrangeAsync(string who, string policy = "NewRevision")
    {
        var admin = await factory.AdminAsync();
        var (user, userId) = await factory.CreateUserAsync(who);
        var categoryId = await admin.CreateCategoryAsync();
        await admin.GrantManyAsync("Category", categoryId, userId, "DOCUMENT_VIEW", "DOCUMENT_CREATE");

        var code = $"T{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        var created = await admin.PostAsJsonAsync("/api/v1/admin/document-types", new { code, name = "قرارداد", description = (string?)null });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var typeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await admin.PutAsJsonAsync($"/api/v1/admin/document-types/{typeId}", new
        {
            name = "قرارداد",
            description = (string?)null,
            settings = new { metadataEditPolicy = policy },
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var saved = await admin.PutAsJsonAsync($"/api/v1/admin/document-types/{typeId}/draft", ContractSchema());
        saved.StatusCode.ShouldBe(HttpStatusCode.NoContent, await saved.Content.ReadAsStringAsync());

        var published = await admin.PostAsync($"/api/v1/admin/document-types/{typeId}/publish", null);
        published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync());
        var schemaV1 = (await published.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("versionId").GetGuid();

        return new Arranged(admin, user, userId, categoryId, typeId, schemaV1);
    }

    private static async Task<HttpResponseMessage> FileAsync(Arranged arranged, object metadata)
    {
        var upload = await arranged.User.UploadAsync(DocumentTestKit.Pdf($"typed {Guid.NewGuid()}"));
        return await arranged.User.PostAsJsonAsync("/api/v1/documents", new
        {
            title = "قرارداد نمونه",
            description = (string?)null,
            categoryId = arranged.CategoryId,
            documentTypeId = arranged.TypeId,
            uploadId = upload.GetProperty("uploadId").GetGuid(),
            tags = Array.Empty<string>(),
            changeDescription = (string?)null,
            metadata,
        });
    }

    private static async Task<JsonElement> ErrorsOf(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
    }

    [Fact]
    public async Task Invalid_metadata_is_refused_field_by_field()
    {
        var arranged = await ArrangeAsync("matrix");

        var errors = await ErrorsOf(await FileAsync(arranged, new
        {
            amount = -5,
            signed_on = "1403/01/01",
            approver = "not-a-guid",
            unknown_field = "x",
        }));

        errors.EnumerateObject().Select(property => property.Name).Order()
            .ShouldBe(["amount", "approver", "kind", "signed_on", "unknown_field"]);
    }

    [Fact]
    public async Task Valid_metadata_is_normalised_stored_and_read_back_with_decimals_as_strings()
    {
        var arranged = await ArrangeAsync("normaliser");

        var response = await FileAsync(arranged, new
        {
            kind = "contract",
            amount = "۱۲٬۵۰۰٫۷۵",
            vendor = "  شرکت نمونه  ",
            signed_on = "2024-03-20",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var documentId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();

        var details = await arranged.User.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}");
        var metadata = details.GetProperty("currentMetadata");
        metadata.GetProperty("amount").GetString().ShouldBe("12500.75");
        metadata.GetProperty("vendor").GetString().ShouldBe("شرکت نمونه");
        details.GetProperty("currentSchemaVersionId").GetGuid().ShouldBe(arranged.SchemaV1);
    }

    [Fact]
    public async Task Conditional_fields_are_hidden_or_required_by_the_rules()
    {
        var arranged = await ArrangeAsync("conditional");

        // Above ten billion the approver becomes required.
        (await ErrorsOf(await FileAsync(arranged, new { kind = "contract", amount = 12_000_000_000m })))
            .TryGetProperty("approver", out _).ShouldBeTrue();

        // With an approver it passes.
        (await FileAsync(arranged, new { kind = "contract", amount = 12_000_000_000m, approver = arranged.UserId }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // For a letter the vendor is hidden, so a stale vendor value is not stored.
        var letter = await FileAsync(arranged, new { kind = "letter", vendor = "باید حذف شود" });
        letter.StatusCode.ShouldBe(HttpStatusCode.Created, await letter.Content.ReadAsStringAsync());
        var documentId = (await letter.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();
        var details = await arranged.User.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}");
        details.GetProperty("currentMetadata").TryGetProperty("vendor", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task References_must_point_at_real_users_and_visible_documents()
    {
        var arranged = await ArrangeAsync("references");

        // A user that does not exist.
        (await ErrorsOf(await FileAsync(arranged, new { kind = "letter", approver = Guid.NewGuid() })))
            .TryGetProperty("approver", out _).ShouldBeTrue();

        // A document the author cannot see: refused exactly like one that does not exist.
        var (owner, ownerId) = await factory.CreateUserAsync("ref.owner");
        var hidden = await CreateHiddenAsync(arranged.Admin, owner, ownerId);

        (await ErrorsOf(await FileAsync(arranged, new { kind = "letter", related = hidden })))
            .TryGetProperty("related", out _).ShouldBeTrue();

        // Once the author can see it, the same reference is accepted.
        await arranged.Admin.GrantAsync("Document", hidden, arranged.UserId, "DOCUMENT_VIEW");
        (await FileAsync(arranged, new { kind = "letter", related = hidden })).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<Guid> CreateHiddenAsync(HttpClient admin, HttpClient owner, Guid ownerId)
    {
        var category = await admin.CreateCategoryAsync();
        await admin.GrantManyAsync("Category", category, ownerId, "DOCUMENT_VIEW", "DOCUMENT_CREATE");
        var (documentId, _) = await owner.CreateDocumentAsync(category, "پنهان");
        return documentId;
    }

    [Fact]
    public async Task Old_documents_keep_reading_with_their_original_schema()
    {
        var arranged = await ArrangeAsync("schemas");
        var filed = await FileAsync(arranged, new { kind = "letter", note = "v1" });
        var documentId = (await filed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();

        // Schema v2 adds a required department.
        (await arranged.Admin.PutAsJsonAsync($"/api/v1/admin/document-types/{arranged.TypeId}/draft", ContractSchema(withDepartment: true)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var published = await arranged.Admin.PostAsync($"/api/v1/admin/document-types/{arranged.TypeId}/publish", null);
        var schemaV2 = (await published.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("versionId").GetGuid();

        // The old document still points at v1, and v1 is still served unchanged.
        var details = await arranged.User.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}");
        details.GetProperty("currentSchemaVersionId").GetGuid().ShouldBe(arranged.SchemaV1);
        var v1 = await arranged.User.GetFromJsonAsync<JsonElement>($"/api/v1/document-types/schemas/{arranged.SchemaV1}");
        v1.GetProperty("fields").EnumerateArray().Any(field => field.GetProperty("code").GetString() == "department").ShouldBeFalse();

        // New documents need the department now.
        (await ErrorsOf(await FileAsync(arranged, new { kind = "letter" }))).TryGetProperty("department", out _).ShouldBeTrue();

        // Editing the old one validates against its own schema...
        var edited = await arranged.User.PutAsJsonAsync($"/api/v1/documents/{documentId}/metadata", new { metadata = new { kind = "letter", note = "v1 edited" } });
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        (await edited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("label").GetString().ShouldBe("V1.2");

        // ...until the author explicitly moves it onto the new schema.
        var upgraded = await arranged.User.PutAsJsonAsync($"/api/v1/documents/{documentId}/metadata", new
        {
            metadata = new { kind = "letter", note = "v2", department = "مالی" },
            upgradeSchema = true,
        });
        upgraded.StatusCode.ShouldBe(HttpStatusCode.OK, await upgraded.Content.ReadAsStringAsync());

        var versions = await arranged.User.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}/versions");
        var rows = versions.EnumerateArray().ToList();
        rows[0].GetProperty("schemaVersionId").GetGuid().ShouldBe(schemaV2);
        rows.Last().GetProperty("schemaVersionId").GetGuid().ShouldBe(arranged.SchemaV1);
        rows.Last().GetProperty("metadata").GetProperty("note").GetString().ShouldBe("v1");
    }

    [Fact]
    public async Task A_metadata_edit_is_a_new_revision_on_the_same_file()
    {
        var arranged = await ArrangeAsync("revisions");
        var filed = await FileAsync(arranged, new { kind = "contract", amount = 8_000_000_000m });
        var created = await filed.Content.ReadFromJsonAsync<JsonElement>();
        var documentId = created.GetProperty("documentId").GetGuid();
        var v1 = created.GetProperty("versionId").GetGuid();

        var edit = await arranged.User.PutAsJsonAsync($"/api/v1/documents/{documentId}/metadata", new
        {
            metadata = new { kind = "contract", amount = 9_000_000_000m },
            changeDescription = "اصلاح مبلغ",
            baseVersionId = v1,
        });

        edit.StatusCode.ShouldBe(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync());
        var result = await edit.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("outcome").GetString().ShouldBe("revision");
        result.GetProperty("label").GetString().ShouldBe("V1.2");

        var versions = (await arranged.User.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}/versions"))
            .EnumerateArray().ToList();
        versions.Count.ShouldBe(2);
        versions[0].GetProperty("storageObjectId").GetGuid().ShouldBe(versions[1].GetProperty("storageObjectId").GetGuid());
        versions[0].GetProperty("sha256").GetString().ShouldBe(versions[1].GetProperty("sha256").GetString());
        versions[0].GetProperty("changeKind").GetString().ShouldBe("Metadata");
        versions[1].GetProperty("metadata").GetProperty("amount").GetString().ShouldBe("8000000000");

        // The same edit again changes nothing and creates nothing.
        var again = await arranged.User.PutAsJsonAsync($"/api/v1/documents/{documentId}/metadata", new
        {
            metadata = new { kind = "contract", amount = 9_000_000_000m },
        });
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outcome").GetString().ShouldBe("unchanged");

        // And an edit based on V1.1 is now stale.
        var stale = await arranged.User.PutAsJsonAsync($"/api/v1/documents/{documentId}/metadata", new
        {
            metadata = new { kind = "contract", amount = 1m },
            baseVersionId = v1,
        });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task In_place_edits_are_audited_and_approval_relevant_fields_still_get_a_revision()
    {
        var arranged = await ArrangeAsync("in.place", policy: "InPlace");
        var filed = await FileAsync(arranged, new { kind = "contract", amount = 100m, vendor = "الف" });
        var documentId = (await filed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();

        // Vendor is not approval-relevant: rewritten in place, same row, with a diff in the audit log.
        var vendor = await arranged.User.PutAsJsonAsync($"/api/v1/documents/{documentId}/metadata", new
        {
            metadata = new { kind = "contract", amount = 100m, vendor = "ب" },
        });
        var vendorResult = await vendor.Content.ReadFromJsonAsync<JsonElement>();
        vendorResult.GetProperty("outcome").GetString().ShouldBe("in_place", vendorResult.GetRawText());
        vendorResult.GetProperty("label").GetString().ShouldBe("V1.1");
        await factory.ShouldHaveAuditAsync("METADATA_UPDATED_IN_PLACE");

        // Amount is approval-relevant: always a new revision, never an edit under an approval.
        var amount = await arranged.User.PutAsJsonAsync($"/api/v1/documents/{documentId}/metadata", new
        {
            metadata = new { kind = "contract", amount = 200m, vendor = "ب" },
        });
        var amountResult = await amount.Content.ReadFromJsonAsync<JsonElement>();
        amountResult.GetProperty("outcome").GetString().ShouldBe("revision");
        amountResult.GetProperty("label").GetString().ShouldBe("V1.2");
    }

    [Fact]
    public async Task The_database_refuses_to_change_a_published_schema()
    {
        var arranged = await ArrangeAsync("frozen");

        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE doctypes.field_definitions SET is_required = true WHERE type_version_id = @v";
        command.Parameters.AddWithValue("v", arranged.SchemaV1);

        var failure = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        failure.MessageText.ShouldContain("immutable");

        await using var back = connection.CreateCommand();
        back.CommandText = "UPDATE doctypes.document_type_versions SET status = 'Draft' WHERE id = @v";
        back.Parameters.AddWithValue("v", arranged.SchemaV1);
        await Should.ThrowAsync<PostgresException>(() => back.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task A_published_field_cannot_change_its_type()
    {
        var arranged = await ArrangeAsync("retype");

        var schema = JsonSerializer.SerializeToNode(ContractSchema())!;
        schema["fields"]![1]!["type"] = "Text";
        schema["fields"]![1]!["validation"] = null;

        var saved = await arranged.Admin.PutAsJsonAsync($"/api/v1/admin/document-types/{arranged.TypeId}/draft", schema);

        (await ErrorsOf(saved)).TryGetProperty("fields[1].type", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Only_type_administrators_edit_schemas_but_everyone_can_read_them()
    {
        var arranged = await ArrangeAsync("schema.reader");

        (await arranged.User.PutAsJsonAsync($"/api/v1/admin/document-types/{arranged.TypeId}/draft", ContractSchema()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var schema = await arranged.User.GetFromJsonAsync<JsonElement>($"/api/v1/document-types/{arranged.TypeId}/schema");
        schema.GetProperty("versionId").GetGuid().ShouldBe(arranged.SchemaV1);
        schema.GetProperty("rules").GetArrayLength().ShouldBe(2);
    }
}
