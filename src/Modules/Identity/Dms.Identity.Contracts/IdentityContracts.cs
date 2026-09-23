using Dms.SharedKernel;

namespace Dms.Identity.Contracts;

public sealed record UserSummary(
    UserId Id,
    string Username,
    string DisplayName,
    bool IsActive,
    bool IsSystemAdmin);

/// <summary>Read-only view of the user registry for other modules.</summary>
public interface IUserDirectory
{
    Task<UserSummary?> FindAsync(UserId userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSummary>> FindManyAsync(
        IReadOnlyCollection<UserId> userIds,
        CancellationToken cancellationToken);

    /// <summary>Active users whose name or username contains the text. For pickers; capped.</summary>
    Task<IReadOnlyList<UserSummary>> SearchAsync(string? text, int limit, CancellationToken cancellationToken);
}

public sealed record GroupSummary(GroupId Id, string Code, string Name, bool IsActive);

/// <summary>Read-only view of groups for other modules, for example GROUP metadata fields.</summary>
public interface IGroupDirectory
{
    Task<IReadOnlyList<GroupSummary>> FindManyAsync(
        IReadOnlyCollection<GroupId> groupIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GroupSummary>> SearchAsync(string? text, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Group membership, used by Authorization to build the principal set. Kept separate from
/// <see cref="IUserDirectory"/> so an external directory (AD/OIDC) can supply memberships later
/// without changing the authorization engine.
/// </summary>
public interface IGroupMembershipReader
{
    Task<IReadOnlySet<GroupId>> GetGroupIdsAsync(UserId userId, CancellationToken cancellationToken);
}
