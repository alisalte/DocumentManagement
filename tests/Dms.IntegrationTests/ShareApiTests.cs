using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>
/// Phase 6, sharing (decision D8): internal shares and external links, each pinned to one
/// version, re-checked against the sharer's current rights on every use, and always beaten by an
/// explicit DENY. The exit criteria: expiry, revocation, the opening limit under concurrency,
/// version pinning and share versus DENY.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ShareApiTests(DmsApiFactory factory)
{
    private sealed record Arranged(
        HttpClient Admin,
        HttpClient Owner,
        Guid OwnerId,
        Guid CategoryId,
        Guid DocumentId,
        Guid VersionId,
        byte[] Content,
        Dictionary<string, Guid> OwnerGrants);

    /// <summary>An owner who may view, download, print and share everything in a fresh category, and one document.</summary>
    private async Task<Arranged> ArrangeAsync(string who, byte[]? content = null)
    {
        var admin = await factory.AdminAsync();
        var (owner, ownerId) = await factory.CreateUserAsync(who);
        var categoryId = await admin.CreateCategoryAsync();

        var grants = new Dictionary<string, Guid>();
        foreach (var permission in new[]
        {
            "DOCUMENT_VIEW", "DOCUMENT_CREATE", "DOCUMENT_CREATE_VERSION", "DOCUMENT_DOWNLOAD",
            "DOCUMENT_PRINT", "DOCUMENT_SHARE", "DOCUMENT_SHARE_EXTERNAL",
        })
        {
            grants[permission] = await admin.GrantAsync("Category", categoryId, ownerId, permission, inherit: true);
        }

        content ??= DocumentTestKit.Pdf($"shared {Guid.NewGuid()}");
        var (documentId, versionId) = await owner.CreateDocumentAsync(categoryId, "سند اشتراکی", content);
        return new Arranged(admin, owner, ownerId, categoryId, documentId, versionId, content, grants);
    }

    private static async Task<HttpResponseMessage> ShareRawAsync(
        HttpClient sharer,
        Guid documentId,
        Guid versionId,
        Guid recipientId,
        string[] permissions,
        DateTimeOffset? expiresAt = null) =>
        await sharer.PostAsJsonAsync($"/api/v1/documents/{documentId}/shares", new
        {
            versionId,
            recipientId,
            permissions,
            expiresAt,
            message = "برای بررسی",
        });

    private static async Task<Guid> ShareAsync(HttpClient sharer, Guid documentId, Guid versionId, Guid recipientId, params string[] permissions)
    {
        var response = await ShareRawAsync(sharer, documentId, versionId, recipientId, permissions);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> CreateLinkRawAsync(
        HttpClient creator,
        Guid documentId,
        Guid versionId,
        string[] permissions,
        int? maxAccessCount = null,
        string? password = null,
        DateTimeOffset? expiresAt = null) =>
        await creator.PostAsJsonAsync($"/api/v1/documents/{documentId}/links", new
        {
            versionId,
            permissions,
            expiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(7),
            maxAccessCount,
            password,
            label = "برای پیمانکار",
        });

    private static async Task<(Guid Id, string Token)> CreateLinkAsync(
        HttpClient creator,
        Guid documentId,
        Guid versionId,
        string[] permissions,
        int? maxAccessCount = null,
        string? password = null)
    {
        var response = await CreateLinkRawAsync(creator, documentId, versionId, permissions, maxAccessCount, password);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), body.GetProperty("token").GetString()!);
    }

    private static Task<HttpResponseMessage> OpenLinkAsync(HttpClient anonymous, string token, string? password = null) =>
        anonymous.PostAsJsonAsync($"/api/v1/public/links/{token}/open", new { password });

    private static HttpRequestMessage WithSession(HttpMethod method, string url, string session)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Share-Session", session);
        return request;
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task A_share_opens_one_version_and_nothing_else()
    {
        var arranged = await ArrangeAsync("share.owner");
        var (recipient, recipientId) = await factory.CreateUserAsync("share.recipient");
        var (documentId, v1) = (arranged.DocumentId, arranged.VersionId);

        // Before the share, the document does not exist for the recipient.
        (await recipient.GetAsync($"/api/v1/documents/{documentId}/versions/{v1}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await ShareAsync(arranged.Owner, documentId, v1, recipientId, "View", "Download");

        var version = await recipient.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}/versions/{v1}");
        version.GetProperty("title").GetString().ShouldBe("سند اشتراکی");
        version.GetProperty("version").GetProperty("id").GetGuid().ShouldBe(v1);
        version.GetProperty("allowedActions").EnumerateArray().Select(item => item.GetString())
            .ShouldBe(["DOCUMENT_VIEW", "DOCUMENT_DOWNLOAD"], ignoreOrder: true);
        (await recipient.GetByteArrayAsync($"/api/v1/documents/{documentId}/versions/{v1}/content")).ShouldBe(arranged.Content);

        // The document as a whole, its history and the plain "effective version" routes stay closed.
        (await recipient.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await recipient.GetAsync($"/api/v1/documents/{documentId}/versions")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await recipient.GetAsync($"/api/v1/documents/{documentId}/content")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Version pinning: a newer version is not part of the share.
        var added = await arranged.Owner.AddVersionRawAsync(documentId, DocumentTestKit.Pdf("v2"));
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync());
        var v2 = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("versionId").GetGuid();

        (await recipient.GetAsync($"/api/v1/documents/{documentId}/versions/{v2}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await recipient.GetAsync($"/api/v1/documents/{documentId}/versions/{v2}/content")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await recipient.GetByteArrayAsync($"/api/v1/documents/{documentId}/versions/{v1}/content")).ShouldBe(arranged.Content);

        var received = await recipient.GetFromJsonAsync<JsonElement>("/api/v1/shares/received");
        var item = received.EnumerateArray().Single(entry => entry.GetProperty("documentId").GetGuid() == documentId);
        item.GetProperty("versionId").GetGuid().ShouldBe(v1);
        item.GetProperty("versionLabel").GetString().ShouldBe("V1.1");

        await factory.ShouldHaveAuditAsync("DOCUMENT_SHARED");
    }

    [Fact]
    public async Task A_share_gives_only_what_it_names_and_never_more_than_the_sharer_has()
    {
        var arranged = await ArrangeAsync("share.limits");
        var (recipient, recipientId) = await factory.CreateUserAsync("share.limited");

        await ShareAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, recipientId, "View");
        (await recipient.GetAsync($"/api/v1/documents/{arranged.DocumentId}/versions/{arranged.VersionId}/content"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A sharer who may view and share, but not download, cannot hand out a download.
        var (colleague, colleagueId) = await factory.CreateUserAsync("share.viewer");
        await arranged.Admin.GrantManyAsync("Category", arranged.CategoryId, colleagueId, "DOCUMENT_VIEW", "DOCUMENT_SHARE");
        var exceeding = await ShareRawAsync(colleague, arranged.DocumentId, arranged.VersionId, recipientId, ["View", "Download"]);
        exceeding.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await exceeding.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("share.exceeds_rights");

        // Nor can a recipient pass the share along: their rights come from the share, not their own.
        var (third, thirdId) = await factory.CreateUserAsync("share.third");
        var passedOn = await ShareRawAsync(recipient, arranged.DocumentId, arranged.VersionId, thirdId, ["View"]);
        passedOn.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await third.GetAsync($"/api/v1/documents/{arranged.DocumentId}/versions/{arranged.VersionId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Sharing with yourself says so.
        (await ShareRawAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, arranged.OwnerId, ["View"]))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_explicit_deny_beats_a_share()
    {
        var arranged = await ArrangeAsync("share.deny.owner");
        var (recipient, recipientId) = await factory.CreateUserAsync("share.denied");
        await ShareAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, recipientId, "View", "Download");
        (await recipient.GetAsync($"/api/v1/documents/{arranged.DocumentId}/versions/{arranged.VersionId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await arranged.Admin.GrantAsync("Category", arranged.CategoryId, recipientId, "DOCUMENT_VIEW", effect: "Deny", inherit: true);

        (await recipient.GetAsync($"/api/v1/documents/{arranged.DocumentId}/versions/{arranged.VersionId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await recipient.GetAsync($"/api/v1/documents/{arranged.DocumentId}/versions/{arranged.VersionId}/content")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await recipient.GetFromJsonAsync<JsonElement>("/api/v1/shares/received")).EnumerateArray()
            .ShouldNotContain(entry => entry.GetProperty("documentId").GetGuid() == arranged.DocumentId);
    }

    [Fact]
    public async Task A_share_stops_working_when_it_is_revoked_expires_or_the_sharer_loses_the_right()
    {
        var arranged = await ArrangeAsync("share.life.owner");
        var url = $"/api/v1/documents/{arranged.DocumentId}/versions/{arranged.VersionId}";

        // Revocation.
        var (first, firstId) = await factory.CreateUserAsync("share.revoked");
        var shareId = await ShareAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, firstId, "View");
        (await first.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await arranged.Owner.DeleteAsync($"/api/v1/shares/{shareId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await first.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await factory.ShouldHaveAuditAsync("SHARE_REVOKED");

        // Expiry. Time is not faked, so the row is moved into the past.
        var (second, secondId) = await factory.CreateUserAsync("share.expired");
        var expiringId = await ShareAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, secondId, "View");
        (await second.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ExecuteAsync("UPDATE sharing.document_shares SET expires_at = now() - interval '1 minute' WHERE id = @id", ("id", expiringId));
        (await second.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Sharing again after expiry replaces the old share rather than colliding with it.
        await ShareAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, secondId, "View");
        (await second.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Decision D8: the sharer's current rights. Taking DOCUMENT_SHARE away ends every share they made.
        (await arranged.Admin.DeleteAsync($"/api/v1/permissions/{arranged.OwnerGrants["DOCUMENT_SHARE"]}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await second.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_the_sharer_the_recipient_or_a_permission_manager_can_see_and_revoke_shares()
    {
        var arranged = await ArrangeAsync("share.manage.owner");
        var (recipient, recipientId) = await factory.CreateUserAsync("share.manage.recipient");
        var shareId = await ShareAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, recipientId, "View");

        var (reader, readerId) = await factory.CreateUserAsync("share.manage.reader");
        await arranged.Admin.GrantAsync("Category", arranged.CategoryId, readerId, "DOCUMENT_VIEW", inherit: true);

        var ownView = await arranged.Owner.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{arranged.DocumentId}/shares");
        ownView.GetProperty("shares").GetArrayLength().ShouldBe(1);
        var readerView = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{arranged.DocumentId}/shares");
        readerView.GetProperty("shares").GetArrayLength().ShouldBe(0);

        (await reader.DeleteAsync($"/api/v1/shares/{shareId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The recipient may decline it.
        (await recipient.DeleteAsync($"/api/v1/shares/{shareId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await recipient.GetAsync($"/api/v1/documents/{arranged.DocumentId}/versions/{arranged.VersionId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_link_serves_its_version_to_anyone_holding_it_and_never_shows_the_token_again()
    {
        var arranged = await ArrangeAsync("link.owner", ProcessingTestKit.RealPdf("Shared page"));
        await factory.Services.RunJobsAsync();
        var (linkId, token) = await CreateLinkAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, ["View", "Download"]);

        var anonymous = factory.CreateClient();
        var info = await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/public/links/{token}");
        info.GetProperty("requiresPassword").GetBoolean().ShouldBeFalse();

        var opened = await OpenLinkAsync(anonymous, token);
        opened.StatusCode.ShouldBe(HttpStatusCode.OK, await opened.Content.ReadAsStringAsync());
        var body = await opened.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("title").GetString().ShouldBe("سند اشتراکی");
        body.GetProperty("canDownload").GetBoolean().ShouldBeTrue();
        body.GetProperty("canPrint").GetBoolean().ShouldBeFalse();
        body.GetProperty("pageCount").GetInt32().ShouldBe(1);
        var session = body.GetProperty("sessionToken").GetString()!;

        var page = await anonymous.SendAsync(WithSession(HttpMethod.Get, $"/api/v1/public/links/{token}/pages/1", session));
        page.StatusCode.ShouldBe(HttpStatusCode.OK, await page.Content.ReadAsStringAsync());
        page.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        page.Headers.CacheControl!.NoStore.ShouldBeTrue();

        var file = await anonymous.SendAsync(WithSession(HttpMethod.Get, $"/api/v1/public/links/{token}/content", session));
        file.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await file.Content.ReadAsByteArrayAsync()).ShouldBe(arranged.Content);

        // Print is not part of this link; no session, no content.
        (await anonymous.SendAsync(WithSession(HttpMethod.Get, $"/api/v1/public/links/{token}/print/1", session)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await anonymous.GetAsync($"/api/v1/public/links/{token}/content")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Only a digest is stored, and the creator's list shows the prefix, never the token.
        var listed = await arranged.Owner.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{arranged.DocumentId}/shares");
        var link = listed.GetProperty("links").EnumerateArray().Single(entry => entry.GetProperty("id").GetGuid() == linkId);
        link.GetProperty("tokenPrefix").GetString().ShouldBe(token[..8]);
        link.GetProperty("accessCount").GetInt32().ShouldBe(1);
        listed.GetRawText().ShouldNotContain(token);
        (await ScalarAsync("SELECT count(*) FROM sharing.share_links WHERE token_prefix = @prefix AND length(token_hash) = 32", ("prefix", token[..8]))).ShouldBe(1);

        await factory.ShouldHaveAuditAsync("SHARE_LINK_ACCESSED");
        (await ScalarAsync(
            "SELECT count(*) FROM audit.audit_logs WHERE action = 'DOCUMENT_DOWNLOADED' AND actor_type = 'SHARELINK' AND share_link_id = @id",
            ("id", linkId))).ShouldBe(1);

        // Revoking cuts off even a session that is already open.
        (await arranged.Owner.DeleteAsync($"/api/v1/share-links/{linkId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await anonymous.SendAsync(WithSession(HttpMethod.Get, $"/api/v1/public/links/{token}/pages/1", session)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await OpenLinkAsync(anonymous, token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"/api/v1/public/links/{token}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_link_opens_exactly_as_often_as_allowed_even_under_concurrency()
    {
        var arranged = await ArrangeAsync("link.count.owner");
        var (linkId, token) = await CreateLinkAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, ["View"], maxAccessCount: 3);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => OpenLinkAsync(factory.CreateClient(), token)));

        attempts.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(3);
        attempts.Count(response => response.StatusCode == HttpStatusCode.NotFound).ShouldBe(9);
        (await ScalarAsync("SELECT access_count FROM sharing.share_links WHERE id = @id", ("id", linkId))).ShouldBe(3);

        var listed = await arranged.Owner.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{arranged.DocumentId}/shares");
        listed.GetProperty("links").EnumerateArray().Single().GetProperty("state").GetString().ShouldBe("UsedUp");
    }

    [Fact]
    public async Task An_expired_link_opens_for_no_one()
    {
        var arranged = await ArrangeAsync("link.expiry.owner");
        var (linkId, token) = await CreateLinkAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, ["View"]);
        var anonymous = factory.CreateClient();

        var opened = await (await OpenLinkAsync(anonymous, token)).Content.ReadFromJsonAsync<JsonElement>();
        var session = opened.GetProperty("sessionToken").GetString()!;

        await ExecuteAsync(
            "UPDATE sharing.share_links SET created_at = now() - interval '2 days', expires_at = now() - interval '1 minute' WHERE id = @id",
            ("id", linkId));

        (await anonymous.GetAsync($"/api/v1/public/links/{token}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await OpenLinkAsync(anonymous, token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.SendAsync(WithSession(HttpMethod.Post, $"/api/v1/public/links/{token}/print", session)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Creating one that is already expired, or that would outlive the limit, is refused.
        (await CreateLinkRawAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, ["View"], expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CreateLinkRawAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, ["View"], expiresAt: DateTimeOffset.UtcNow.AddDays(400)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_link_password_is_checked_and_guessing_locks_the_link()
    {
        var arranged = await ArrangeAsync("link.password.owner");
        var (_, token) = await CreateLinkAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, ["View"], password: "کلید-رمز-۱۲۳");
        var anonymous = factory.CreateClient();

        (await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/public/links/{token}")).GetProperty("requiresPassword").GetBoolean().ShouldBeTrue();

        var missing = await OpenLinkAsync(anonymous, token);
        missing.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("share_link.password_required");

        (await OpenLinkAsync(anonymous, token, "کلید-رمز-۱۲۳")).StatusCode.ShouldBe(HttpStatusCode.OK);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            (await OpenLinkAsync(anonymous, token, $"wrong-{attempt}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await OpenLinkAsync(anonymous, token, "wrong-5")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // Locked: even the right password waits.
        (await OpenLinkAsync(anonymous, token, "کلید-رمز-۱۲۳")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        await factory.ShouldHaveAuditAsync("SHARE_LINK_PASSWORD_FAILED");
    }

    [Fact]
    public async Task A_link_dies_with_its_creators_rights_and_bows_to_a_deny_on_them()
    {
        var arranged = await ArrangeAsync("link.backing.owner");
        var (_, token) = await CreateLinkAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, ["View", "Download"]);
        var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/public/links/{token}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Losing DOWNLOAD narrows the link rather than ending it. (The owner also holds DOWNLOAD
        // through the document's owner defaults, so a DENY is what takes it away.)
        await arranged.Admin.GrantAsync("Document", arranged.DocumentId, arranged.OwnerId, "DOCUMENT_DOWNLOAD", effect: "Deny");
        var opened = await (await OpenLinkAsync(anonymous, token)).Content.ReadFromJsonAsync<JsonElement>();
        opened.GetProperty("canDownload").GetBoolean().ShouldBeFalse();
        (await anonymous.SendAsync(WithSession(HttpMethod.Get, $"/api/v1/public/links/{token}/content", opened.GetProperty("sessionToken").GetString()!)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A DENY on the creator's view ends it.
        await arranged.Admin.GrantAsync("Document", arranged.DocumentId, arranged.OwnerId, "DOCUMENT_VIEW", effect: "Deny");
        (await anonymous.GetAsync($"/api/v1/public/links/{token}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await OpenLinkAsync(anonymous, token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task External_links_need_their_own_permission()
    {
        var arranged = await ArrangeAsync("link.permission.owner");
        var (colleague, colleagueId) = await factory.CreateUserAsync("link.internal.only");
        await arranged.Admin.GrantManyAsync("Category", arranged.CategoryId, colleagueId, "DOCUMENT_VIEW", "DOCUMENT_SHARE");

        (await CreateLinkRawAsync(colleague, arranged.DocumentId, arranged.VersionId, ["View"])).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var hints = await colleague.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{arranged.DocumentId}/shares");
        hints.GetProperty("canShare").GetBoolean().ShouldBeTrue();
        hints.GetProperty("canShareExternal").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Unknown_tokens_and_sessions_reveal_nothing()
    {
        var anonymous = factory.CreateClient();
        var token = new string('A', 43);

        (await anonymous.GetAsync($"/api/v1/public/links/{token}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await OpenLinkAsync(anonymous, token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var arranged = await ArrangeAsync("link.session.owner");
        var (_, real) = await CreateLinkAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, ["View", "Download"]);
        (await anonymous.SendAsync(WithSession(HttpMethod.Get, $"/api/v1/public/links/{real}/content", "not-a-session")))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sharing_the_same_version_with_the_same_person_concurrently_leaves_one_live_share()
    {
        var arranged = await ArrangeAsync("share.race.owner");
        var (_, recipientId) = await factory.CreateUserAsync("share.race.recipient");

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            ShareRawAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, recipientId, ["View"])));

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.Created);
        (await ScalarAsync(
            "SELECT count(*) FROM sharing.document_shares WHERE version_id = @version AND shared_with_user_id = @recipient AND revoked_at IS NULL",
            ("version", arranged.VersionId),
            ("recipient", recipientId))).ShouldBe(1);
    }

    [Fact]
    public async Task A_sharer_still_sees_their_own_share_among_many_newer_ones()
    {
        var arranged = await ArrangeAsync("share.crowd.owner");
        var (colleague, colleagueId) = await factory.CreateUserAsync("share.crowd.colleague");
        await arranged.Admin.GrantManyAsync("Category", arranged.CategoryId, colleagueId, "DOCUMENT_VIEW", "DOCUMENT_SHARE");
        var (_, recipientId) = await factory.CreateUserAsync("share.crowd.recipient");
        var ownId = await ShareAsync(colleague, arranged.DocumentId, arranged.VersionId, recipientId, "View");

        // More newer (revoked) shares by the owner than the list returns.
        await ExecuteAsync(
            """
            INSERT INTO sharing.document_shares
                (id, document_id, version_id, shared_by, shared_with_user_id, permissions, created_at, revoked_at, revoked_by)
            SELECT gen_random_uuid(), @document, @version, @owner, @recipient, 1,
                   now() + make_interval(secs => n), now() + make_interval(secs => n), @owner
              FROM generate_series(1, 210) AS n
            """,
            ("document", arranged.DocumentId),
            ("version", arranged.VersionId),
            ("owner", arranged.OwnerId),
            ("recipient", recipientId));

        var listed = await colleague.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{arranged.DocumentId}/shares");
        listed.GetProperty("shares").EnumerateArray().Select(share => share.GetProperty("id").GetGuid()).ShouldBe([ownId]);
    }

    [Fact]
    public async Task Link_print_pages_need_the_audited_print_start()
    {
        var arranged = await ArrangeAsync("link.print.owner", ProcessingTestKit.RealPdf("Printed page"));
        await factory.Services.RunJobsAsync();
        var (linkId, token) = await CreateLinkAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, ["View", "Print"]);
        var anonymous = factory.CreateClient();
        var session = (await (await OpenLinkAsync(anonymous, token)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sessionToken").GetString()!;
        var printPage = $"/api/v1/public/links/{token}/print/1";

        var early = await anonymous.SendAsync(WithSession(HttpMethod.Get, printPage, session));
        early.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await early.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("share_link.print_not_started");

        (await anonymous.SendAsync(WithSession(HttpMethod.Post, $"/api/v1/public/links/{token}/print", session)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await anonymous.SendAsync(WithSession(HttpMethod.Get, printPage, session))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ScalarAsync(
            "SELECT count(*) FROM audit.audit_logs WHERE action = 'DOCUMENT_PRINTED' AND actor_type = 'SHARELINK' AND share_link_id = @id",
            ("id", linkId))).ShouldBe(1);

        // A new session starts over.
        var second = (await (await OpenLinkAsync(anonymous, token)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sessionToken").GetString()!;
        (await anonymous.SendAsync(WithSession(HttpMethod.Get, printPage, second))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_administrator_without_view_can_find_and_revoke_a_link()
    {
        var arranged = await ArrangeAsync("link.admin.owner");
        var (linkId, token) = await CreateLinkAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, ["View"]);

        // Decision D5: no content for the administrator, but the permission-management bypass.
        (await arranged.Admin.GetAsync($"/api/v1/documents/{arranged.DocumentId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var listed = await arranged.Admin.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{arranged.DocumentId}/shares");
        listed.GetProperty("canManageAll").GetBoolean().ShouldBeTrue();
        listed.GetProperty("links").EnumerateArray().Select(link => link.GetProperty("id").GetGuid()).ShouldBe([linkId]);
        await factory.ShouldHaveAuditAsync("ADMIN_PERMISSION_OVERRIDE");

        (await arranged.Admin.DeleteAsync($"/api/v1/share-links/{linkId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.CreateClient().GetAsync($"/api/v1/public/links/{token}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Anyone else without view still gets nothing.
        var (stranger, _) = await factory.CreateUserAsync("link.admin.stranger");
        (await stranger.GetAsync($"/api/v1/documents/{arranged.DocumentId}/shares")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_recipient_is_notified_and_only_they_can_read_it()
    {
        var arranged = await ArrangeAsync("share.notify.owner");
        var (recipient, recipientId) = await factory.CreateUserAsync("share.notify.recipient");
        var (other, _) = await factory.CreateUserAsync("share.notify.other");
        await ShareAsync(arranged.Owner, arranged.DocumentId, arranged.VersionId, recipientId, "View", "Download");

        (await recipient.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count")).GetProperty("count").GetInt32().ShouldBe(1);
        var page = await recipient.GetFromJsonAsync<JsonElement>("/api/v1/notifications?unreadOnly=true");
        var item = page.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        item.GetProperty("type").GetString().ShouldBe("DOCUMENT_SHARED");
        item.GetProperty("payload").GetProperty("versionLabel").GetString().ShouldBe("V1.1");
        item.GetProperty("payload").GetProperty("permissions").EnumerateArray().Select(value => value.GetString()).ShouldBe(["View", "Download"]);
        var id = item.GetProperty("id").GetGuid();

        // Someone else's notification: nothing to see, and marking it changes nothing.
        (await other.GetFromJsonAsync<JsonElement>("/api/v1/notifications")).GetProperty("items").GetArrayLength().ShouldBe(0);
        var foreign = await other.PostAsJsonAsync("/api/v1/notifications/read", new { ids = new[] { id } });
        (await foreign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("count").GetInt32().ShouldBe(0);
        (await recipient.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count")).GetProperty("count").GetInt32().ShouldBe(1);

        var read = await recipient.PostAsJsonAsync("/api/v1/notifications/read", new { ids = new[] { id } });
        (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("count").GetInt32().ShouldBe(1);
        (await recipient.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count")).GetProperty("count").GetInt32().ShouldBe(0);
        (await recipient.GetFromJsonAsync<JsonElement>("/api/v1/notifications")).GetProperty("items")[0].GetProperty("readAt").ValueKind
            .ShouldBe(JsonValueKind.String);

        // The sharer caused it and is not told.
        (await arranged.Owner.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count")).GetProperty("count").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Notification_pages_do_not_lose_rows_that_share_a_timestamp()
    {
        var (reader, readerId) = await factory.CreateUserAsync("notify.pages");
        await ExecuteAsync(
            """
            INSERT INTO notify.notifications (id, user_id, type, payload, created_at)
            SELECT gen_random_uuid(), @user, 'DOCUMENT_SHARED', '{}', '2026-01-01T00:00:00Z'
              FROM generate_series(1, 5)
            """,
            ("user", readerId));

        var seen = new HashSet<Guid>();
        string? cursor = null;
        do
        {
            var page = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/notifications?take=2{(cursor is null ? "" : $"&cursor={cursor}")}");
            foreach (var item in page.GetProperty("items").EnumerateArray())
            {
                seen.Add(item.GetProperty("id").GetGuid()).ShouldBeTrue("a row came twice");
            }

            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        seen.Count.ShouldBe(5);
        (await reader.GetAsync("/api/v1/notifications?cursor=nonsense")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
