using Dms.SharedKernel;

namespace Dms.Identity.Domain;

/// <summary>
/// Where the account is authenticated. The user registry itself is always local, so that
/// authorization never depends on an external directory being reachable (decision D2).
/// </summary>
public enum AuthSource
{
    Local,
    Oidc,
    Ldap,
}

public sealed class User : AggregateRoot<UserId>
{
    private User()
    {
    }

    private User(UserId id, string username, string displayName, string? email, DateTimeOffset now)
        : base(id)
    {
        Username = username;
        NormalizedUsername = Normalize(username);
        DisplayName = displayName;
        Email = email;
        NormalizedEmail = email is null ? null : Normalize(email);
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string Username { get; private set; } = string.Empty;

    public string NormalizedUsername { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string? Email { get; private set; }

    public string? NormalizedEmail { get; private set; }

    public string? PasswordHash { get; private set; }

    public AuthSource AuthSource { get; private set; } = AuthSource.Local;

    /// <summary>Subject id in the external identity provider, once OIDC or LDAP is wired up.</summary>
    public string? ExternalId { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// System administrators hold every system permission and may always manage ACLs, but they do
    /// not get document content access for free (decision D5).
    /// </summary>
    public bool IsSystemAdmin { get; private set; }

    public bool MustChangePassword { get; private set; }

    public UserId? ManagerId { get; private set; }

    public int AccessFailedCount { get; private set; }

    public DateTimeOffset? LockoutEndsAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();

    public static User CreateLocal(
        string username,
        string displayName,
        string? email,
        string passwordHash,
        bool isSystemAdmin,
        bool mustChangePassword,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        return new User(UserId.New(), username.Trim(), displayName.Trim(), email?.Trim(), now)
        {
            PasswordHash = passwordHash,
            AuthSource = AuthSource.Local,
            IsSystemAdmin = isSystemAdmin,
            MustChangePassword = mustChangePassword,
        };
    }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndsAt is { } until && until > now;

    public void RegisterFailedLogin(int maxAttempts, TimeSpan lockoutDuration, DateTimeOffset now)
    {
        AccessFailedCount++;
        if (AccessFailedCount >= maxAttempts)
        {
            LockoutEndsAt = now.Add(lockoutDuration);
            AccessFailedCount = 0;
        }

        UpdatedAt = now;
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        AccessFailedCount = 0;
        LockoutEndsAt = null;
        LastLoginAt = now;
        UpdatedAt = now;
    }

    public void SetPassword(string passwordHash, bool mustChangePassword, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        MustChangePassword = mustChangePassword;
        UpdatedAt = now;
    }

    public void SetActive(bool isActive, DateTimeOffset now)
    {
        IsActive = isActive;
        UpdatedAt = now;
    }

    public void SetManager(UserId? managerId, DateTimeOffset now)
    {
        ManagerId = managerId;
        UpdatedAt = now;
    }

    public void SetSystemAdmin(bool isSystemAdmin, DateTimeOffset now)
    {
        IsSystemAdmin = isSystemAdmin;
        UpdatedAt = now;
    }

    public void ChangeProfile(string displayName, string? email, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
        Email = email?.Trim();
        NormalizedEmail = Email is null ? null : Normalize(Email);
        UpdatedAt = now;
    }
}
