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
        AuthorizeAsync(userId, permissionCode, resource, null, ownRightsOnly: false, cancellationToken);

    public Task<AuthorizationDecision> AuthorizeOwnRightsAsync(
        UserId userId,
        string permissionCode,
        ResourceRef resource,
        Guid versionId,
        CancellationToken cancellationToken) =>
        AuthorizeAsync(userId, permissionCode, resource, versionId, ownRightsOnly: true, cancellationToken);

    private Task<AuthorizationDecision> AuthorizeAsync(
        UserId userId,
        string permissionCode,
        ResourceRef resource,
        Guid? versionId,
        CancellationToken cancellationToken) =>
        AuthorizeAsync(userId, permissionCode, resource, versionId, ownRightsOnly: false, cancellationToken);

    private async Task<AuthorizationDecision> AuthorizeAsync(
        UserId userId,
        string permissionCode,
        ResourceRef resource,
        Guid? versionId,
        bool ownRightsOnly,
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

        var entries = (await aclEntries.GetForResourcesAsync(resourceRefs, cancellationToken))
            .Select(entry => entry.ToAclEntry())
            .ToList();

        var grants = await CollectGrantsAsync(userId, resource, includeShares: !ownRightsOnly, cancellationToken);
        if (!ownRightsOnly)
        {
            grants = await KeepBackedSharesAsync(grants, descriptor, entries, cancellationToken);
        }

        return PermissionEvaluator.Evaluate(
            principal,
            permissionCode,
            descriptor,
            entries,
            grants,
            timeProvider.GetUtcNow());
    }

    private async Task<List<TemporaryGrant>> CollectGrantsAsync(
        UserId userId,
        ResourceRef resource,
        bool includeShares,
        CancellationToken cancellationToken)
    {
        var grants = new List<TemporaryGrant>();
        foreach (var source in grantSources)
        {
            grants.AddRange(await source.GetGrantsAsync(userId, resource, cancellationToken));
        }

        if (!includeShares)
        {
            grants.RemoveAll(grant => grant.Kind == TemporaryGrantKind.Share);
        }

        return grants;
    }

    /// <summary>
    /// Decision D8: a share is only as good as its sharer's current rights. Each share grant that
    /// could apply here is kept only while the sharer still holds DOCUMENT_SHARE and the shared
    /// permission on this version through their own ACL entries or tasks, never through a share of
    /// their own, so rights cannot be passed along a chain of shares.
    /// </summary>
    private async Task<List<TemporaryGrant>> KeepBackedSharesAsync(
        List<TemporaryGrant> grants,
        ResourceDescriptor descriptor,
        IReadOnlyCollection<AclEntry> entries,
        CancellationToken cancellationToken)
    {
        if (!grants.Exists(grant => grant.Kind == TemporaryGrantKind.Share))
        {
            return grants;
        }

        var now = timeProvider.GetUtcNow();
        var grantorGrants = new Dictionary<UserId, List<TemporaryGrant>>();
        var backed = new Dictionary<(UserId, string), bool>();
        var kept = new List<TemporaryGrant>(grants.Count);

        foreach (var grant in grants)
        {
            if (grant.Kind != TemporaryGrantKind.Share)
            {
                kept.Add(grant);
                continue;
            }

            // Pinned elsewhere: the evaluator would ignore it anyway, so do not pay for the check.
            if (grant.VersionId is null || grant.VersionId != descriptor.VersionId || grant.GrantedBy is not { } grantor)
            {
                continue;
            }

            if (!backed.TryGetValue((grantor, grant.PermissionCode), out var isBacked))
            {
                var principal = await GetPrincipalsAsync(grantor, cancellationToken);
                if (!grantorGrants.TryGetValue(grantor, out var own))
                {
                    own = await CollectGrantsAsync(grantor, descriptor.Resource, includeShares: false, cancellationToken);
                    grantorGrants[grantor] = own;
                }

                isBacked =
                    PermissionEvaluator.Evaluate(principal, PermissionCodes.DocumentShare, descriptor, entries, own, now).Allowed
                    && PermissionEvaluator.Evaluate(principal, grant.PermissionCode, descriptor, entries, own, now).Allowed;
                backed[(grantor, grant.PermissionCode)] = isBacked;
            }

            if (isBacked)
            {
                kept.Add(grant);
            }
        }

        return kept;
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
