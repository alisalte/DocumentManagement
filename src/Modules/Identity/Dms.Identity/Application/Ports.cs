using Dms.Identity.Domain;
using Dms.SharedKernel;

namespace Dms.Identity.Application;

public interface IUserRepository
{
    Task<User?> FindAsync(UserId userId, CancellationToken cancellationToken);

    Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken);

    Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken);

    Task<IReadOnlyList<User>> ListAsync(string? search, int skip, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<User>> FindManyAsync(IReadOnlyCollection<UserId> userIds, CancellationToken cancellationToken);

    /// <summary>Active system administrators; there must always be one left.</summary>
    Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken);

    void Add(User user);
}

public interface IGroupRepository
{
    Task<Group?> FindAsync(GroupId groupId, CancellationToken cancellationToken);

    Task<Group?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<Group>> ListAsync(CancellationToken cancellationToken);

    void Add(Group group);
}

public interface IMembershipRepository
{
    Task<UserGroupMembership?> FindAsync(UserId userId, GroupId groupId, CancellationToken cancellationToken);

    Task<IReadOnlySet<GroupId>> GetGroupIdsAsync(UserId userId, CancellationToken cancellationToken);

    /// <summary>Every group the user belongs to, active or not (for administration).</summary>
    Task<IReadOnlyList<GroupId>> ListGroupIdsAsync(UserId userId, CancellationToken cancellationToken);

    /// <summary>Every member of the group, active or not (for administration).</summary>
    Task<IReadOnlyList<UserId>> ListMemberIdsAsync(GroupId groupId, CancellationToken cancellationToken);

    void Add(UserGroupMembership membership);

    void Remove(UserGroupMembership membership);
}

public interface IUserSessionRepository
{
    Task<UserSession?> FindByTokenHashAsync(byte[] tokenHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSession>> ListActiveAsync(UserId userId, CancellationToken cancellationToken);

    void Add(UserSession session);
}

public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAt);

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(User user, SessionId sessionId);
}
