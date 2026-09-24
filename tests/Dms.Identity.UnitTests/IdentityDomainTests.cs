using Dms.Authorization.Contracts;
using Dms.Authorization.Domain;
using Dms.Identity.Domain;
using Dms.Infrastructure.Security;
using Dms.SharedKernel;
using Shouldly;

namespace Dms.Identity.UnitTests;

public sealed class PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void A_password_verifies_against_its_own_hash()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        _hasher.Verify("correct horse battery staple", hash).ShouldBeTrue();
    }

    [Fact]
    public void A_wrong_password_does_not_verify()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        _hasher.Verify("Correct horse battery staple", hash).ShouldBeFalse();
    }

    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        // Distinct salts, so identical passwords are not recognisable in the database.
        _hasher.Hash("same password").ShouldNotBe(_hasher.Hash("same password"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("v9.1.aaaa.bbbb")]
    [InlineData("v1.notanumber.aaaa.bbbb")]
    public void A_malformed_hash_never_verifies(string hash)
    {
        _hasher.Verify("password", hash).ShouldBeFalse();
    }
}

public sealed class SecureTokenGeneratorTests
{
    private readonly SecureTokenGenerator _generator = new();

    [Fact]
    public void Each_token_is_unique_and_url_safe()
    {
        var first = _generator.Create();
        var second = _generator.Create();

        first.Value.ShouldNotBe(second.Value);
        first.Value.ShouldNotContain("+");
        first.Value.ShouldNotContain("/");
        first.Value.ShouldNotContain("=");
    }

    [Fact]
    public void The_stored_hash_matches_a_recomputed_hash_of_the_same_token()
    {
        var token = _generator.Create();

        _generator.ComputeHash(token.Value).ShouldBe(token.Hash);
    }
}

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static User CreateUser() =>
        User.CreateLocal("a.karimi", "Ali Karimi", "a@example.com", "hash", false, false, Now);

    [Fact]
    public void A_new_user_is_active_and_not_locked_out()
    {
        var user = CreateUser();

        user.IsActive.ShouldBeTrue();
        user.IsLockedOut(Now).ShouldBeFalse();
        user.NormalizedUsername.ShouldBe("A.KARIMI");
    }

    [Fact]
    public void The_account_locks_after_the_configured_number_of_failures()
    {
        var user = CreateUser();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            user.RegisterFailedLogin(3, TimeSpan.FromMinutes(15), Now);
        }

        user.IsLockedOut(Now).ShouldBeTrue();
        user.IsLockedOut(Now.AddMinutes(16)).ShouldBeFalse();
    }

    [Fact]
    public void A_successful_login_clears_the_failure_count_and_the_lockout()
    {
        var user = CreateUser();
        user.RegisterFailedLogin(3, TimeSpan.FromMinutes(15), Now);
        user.RegisterFailedLogin(3, TimeSpan.FromMinutes(15), Now);

        user.RegisterSuccessfulLogin(Now);

        user.AccessFailedCount.ShouldBe(0);
        user.IsLockedOut(Now).ShouldBeFalse();
        user.LastLoginAt.ShouldBe(Now);
    }
}

public sealed class UserSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static UserSession CreateSession() =>
        UserSession.Start(UserId.New(), [1, 2, 3], Now.AddDays(14), "127.0.0.1", "test", Now);

    [Fact]
    public void A_fresh_session_is_active()
    {
        CreateSession().IsActive(Now).ShouldBeTrue();
    }

    [Fact]
    public void An_expired_session_is_not_active()
    {
        CreateSession().IsActive(Now.AddDays(15)).ShouldBeFalse();
    }

    [Fact]
    public void A_revoked_session_is_not_active_and_keeps_its_successor()
    {
        var session = CreateSession();
        var replacement = SessionId.New();

        session.Revoke(Now, replacement);

        session.IsActive(Now).ShouldBeFalse();
        session.ReplacedBySessionId.ShouldBe(replacement);
    }

    [Fact]
    public void Revoking_twice_keeps_the_first_revocation_time()
    {
        var session = CreateSession();
        session.Revoke(Now);

        session.Revoke(Now.AddHours(1));

        session.RevokedAt.ShouldBe(Now);
    }
}

public sealed class RoleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_role_accepts_a_system_permission()
    {
        var role = Role.Create("ARCHIVIST", "Archivist", null, false, Now);

        role.Grant(PermissionCodes.AdminManageCategories).IsSuccess.ShouldBeTrue();
        role.Permissions.ShouldContain(permission =>
            permission.PermissionCode == PermissionCodes.AdminManageCategories);
    }

    [Fact]
    public void A_role_refuses_a_resource_permission()
    {
        // Resource access belongs in the ACL. Granting DOCUMENT_VIEW globally through a role would
        // silently bypass the whole permission model.
        var role = Role.Create("ARCHIVIST", "Archivist", null, false, Now);

        var result = role.Grant(PermissionCodes.DocumentView);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("permission.not_system_scoped");
        role.Permissions.ShouldBeEmpty();
    }

    [Fact]
    public void Granting_the_same_permission_twice_is_idempotent()
    {
        var role = Role.Create("ARCHIVIST", "Archivist", null, false, Now);

        role.Grant(PermissionCodes.AuditView);
        role.Grant(PermissionCodes.AuditView);

        role.Permissions.Count.ShouldBe(1);
    }

    [Fact]
    public void An_unknown_permission_is_refused()
    {
        var role = Role.Create("ARCHIVIST", "Archivist", null, false, Now);

        role.Grant("MAKE_COFFEE").IsFailure.ShouldBeTrue();
    }
}

public sealed class ResourcePermissionEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Inheritance_cannot_be_set_on_a_document_entry()
    {
        var result = ResourcePermissionEntry.Create(
            ResourceRef.Document(Guid.NewGuid()),
            SubjectType.User,
            Guid.NewGuid(),
            PermissionCodes.DocumentView,
            PermissionEffect.Allow,
            inherit: true,
            reason: null,
            UserId.New(),
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("permission.inherit_not_allowed");
    }

    [Fact]
    public void A_system_permission_cannot_be_granted_on_a_resource()
    {
        var result = ResourcePermissionEntry.Create(
            ResourceRef.Category(Guid.NewGuid()),
            SubjectType.Group,
            Guid.NewGuid(),
            PermissionCodes.AdminManageUsers,
            PermissionEffect.Allow,
            inherit: true,
            reason: null,
            UserId.New(),
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("permission.not_resource_scoped");
    }
}
