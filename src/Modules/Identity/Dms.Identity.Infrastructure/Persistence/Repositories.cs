using Dms.Identity.Application;
using Dms.Identity.Contracts;
using Dms.Identity.Domain;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Dms.Identity.Infrastructure.Persistence;

public sealed class UserRepository(IdentityDbContext context) : IUserRepository, IUserDirectory
{
    public Task<User?> FindAsync(UserId userId, CancellationToken cancellationToken) =>
        context.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        var normalized = User.Normalize(username);
        return context.Users.FirstOrDefaultAsync(user => user.NormalizedUsername == normalized, cancellationToken);
    }

    public Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken)
    {
        var normalized = User.Normalize(username);
        return context.Users.AnyAsync(user => user.NormalizedUsername == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<User>> ListAsync(
        string? search,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = context.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = User.Normalize(search);
            query = query.Where(user =>
                user.NormalizedUsername.Contains(normalized) || user.DisplayName.Contains(search));
        }

        return await query
            .OrderBy(user => user.NormalizedUsername)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<User>> FindManyAsync(
        IReadOnlyCollection<UserId> userIds,
        CancellationToken cancellationToken) =>
        await context.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToListAsync(cancellationToken);

    public void Add(User user) => context.Users.Add(user);

    async Task<UserSummary?> IUserDirectory.FindAsync(UserId userId, CancellationToken cancellationToken)
    {
        var user = await context.Users.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        return user is null ? null : ToSummary(user);
    }

    async Task<IReadOnlyList<UserSummary>> IUserDirectory.FindManyAsync(
        IReadOnlyCollection<UserId> userIds,
        CancellationToken cancellationToken) =>
        await context.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new UserSummary(user.Id, user.Username, user.DisplayName, user.IsActive, user.IsSystemAdmin))
            .ToListAsync(cancellationToken);

    private static UserSummary ToSummary(User user) =>
        new(user.Id, user.Username, user.DisplayName, user.IsActive, user.IsSystemAdmin);
}

public sealed class GroupRepository(IdentityDbContext context) : IGroupRepository
{
    public Task<Group?> FindAsync(GroupId groupId, CancellationToken cancellationToken) =>
        context.Groups.FirstOrDefaultAsync(group => group.Id == groupId, cancellationToken);

    public Task<Group?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        context.Groups.FirstOrDefaultAsync(group => group.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Group>> ListAsync(CancellationToken cancellationToken) =>
        await context.Groups.AsNoTracking().OrderBy(group => group.Code).ToListAsync(cancellationToken);

    public void Add(Group group) => context.Groups.Add(group);
}

public sealed class MembershipRepository(IdentityDbContext context) : IMembershipRepository, IGroupMembershipReader
{
    public Task<UserGroupMembership?> FindAsync(UserId userId, GroupId groupId, CancellationToken cancellationToken) =>
        context.Memberships.FirstOrDefaultAsync(
            membership => membership.UserId == userId && membership.GroupId == groupId,
            cancellationToken);

    public async Task<IReadOnlySet<GroupId>> GetGroupIdsAsync(UserId userId, CancellationToken cancellationToken)
    {
        // Only memberships of active groups become principals.
        var ids = await context.Memberships.AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .Join(
                context.Groups.Where(group => group.IsActive),
                membership => membership.GroupId,
                group => group.Id,
                (membership, _) => membership.GroupId)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    public void Add(UserGroupMembership membership) => context.Memberships.Add(membership);

    public void Remove(UserGroupMembership membership) => context.Memberships.Remove(membership);
}

public sealed class UserSessionRepository(IdentityDbContext context) : IUserSessionRepository
{
    public Task<UserSession?> FindByTokenHashAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
        context.Sessions.FirstOrDefaultAsync(session => session.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<UserSession>> ListActiveAsync(UserId userId, CancellationToken cancellationToken) =>
        await context.Sessions
            .Where(session => session.UserId == userId && session.RevokedAt == null)
            .ToListAsync(cancellationToken);

    public void Add(UserSession session) => context.Sessions.Add(session);
}
