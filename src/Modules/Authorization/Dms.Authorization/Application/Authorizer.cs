using Dms.Application;
using Dms.Authorization.Contracts;
using Dms.Identity.Contracts;
using Dms.SharedKernel;

namespace Dms.Authorization.Application;

/// <summary>
/// Loads everything the pure <see cref="PermissionEvaluator"/> needs and caches the principal set
/// for the lifetime of the request.
/// </summary>
public sealed class Authorizer(
    ICurrentUser currentUser,
    IUserDirectory users,
    IGroupMembershipReader groupMemberships,
    IUserRoleRepository userRoles,
    IRoleRepository roles,
    IResourcePermissionRepository aclEntries,
    IResourceHierarchy hierarchy,
    IEnumerable<ITemporaryGrantSource> grantSources,
    TimeProvider timeProvider) : IDmsAuthorizer
{
    private readonly Dictionary<UserId, PrincipalSet> _principalCache = [];

    public async Task<AuthorizationDecision> AuthorizeSystemAsync(
        string permissionCode,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return AuthorizationDecision.Deny(DecisionReason.DeniedUserInactive, "The request is not authenticated.");
        }

        var principal = await GetPrincipalsAsync(userId, cancellationToken);
        return PermissionEvaluator.EvaluateSystem(principal, permissionCode);
    }

    public async Task<AuthorizationDecision> AuthorizeAsync(
        string permissionCode,
        ResourceRef resource,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return AuthorizationDecision.Deny(DecisionReason.DeniedUserInactive, "The request is not authenticated.");
        }

        return await AuthorizeAsync(userId, permissionCode, resource, null, cancellationToken);
    }

    public async Task<AuthorizationDecision> AuthorizeVersionAsync(
        string permissionCode,
        ResourceRef resource,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return AuthorizationDecision.Deny(DecisionReason.DeniedUserInactive, "The request is not authenticated.");
        }

        return await AuthorizeAsync(userId, permissionCode, resource, versionId, cancellationToken);
    }

    public Task<AuthorizationDecision> AuthorizeAsync(
        UserId userId,
        string permissionCode,
        ResourceRef resource,
        CancellationToken cancellationToken) =>
        AuthorizeAsync(userId, permissionCode, resource, null, cancellationToken);

    private async Task<AuthorizationDecision> AuthorizeAsync(
        UserId userId,
        string permissionCode,
        ResourceRef resource,
        Guid? versionId,
        CancellationToken cancellationToken)
    {
        var principal = await GetPrincipalsAsync(userId, cancellationToken);

        var descriptor = versionId is { } version
            ? await hierarchy.DescribeVersionAsync(resource, version, cancellationToken)
            : await hierarchy.DescribeAsync(resource, cancellationToken);
        if (descriptor is null)
        {
            return AuthorizationDecision.Deny(
                DecisionReason.DeniedUnknownResource,
                $"{resource} does not exist.");
        }

        var resourceRefs = new List<ResourceRef>(descriptor.AncestorCategoryIds.Count + 1) { resource };
        resourceRefs.AddRange(descriptor.AncestorCategoryIds.Select(ResourceRef.Category));

        var entries = await aclEntries.GetForResourcesAsync(resourceRefs, cancellationToken);

        var grants = new List<TemporaryGrant>();
        foreach (var source in grantSources)
        {
            grants.AddRange(await source.GetGrantsAsync(userId, resource, cancellationToken));
        }

        return PermissionEvaluator.Evaluate(
            principal,
            permissionCode,
            descriptor,
            entries.Select(entry => entry.ToAclEntry()).ToList(),
            grants,
            timeProvider.GetUtcNow());
    }

    public async Task<PrincipalSet> GetPrincipalsAsync(UserId userId, CancellationToken cancellationToken)
    {
        if (_principalCache.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        var user = await users.FindAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            var anonymous = PrincipalSet.Anonymous(userId);
            _principalCache[userId] = anonymous;
            return anonymous;
        }

        var groupIds = await groupMemberships.GetGroupIdsAsync(userId, cancellationToken);
        var roleIds = await userRoles.GetRoleIdsAsync(userId, cancellationToken);
        var systemPermissions = await roles.GetPermissionCodesAsync([.. roleIds], cancellationToken);

        var principal = new PrincipalSet(
            userId,
            IsActive: true,
            user.IsSystemAdmin,
            groupIds,
            roleIds,
            systemPermissions);

        _principalCache[userId] = principal;
        return principal;
    }
}
