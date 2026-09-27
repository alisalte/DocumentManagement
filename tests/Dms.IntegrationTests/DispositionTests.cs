using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>Phase 10.4: disposition review, Legal Hold re-check, destruction, Certificate of Destruction.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class DispositionTests(DmsApiFactory factory)
{
    [Fact]
    public async Task Eligible_Record_can_request_approve_destroy_and_emits_one_certificate()
    {
        var admin = await factory.AdminAsync();
        var (recordId, documentId, _, _) = await ArrangePendingDisposalAsync(admin, "disp.happy");

        var requested = await admin.PostAsJsonAsync(
            $"/api/v1/records/{recordId}/disposition",
            new { reason = "پایان نگهداری" });
        requested.StatusCode.ShouldBe(HttpStatusCode.Created, await requested.Content.ReadAsStringAsync());
        var dispositionId = (await requested.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Idempotent re-request returns the same active disposition.
        var again = await admin.PostAsJsonAsync(
            $"/api/v1/records/{recordId}/disposition",
            new { reason = "دوباره" });
        again.StatusCode.ShouldBe(HttpStatusCode.Created, await again.Content.ReadAsStringAsync());
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid().ShouldBe(dispositionId);

        (await admin.PostAsJsonAsync($"/api/v1/disposition/{dispositionId}/approve", new { reason = "تأیید" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Duplicate approve is safe.
        (await admin.PostAsJsonAsync($"/api/v1/disposition/{dispositionId}/approve", new { reason = "دوباره" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var destroyed = await admin.PostAsJsonAsync(
            $"/api/v1/disposition/{dispositionId}/destroy",
            new { reason = "امحا" });
        destroyed.StatusCode.ShouldBe(HttpStatusCode.OK, await destroyed.Content.ReadAsStringAsync());
        var certificateId = (await destroyed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("certificateId").GetGuid();

        // Idempotent destroy returns the same certificate.
        var destroyAgain = await admin.PostAsJsonAsync(
            $"/api/v1/disposition/{dispositionId}/destroy",
            new { reason = "امحا دوباره" });
        destroyAgain.StatusCode.ShouldBe(HttpStatusCode.OK, await destroyAgain.Content.ReadAsStringAsync());
        (await destroyAgain.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("certificateId").GetGuid().ShouldBe(certificateId);

        var certificate = await admin.GetAsync($"/api/v1/destruction-certificates/{certificateId}");
        certificate.StatusCode.ShouldBe(HttpStatusCode.OK, await certificate.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await certificate.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("recordId").GetGuid().ShouldBe(recordId);
        body.RootElement.GetProperty("documentId").GetGuid().ShouldBe(documentId);
        body.RootElement.GetProperty("dispositionId").GetGuid().ShouldBe(dispositionId);
        body.RootElement.GetProperty("certificateNumber").GetString().ShouldNotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("contentSha256").GetString()!.Length.ShouldBe(64);
        body.RootElement.GetProperty("certificateHash").GetString()!.Length.ShouldBe(64);

        var disposition = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/disposition/{dispositionId}");
        disposition.GetProperty("status").GetString().ShouldBe("Destroyed");

        var pending = await admin.GetFromJsonAsync<JsonElement>("/api/v1/disposition/pending");
        pending.EnumerateArray().Any(item =>
            item.GetProperty("recordId").GetGuid() == recordId
            && item.GetProperty("status").GetString() == "Destroyed").ShouldBeTrue();

        // Direct transition to Destroyed is refused.
        var bypass = await admin.PostAsJsonAsync(
            $"/api/v1/records/{recordId}/transition",
            new { status = "Destroyed", reason = "bypass" });
        bypass.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await bypass.Content.ReadAsStringAsync()).ShouldContain("disposition.required");

        // Purge of a Record document remains blocked by immutability (cannot bypass disposition).
        var purge = await admin.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/purge",
            new { reason = "نباید" });
        purge.StatusCode.ShouldBe(HttpStatusCode.Conflict, await purge.Content.ReadAsStringAsync());
        (await purge.Content.ReadAsStringAsync()).ShouldContain("record.immutable");

        await factory.ShouldHaveAuditAsync("DISPOSITION_REQUESTED");
        await factory.ShouldHaveAuditAsync("DISPOSITION_APPROVED");
        await factory.ShouldHaveAuditAsync("DESTRUCTION_ATTEMPTED");
        await factory.ShouldHaveAuditAsync("RECORD_DESTROYED");
        await factory.ShouldHaveAuditAsync("CERTIFICATE_CREATED");
    }

    [Fact]
    public async Task Non_expired_and_held_Records_cannot_enter_or_complete_disposition()
    {
        var admin = await factory.AdminAsync();
        var categoryId = await admin.CreateCategoryAsync();
        var (owner, ownerId) = await factory.CreateUserAsync("disp.block");
        await admin.GrantManyAsync(
            "Category", categoryId, ownerId,
            "DOCUMENT_VIEW", "DOCUMENT_CREATE", "RECORD_DECLARE");

        var classId = await CreateClassAsync(admin, "BLK");
        var (documentId, _) = await owner.CreateDocumentAsync(categoryId, title: "هنوز فعال");
        var declared = await owner.PostAsJsonAsync(
            $"/api/v1/records/declare/{documentId}",
            new { recordClassId = classId, recordSeriesId = (Guid?)null, finalVersionId = (Guid?)null, reason = "اعلام" });
        declared.StatusCode.ShouldBe(HttpStatusCode.Created, await declared.Content.ReadAsStringAsync());
        var recordId = (await declared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var tooEarly = await admin.PostAsJsonAsync(
            $"/api/v1/records/{recordId}/disposition",
            new { reason = "زود است" });
        tooEarly.StatusCode.ShouldBe(HttpStatusCode.Conflict, await tooEarly.Content.ReadAsStringAsync());
        (await tooEarly.Content.ReadAsStringAsync()).ShouldContain("disposition.not_eligible");

        var (pendingId, holdDocId, _, _) = await ArrangePendingDisposalAsync(admin, "disp.hold", withLegalHoldType: true);
        (await admin.PostAsJsonAsync("/api/v1/legal-holds", new { documentId = holdDocId, reason = "دادگاه" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var heldRequest = await admin.PostAsJsonAsync(
            $"/api/v1/records/{pendingId}/disposition",
            new { reason = "hold" });
        heldRequest.StatusCode.ShouldBe(HttpStatusCode.Conflict, await heldRequest.Content.ReadAsStringAsync());
        (await heldRequest.Content.ReadAsStringAsync()).ShouldContain("purge.legal_hold");
        await factory.ShouldHaveAuditAsync("DISPOSITION_BLOCKED_BY_LEGAL_HOLD");
    }

    [Fact]
    public async Task Legal_Hold_after_approval_blocks_destruction()
    {
        var admin = await factory.AdminAsync();
        var (recordId, documentId, _, owner) = await ArrangePendingDisposalAsync(admin, "disp.latehold", withLegalHoldType: true);

        var requested = await admin.PostAsJsonAsync(
            $"/api/v1/records/{recordId}/disposition",
            new { reason = "ready" });
        var dispositionId = (await requested.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PostAsJsonAsync($"/api/v1/disposition/{dispositionId}/approve", new { reason = "ok" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await admin.PostAsJsonAsync("/api/v1/legal-holds", new { documentId, reason = "بعد از تأیید" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var destroy = await admin.PostAsJsonAsync(
            $"/api/v1/disposition/{dispositionId}/destroy",
            new { reason = "نباید" });
        destroy.StatusCode.ShouldBe(HttpStatusCode.Conflict, await destroy.Content.ReadAsStringAsync());
        (await destroy.Content.ReadAsStringAsync()).ShouldContain("purge.legal_hold");

        var record = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/records/{recordId}");
        record.GetProperty("status").GetString().ShouldBe("PendingDisposal");

        var disposition = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/disposition/{dispositionId}");
        disposition.GetProperty("status").GetString().ShouldBe("Approved");

        await factory.ShouldHaveAuditAsync("DESTRUCTION_BLOCKED");
    }

    [Fact]
    public async Task Unauthorized_users_cannot_approve_or_destroy()
    {
        var admin = await factory.AdminAsync();
        var (recordId, _, _, _) = await ArrangePendingDisposalAsync(admin, "disp.authz");
        var requested = await admin.PostAsJsonAsync(
            $"/api/v1/records/{recordId}/disposition",
            new { reason = "req" });
        var dispositionId = (await requested.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var (viewer, _) = await factory.CreateUserAsync("disp.viewer");
        var approve = await viewer.PostAsJsonAsync(
            $"/api/v1/disposition/{dispositionId}/approve",
            new { reason = "no" });
        approve.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await approve.Content.ReadAsStringAsync());

        var (approver, _) = await DelegateAsync(admin, "disp.approver", "DISPOSITION_APPROVE");
        (await approver.PostAsJsonAsync($"/api/v1/disposition/{dispositionId}/approve", new { reason = "yes" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var destroyAsApprover = await approver.PostAsJsonAsync(
            $"/api/v1/disposition/{dispositionId}/destroy",
            new { reason = "no destroy right" });
        destroyAsApprover.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await destroyAsApprover.Content.ReadAsStringAsync());

        var (destroyer, _) = await DelegateAsync(admin, "disp.destroyer", "DISPOSITION_DESTROY");
        var destroyed = await destroyer.PostAsJsonAsync(
            $"/api/v1/disposition/{dispositionId}/destroy",
            new { reason = "امحا" });
        destroyed.StatusCode.ShouldBe(HttpStatusCode.OK, await destroyed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rejection_returns_Record_to_Expired_and_requires_reason()
    {
        var admin = await factory.AdminAsync();
        var (recordId, _, _, owner) = await ArrangePendingDisposalAsync(admin, "disp.reject");
        var requested = await admin.PostAsJsonAsync(
            $"/api/v1/records/{recordId}/disposition",
            new { reason = "req" });
        var dispositionId = (await requested.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var missingReason = await admin.PostAsJsonAsync(
            $"/api/v1/disposition/{dispositionId}/reject",
            new { reason = "" });
        missingReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await missingReason.Content.ReadAsStringAsync());

        (await admin.PostAsJsonAsync(
            $"/api/v1/disposition/{dispositionId}/reject",
            new { reason = "نیاز به نگهداری بیشتر" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var record = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/records/{recordId}");
        record.GetProperty("status").GetString().ShouldBe("Expired");

        // Duplicate reject is safe.
        (await admin.PostAsJsonAsync(
            $"/api/v1/disposition/{dispositionId}/reject",
            new { reason = "دوباره" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Cannot approve a rejected disposition.
        var approve = await admin.PostAsJsonAsync(
            $"/api/v1/disposition/{dispositionId}/approve",
            new { reason = "late" });
        approve.StatusCode.ShouldBe(HttpStatusCode.Conflict, await approve.Content.ReadAsStringAsync());
        (await approve.Content.ReadAsStringAsync()).ShouldContain("disposition.invalid_state");

        await factory.ShouldHaveAuditAsync("DISPOSITION_REJECTED");
    }

    [Fact]
    public async Task Unapproved_disposition_cannot_be_destroyed()
    {
        var admin = await factory.AdminAsync();
        var (recordId, _, _, _) = await ArrangePendingDisposalAsync(admin, "disp.unapproved");
        var requested = await admin.PostAsJsonAsync(
            $"/api/v1/records/{recordId}/disposition",
            new { reason = "req" });
        var dispositionId = (await requested.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var destroy = await admin.PostAsJsonAsync(
            $"/api/v1/disposition/{dispositionId}/destroy",
            new { reason = "زود" });
        destroy.StatusCode.ShouldBe(HttpStatusCode.Conflict, await destroy.Content.ReadAsStringAsync());
        (await destroy.Content.ReadAsStringAsync()).ShouldContain("disposition.not_approved");
        await factory.ShouldHaveAuditAsync("DESTRUCTION_BLOCKED");
    }

    private async Task<(Guid RecordId, Guid DocumentId, Guid ClassId, HttpClient Owner)> ArrangePendingDisposalAsync(
        HttpClient admin,
        string prefix,
        bool withLegalHoldType = false)
    {
        var categoryId = await admin.CreateCategoryAsync();
        var (owner, ownerId) = await factory.CreateUserAsync(prefix);
        await admin.GrantManyAsync(
            "Category", categoryId, ownerId,
            "DOCUMENT_VIEW", "DOCUMENT_CREATE", "RECORD_DECLARE");

        var classId = await CreateClassAsync(admin, "DSP");
        Guid documentId;
        if (withLegalHoldType)
        {
            var typeId = await CreateLegalHoldTypeAsync(admin);
            var upload = await owner.UploadAsync(DocumentTestKit.Pdf(prefix));
            var filed = await owner.PostAsJsonAsync("/api/v1/documents", new
            {
                title = $"رکورد {prefix}",
                description = (string?)null,
                categoryId,
                documentTypeId = typeId,
                uploadId = upload.GetProperty("uploadId").GetGuid(),
                tags = Array.Empty<string>(),
                changeDescription = "v1",
            });
            filed.StatusCode.ShouldBe(HttpStatusCode.Created, await filed.Content.ReadAsStringAsync());
            documentId = (await filed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();
        }
        else
        {
            (documentId, _) = await owner.CreateDocumentAsync(categoryId, title: $"رکورد {prefix}");
        }

        var declared = await owner.PostAsJsonAsync(
            $"/api/v1/records/declare/{documentId}",
            new { recordClassId = classId, recordSeriesId = (Guid?)null, finalVersionId = (Guid?)null, reason = "اعلام" });
        declared.StatusCode.ShouldBe(HttpStatusCode.Created, await declared.Content.ReadAsStringAsync());
        var recordId = (await declared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await admin.PostAsJsonAsync(
            $"/api/v1/records/{recordId}/transition",
            new { status = "Expired", reason = "test" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PostAsJsonAsync(
            $"/api/v1/records/{recordId}/transition",
            new { status = "PendingDisposal", reason = "test" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        return (recordId, documentId, classId, owner);
    }

    private async Task<(HttpClient Client, Guid UserId)> DelegateAsync(
        HttpClient admin,
        string prefix,
        params string[] permissions)
    {
        var (_, userId) = await factory.CreateUserAsync(prefix);
        var role = await admin.PostAsJsonAsync(
            "/api/v1/admin/roles",
            new { code = $"R{Guid.NewGuid():N}"[..16], name = prefix, description = (string?)null });
        role.StatusCode.ShouldBe(HttpStatusCode.Created, await role.Content.ReadAsStringAsync());
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PutAsJsonAsync($"/api/v1/admin/roles/{roleId}/permissions", new { permissionCodes = permissions }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PutAsync($"/api/v1/admin/roles/{roleId}/users/{userId}", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var client = factory.CreateClient();
        var username = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/users/{userId}"))
            .GetProperty("user").GetProperty("username").GetString()!;
        var tokens = await client.LoginAsync(username, DocumentTestKit.UserPassword);
        return (client.WithToken(tokens.AccessToken), userId);
    }

    private static async Task<Guid> CreateClassAsync(HttpClient admin, string code)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/records/classes",
            new { code = $"{code}{Guid.NewGuid():N}"[..12].ToUpperInvariant(), name = code, description = (string?)null });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateLegalHoldTypeAsync(HttpClient admin)
    {
        var code = $"DH{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/document-types",
            new { code, name = "hold type", description = (string?)null });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var typeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PutAsJsonAsync(
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
            })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PutAsJsonAsync(
            $"/api/v1/admin/document-types/{typeId}/draft",
            new { fields = Array.Empty<object>(), rules = Array.Empty<object>() }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PostAsync($"/api/v1/admin/document-types/{typeId}/publish", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        return typeId;
    }
}
