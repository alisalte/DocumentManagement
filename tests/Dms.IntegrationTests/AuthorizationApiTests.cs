using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>
/// End-to-end checks that the server, not the client, decides. Also covers the specification's
/// "unauthorized access attempts" requirement: a refusal must leave an audit trail.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AuthorizationApiTests(DmsApiFactory factory)
{
    private async Task<(HttpClient Client, Guid UserId)> CreateOrdinaryUserAsync(string username)
    {
        var admin = factory.CreateClient();
        var adminTokens = await admin.LoginAsync(DmsApiFactory.AdminUsername, DmsApiFactory.AdminPassword);
        admin.WithToken(adminTokens.AccessToken);

        const string password = "OrdinaryUserPassword!1";
        var created = await admin.PostAsJsonAsync("/api/v1/admin/users", new
        {
            username,
            displayName = username,
            email = (string?)null,
            password,
            isSystemAdmin = false,
            mustChangePassword = false,
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var userId = body.GetProperty("id").GetGuid();

        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(username, password);
        return (client.WithToken(tokens.AccessToken), userId);
    }

    [Fact]
    public async Task An_administrator_can_list_users()
    {
        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(DmsApiFactory.AdminUsername, DmsApiFactory.AdminPassword);

        var response = await client.WithToken(tokens.AccessToken).GetAsync("/api/v1/admin/users");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_ordinary_user_cannot_reach_administration_and_the_refusal_is_audited()
    {
        var (client, _) = await CreateOrdinaryUserAsync("ordinary.user");

        var response = await client.GetAsync("/api/v1/admin/users");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await factory.ShouldHaveAuditAsync("ACCESS_DENIED");
    }

    [Fact]
    public async Task An_ordinary_user_cannot_read_the_audit_log()
    {
        var (client, _) = await CreateOrdinaryUserAsync("no.audit.user");

        var response = await client.GetAsync("/api/v1/audit");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_administrator_can_read_the_audit_log()
    {
        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(DmsApiFactory.AdminUsername, DmsApiFactory.AdminPassword);

        var response = await client.WithToken(tokens.AccessToken).GetAsync("/api/v1/audit?take=5");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entries = await response.Content.ReadFromJsonAsync<JsonElement>();
        entries.GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_deactivated_user_loses_access_immediately()
    {
        var (client, userId) = await CreateOrdinaryUserAsync("soon.disabled");

        // The user's own token still looks valid, but the principal is no longer active.
        (await client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var admin = factory.CreateClient();
        var adminTokens = await admin.LoginAsync(DmsApiFactory.AdminUsername, DmsApiFactory.AdminPassword);
        var deactivated = await admin.WithToken(adminTokens.AccessToken)
            .PostAsJsonAsync($"/api/v1/admin/users/{userId}/active", new { isActive = false });
        deactivated.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var permissions = await client.GetAsync("/api/v1/permissions/mine");
        var body = await permissions.Content.ReadFromJsonAsync<JsonElement>();
        body.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Granting_and_revoking_a_resource_permission_is_audited()
    {
        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(DmsApiFactory.AdminUsername, DmsApiFactory.AdminPassword);
        client.WithToken(tokens.AccessToken);

        var (_, userId) = await CreateOrdinaryUserAsync("grantee.user");
        var documentId = Guid.CreateVersion7();

        var granted = await client.PostAsJsonAsync(
            $"/api/v1/resources/Document/{documentId}/permissions",
            new
            {
                subjectType = "User",
                subjectId = userId,
                permissionCode = "DOCUMENT_VIEW",
                effect = "Allow",
                inherit = false,
            });

        granted.StatusCode.ShouldBe(HttpStatusCode.Created, await granted.Content.ReadAsStringAsync());
        var entryId = (await granted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await factory.ShouldHaveAuditAsync("PERMISSION_GRANTED");

        var listed = await client.GetAsync($"/api/v1/resources/Document/{documentId}/permissions");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await listed.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength().ShouldBe(1);

        var revoked = await client.DeleteAsync($"/api/v1/permissions/{entryId}");
        revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.ShouldHaveAuditAsync("PERMISSION_REVOKED");
    }

    [Fact]
    public async Task The_explain_endpoint_reports_the_effective_permissions()
    {
        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(DmsApiFactory.AdminUsername, DmsApiFactory.AdminPassword);
        client.WithToken(tokens.AccessToken);

        var (_, userId) = await CreateOrdinaryUserAsync("explained.user");
        var documentId = Guid.CreateVersion7();

        await client.PostAsJsonAsync(
            $"/api/v1/resources/Document/{documentId}/permissions",
            new
            {
                subjectType = "User",
                subjectId = userId,
                permissionCode = "DOCUMENT_VIEW",
                effect = "Allow",
                inherit = false,
            });

        var explained = await client.GetAsync(
            $"/api/v1/permissions/explain?userId={userId}&resourceType=Document&resourceId={documentId}");

        explained.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = await explained.Content.ReadFromJsonAsync<JsonElement>();

        var view = rows.EnumerateArray().First(row => row.GetProperty("permissionCode").GetString() == "DOCUMENT_VIEW");
        view.GetProperty("allowed").GetBoolean().ShouldBeTrue();

        // View alone must not carry download with it.
        var download = rows.EnumerateArray()
            .First(row => row.GetProperty("permissionCode").GetString() == "DOCUMENT_DOWNLOAD");
        download.GetProperty("allowed").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_role_cannot_be_given_a_resource_permission_over_the_api()
    {
        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(DmsApiFactory.AdminUsername, DmsApiFactory.AdminPassword);
        client.WithToken(tokens.AccessToken);

        var created = await client.PostAsJsonAsync(
            "/api/v1/admin/roles",
            new { code = "TEST_ROLE", name = "Test role", description = (string?)null });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var roleId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/admin/roles/{roleId}/permissions",
            new { permissionCodes = new[] { "DOCUMENT_VIEW" } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
