using Dms.SharedKernel;

namespace Dms.Identity.Domain;

public enum GroupKind
{
    Department,
    Team,
    Other,
}

/// <summary>
/// Flat group (decision D10: no nesting in v1). <see cref="ExternalId"/> is where an AD or OIDC
/// group will be mapped later without touching the authorization engine.
/// </summary>
public sealed class Group : AggregateRoot<GroupId>
{
    private Group()
    {
    }

    private Group(GroupId id, string code, string name, GroupKind kind, DateTimeOffset now)
        : base(id)
    {
        Code = code;
        Name = name;
        Kind = kind;
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public GroupKind Kind { get; private set; }

    public string? ExternalId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Group Create(string code, string name, GroupKind kind, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Group(GroupId.New(), code.Trim().ToUpperInvariant(), name.Trim(), kind, now);
    }

    public void Rename(string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        UpdatedAt = now;
    }

    public void SetActive(bool isActive, DateTimeOffset now)
    {
        IsActive = isActive;
        UpdatedAt = now;
    }
}

public sealed class UserGroupMembership
{
    private UserGroupMembership()
    {
    }

    public UserGroupMembership(UserId userId, GroupId groupId, UserId addedBy, DateTimeOffset now)
    {
        UserId = userId;
        GroupId = groupId;
        AddedBy = addedBy;
        AddedAt = now;
    }

    public UserId UserId { get; private set; }

    public GroupId GroupId { get; private set; }

    public UserId AddedBy { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }
}
