using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>
/// Phase 8 on the server: the administration API the admin screens use, and the phase 1 review
/// findings fixed with it: no privilege escalation through ADMIN_MANAGE_USERS or roles, the
/// forced password change enforced by the server, and every use of the administrator's ACL
/// bypass audited.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdministrationApiTests(DmsApiFactory factory)
{
    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        response.Content.Headers.ContentLength == 0
            ? null
            : (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var code) ? code.GetString() : null;

    /// <summary>A user who holds the given system permissions through a fresh role.</summary>
    private async Task<(HttpClient Client, Guid UserId, Guid RoleId)> DelegateAsync(string prefix, params string[] permissions)
    {
        var admin = await factory.AdminAsync();
        var (_, userId) = await factory.CreateUserAsync(prefix);
        var role = await admin.PostAsJsonAsync("/api/v1/admin/roles", new { code = $"R{Guid.NewGuid():N}"[..16], name = prefix, description = (string?)null });
        role.StatusCode.ShouldBe(HttpStatusCode.Created, await role.Content.ReadAsStringAsync());
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PutAsJsonAsync($"/api/v1/admin/roles/{roleId}/permissions", new { permissionCodes = permissions })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PutAsync($"/api/v1/admin/roles/{roleId}/users/{userId}", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // A fresh sign-in, so the principal is built with the role.
        var client = factory.CreateClient();
        var username = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/users/{userId}")).GetProperty("user").GetProperty("username").GetString()!;
        var tokens = await client.LoginAsync(username, DocumentTestKit.UserPassword);
        return (client.WithToken(tokens.AccessToken), userId, roleId);
    }

    private static Task<HttpResponseMessage> CreateUserRawAsync(HttpClient client, string username, bool isSystemAdmin, bool mustChangePassword = false) =>
        client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            username,
            displayName = username,
            email = (string?)null,
            password = DocumentTestKit.UserPassword,
            isSystemAdmin,
            mustChangePassword,
        });

    [Fact]
    public async Task A_user_manager_cannot_make_or_touch_an_administrator()
    {
        var admin = await factory.AdminAsync();
        var adminId = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetGuid();
        var (manager, managerId, _) = await DelegateAsync("users.manager", "ADMIN_MANAGE_USERS");

        var promoted = await CreateUserRawAsync(manager, $"sneaky.{Guid.NewGuid():N}"[..20], isSystemAdmin: true);
        promoted.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(promoted)).ShouldBe("user.admin_protected");

        (await manager.PostAsJsonAsync($"/api/v1/admin/users/{adminId}/active", new { isActive = false })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await manager.PostAsJsonAsync($"/api/v1/admin/users/{adminId}/password", new { newPassword = "TakenOverPassword!1" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await manager.PutAsJsonAsync($"/api/v1/admin/users/{adminId}", new { displayName = "x", email = (string?)null })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await manager.PutAsJsonAsync($"/api/v1/admin/users/{managerId}/admin", new { isSystemAdmin = true })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Ordinary accounts are theirs to run, but not their own deactivation.
        (await CreateUserRawAsync(manager, $"plain.{Guid.NewGuid():N}"[..20], isSystemAdmin: false)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var self = await manager.PostAsJsonAsync($"/api/v1/admin/users/{managerId}/active", new { isActive = false });
        (await CodeAsync(self)).ShouldBe("user.not_yourself");

        // An administrator cannot remove themselves either, so one always remains.
        (await CodeAsync(await admin.PutAsJsonAsync($"/api/v1/admin/users/{adminId}/admin", new { isSystemAdmin = false }))).ShouldBe("user.not_yourself");
        (await CodeAsync(await admin.PostAsJsonAsync($"/api/v1/admin/users/{adminId}/active", new { isActive = false }))).ShouldBe("user.not_yourself");
    }

    [Fact]
    public async Task An_administrator_can_promote_and_demote_but_not_the_last_one()
    {
        var admin = await factory.AdminAsync();
        var (_, userId) = await factory.CreateUserAsync("promoted");

        (await admin.PutAsJsonAsync($"/api/v1/admin/users/{userId}/admin", new { isSystemAdmin = true })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/users/{userId}")).GetProperty("user").GetProperty("isSystemAdmin").GetBoolean().ShouldBeTrue();
        await factory.ShouldHaveAuditAsync("USER_ADMIN_GRANTED");

        (await admin.PutAsJsonAsync($"/api/v1/admin/users/{userId}/admin", new { isSystemAdmin = false })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.ShouldHaveAuditAsync("USER_ADMIN_REVOKED");
    }

    [Fact]
    public async Task A_forced_password_change_is_enforced_by_the_server()
    {
        var admin = await factory.AdminAsync();
        var username = $"newcomer.{Guid.NewGuid():N}"[..22];
        (await CreateUserRawAsync(admin, username, isSystemAdmin: false, mustChangePassword: true)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(username, DocumentTestKit.UserPassword);
        var gated = client.WithToken(tokens.AccessToken);

        var refused = await gated.GetAsync("/api/v1/documents");
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(refused)).ShouldBe("auth.password_change_required");
        (await gated.GetAsync("/api/v1/categories")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var me = await gated.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        me.GetProperty("mustChangePassword").GetBoolean().ShouldBeTrue();

        const string fresh = "BrandNewPassword!23";
        (await gated.PostAsJsonAsync("/api/v1/auth/change-password", new { currentPassword = DocumentTestKit.UserPassword, newPassword = fresh }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The old token still says "change first"; a new sign-in does not.
        (await gated.GetAsync("/api/v1/documents")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var again = factory.CreateClient();
        var renewed = await again.LoginAsync(username, fresh);
        (await again.WithToken(renewed.AccessToken).GetAsync("/api/v1/documents")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_administrator_resets_a_forgotten_password()
    {
        var admin = await factory.AdminAsync();
        var username = $"forgetful.{Guid.NewGuid():N}"[..22];
        var created = await CreateUserRawAsync(admin, username, isSystemAdmin: false);
        var userId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(username, DocumentTestKit.UserPassword);

        (await admin.PostAsJsonAsync($"/api/v1/admin/users/{userId}/password", new { newPassword = "short" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync($"/api/v1/admin/users/{userId}/password", new { newPassword = "TemporaryPassword!9" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Their sessions ended, and the next sign-in must change the temporary password.
        (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var next = factory.CreateClient();
        var renewed = await next.LoginAsync(username, "TemporaryPassword!9");
        (await next.WithToken(renewed.AccessToken).GetAsync("/api/v1/documents")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await factory.ShouldHaveAuditAsync("USER_PASSWORD_RESET");
    }

    [Fact]
    public async Task A_role_manager_cannot_hand_out_what_they_do_not_hold()
    {
        var admin = await factory.AdminAsync();
        var (manager, _, ownRoleId) = await DelegateAsync("roles.manager", "ADMIN_MANAGE_ROLES", "AUDIT_VIEW");

        var created = await manager.PostAsJsonAsync("/api/v1/admin/roles", new { code = $"R{Guid.NewGuid():N}"[..16], name = "حسابرسان", description = (string?)null });
        var roleId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await manager.PutAsJsonAsync($"/api/v1/admin/roles/{roleId}/permissions", new { permissionCodes = new[] { "AUDIT_VIEW" } }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var exceeding = await manager.PutAsJsonAsync($"/api/v1/admin/roles/{roleId}/permissions", new { permissionCodes = new[] { "AUDIT_VIEW", "AUDIT_EXPORT" } });
        exceeding.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(exceeding)).ShouldBe("role.exceeds_rights");

        // Nor assign someone (themselves included) a role that carries more.
        var (_, otherId) = await factory.CreateUserAsync("roles.target");
        var strong = await admin.PostAsJsonAsync("/api/v1/admin/roles", new { code = $"R{Guid.NewGuid():N}"[..16], name = "مدیر کاربران", description = (string?)null });
        var strongId = (await strong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await admin.PutAsJsonAsync($"/api/v1/admin/roles/{strongId}/permissions", new { permissionCodes = new[] { "ADMIN_MANAGE_USERS" } });
        (await manager.PutAsync($"/api/v1/admin/roles/{strongId}/users/{otherId}", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await manager.PutAsync($"/api/v1/admin/roles/{roleId}/users/{otherId}", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var members = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/admin/roles/{roleId}/users");
        members.EnumerateArray().Select(member => member.GetProperty("id").GetGuid()).ShouldBe([otherId]);
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/roles?userId={otherId}")).EnumerateArray()
            .Select(role => role.GetProperty("id").GetGuid()).ShouldBe([roleId]);
        (await manager.GetFromJsonAsync<JsonElement>("/api/v1/admin/roles")).EnumerateArray().ShouldContain(role => role.GetProperty("id").GetGuid() == ownRoleId);
    }

    [Fact]
    public async Task Groups_can_be_renamed_switched_off_and_listed_with_their_members()
    {
        var admin = await factory.AdminAsync();
        var (_, userId) = await factory.CreateUserAsync("group.member");
        var created = await admin.PostAsJsonAsync("/api/v1/admin/groups", new { code = $"G{Guid.NewGuid():N}"[..12], name = "بایگانی" });
        var groupId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PutAsync($"/api/v1/admin/groups/{groupId}/members/{userId}", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await admin.PutAsJsonAsync($"/api/v1/admin/groups/{groupId}", new { name = "بایگانی مرکزی", isActive = false })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var group = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/groups")).EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == groupId);
        group.GetProperty("name").GetString().ShouldBe("بایگانی مرکزی");
        group.GetProperty("isActive").GetBoolean().ShouldBeFalse();

        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/groups/{groupId}/members")).EnumerateArray()
            .Select(member => member.GetProperty("id").GetGuid()).ShouldBe([userId]);
        var details = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/users/{userId}");
        details.GetProperty("groups").EnumerateArray().ShouldContain(item => item.GetProperty("id").GetGuid() == groupId);
    }

    [Fact]
    public async Task The_acl_editor_sees_inherited_entries_names_and_why()
    {
        var admin = await factory.AdminAsync();
        var (reader, readerId) = await factory.CreateUserAsync("acl.reader");
        var parent = await admin.CreateCategoryAsync(name: "والد");
        var child = await admin.CreateCategoryAsync(parent, "فرزند");
        var inherited = await admin.GrantAsync("Category", parent, readerId, "DOCUMENT_VIEW", inherit: true);
        var direct = await admin.GrantAsync("Category", child, readerId, "DOCUMENT_DOWNLOAD", effect: "Deny");

        var acl = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/resources/Category/{child}/permissions?includeInherited=true");
        var entries = acl.EnumerateArray().ToList();
        var own = entries.Single(entry => entry.GetProperty("id").GetGuid() == direct);
        own.GetProperty("isInherited").GetBoolean().ShouldBeFalse();
        own.GetProperty("subjectName").GetString().ShouldNotBeNullOrEmpty();
        var fromParent = entries.Single(entry => entry.GetProperty("id").GetGuid() == inherited);
        fromParent.GetProperty("isInherited").GetBoolean().ShouldBeTrue();
        fromParent.GetProperty("resourceId").GetGuid().ShouldBe(parent);

        // Without the flag only the category's own entries.
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/resources/Category/{child}/permissions")).EnumerateArray()
            .ShouldNotContain(entry => entry.GetProperty("id").GetGuid() == inherited);

        var why = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/permissions/explain?userId={readerId}&resourceType=Category&resourceId={child}"))
            .EnumerateArray().ToDictionary(item => item.GetProperty("permissionCode").GetString()!);
        why["DOCUMENT_VIEW"].GetProperty("reason").GetString().ShouldBe("AllowedByInheritedAcl");
        why["DOCUMENT_VIEW"].GetProperty("source").GetProperty("resourceId").GetGuid().ShouldBe(parent);
        why["DOCUMENT_VIEW"].GetProperty("source").GetProperty("subjectName").GetString().ShouldNotBeNullOrEmpty();
        why["DOCUMENT_DOWNLOAD"].GetProperty("reason").GetString().ShouldBe("DeniedByExplicitDeny");
        why["DOCUMENT_DOWNLOAD"].GetProperty("source").GetProperty("effect").GetString().ShouldBe("Deny");
        why["DOCUMENT_EDIT"].GetProperty("source").ValueKind.ShouldBe(JsonValueKind.Null);

        // Every use of the administrator's bypass is audited: listing, explaining and revoking too.
        (await admin.DeleteAsync($"/api/v1/permissions/{direct}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT array_agg(DISTINCT metadata->>'operation') FROM audit.audit_logs
             WHERE action = 'ADMIN_PERMISSION_OVERRIDE' AND entity_id = @child
            """;
        command.Parameters.AddWithValue("child", child);
        var operations = (string[])(await command.ExecuteScalarAsync())!;
        operations.ShouldBe(["explain", "grant", "list", "revoke"], ignoreOrder: true);

        // Nobody else manages this category.
        (await reader.GetAsync($"/api/v1/resources/Category/{child}/permissions")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
