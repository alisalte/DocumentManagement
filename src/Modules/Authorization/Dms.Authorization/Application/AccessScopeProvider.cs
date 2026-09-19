using Dms.Authorization.Contracts;
using Dms.SharedKernel;

namespace Dms.Authorization.Application;

/// <summary>
/// Turns the ACL into the set of categories and resources a user may act on, so listings and search
/// can filter with a single SQL/OpenSearch predicate instead of evaluating every row.
///
/// The rules are identical to <see cref="PermissionEvaluator"/>: an ALLOW somewhere applicable and
/// no DENY anywhere applicable. Because the scope is computed from live ACL rows, a revoked
/// permission takes effect immediately and no reindexing is needed.
/// </summary>
public sealed class AccessScopeProvider(
    IDmsAuthorizer authorizer,
    IResourcePermissionRepository aclEntries,
    IResourceHierarchy hierarchy) : IAccessScopeProvider
{
    public async Task<AccessScope> GetScopeAsync(
        UserId userId,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        var principal = await authorizer.GetPrincipalsAsync(userId, cancellationToken);
        if (!principal.IsActive)
        {
            return AccessScope.Empty;
        }

        var subjects = new List<SubjectRef> { new(SubjectType.User, userId.Value) };
        subjects.AddRange(principal.GroupIds.Select(id => new SubjectRef(SubjectType.Group, id.Value)));
        subjects.AddRange(principal.RoleIds.Select(id => new SubjectRef(SubjectType.Role, id.Value)));

        var entries = await aclEntries.GetForSubjectsAsync(subjects, permissionCode, cancellationToken);
        var categories = await hierarchy.GetCategoriesAsync(cancellationToken);

        var allowedResources = new HashSet<Guid>();
        var deniedResources = new HashSet<Guid>();
        var byCategory = new Dictionary<Guid, List<AclEntry>>();

        foreach (var entry in entries.Select(entry => entry.ToAclEntry()))
        {
            if (entry.Resource.Type == ResourceType.Document)
            {
                if (entry.Effect == PermissionEffect.Deny)
                {
                    deniedResources.Add(entry.Resource.Id);
                }
                else
                {
                    allowedResources.Add(entry.Resource.Id);
                }

                continue;
            }

            if (!byCategory.TryGetValue(entry.Resource.Id, out var list))
            {
                list = [];
                byCategory[entry.Resource.Id] = list;
            }

            list.Add(entry);
        }

        var (allowedCategories, deniedCategories) = WalkCategories(categories, byCategory);

        // A document level DENY always wins, even against a category ALLOW.
        allowedResources.ExceptWith(deniedResources);

        return new AccessScope(
            principal.IsSystemAdmin,
            allowedCategories,
            deniedCategories,
            allowedResources,
            deniedResources);
    }

    /// <summary>
    /// Walks the tree from the roots, carrying inherited ALLOW/DENY downwards. Entries with
    /// inherit = false still apply to the documents of their own category, but do not reach
    /// sub-categories.
    /// </summary>
    private static (HashSet<Guid> Allowed, HashSet<Guid> Denied) WalkCategories(
        IReadOnlyList<CategoryNode> categories,
        Dictionary<Guid, List<AclEntry>> byCategory)
    {
        var allowed = new HashSet<Guid>();
        var denied = new HashSet<Guid>();
        if (categories.Count == 0)
        {
            return (allowed, denied);
        }

        var children = categories
            .Where(node => node.ParentId is not null)
            .GroupBy(node => node.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var queue = new Queue<(CategoryNode Node, bool InheritedAllow, bool InheritedDeny)>();
        foreach (var root in categories.Where(node => node.ParentId is null))
        {
            queue.Enqueue((root, false, false));
        }

        while (queue.Count > 0)
        {
            var (node, inheritedAllow, inheritedDeny) = queue.Dequeue();
            var entries = byCategory.TryGetValue(node.Id, out var list) ? list : [];

            var allowHere = entries.Any(entry => entry.Effect == PermissionEffect.Allow);
            var denyHere = entries.Any(entry => entry.Effect == PermissionEffect.Deny);

            var documentsDenied = denyHere || inheritedDeny;
            var documentsAllowed = (allowHere || inheritedAllow) && !documentsDenied;

            if (documentsDenied)
            {
                denied.Add(node.Id);
            }

            if (documentsAllowed)
            {
                allowed.Add(node.Id);
            }

            var allowDown = inheritedAllow || entries.Any(e => e is { Effect: PermissionEffect.Allow, Inherit: true });
            var denyDown = inheritedDeny || entries.Any(e => e is { Effect: PermissionEffect.Deny, Inherit: true });

            if (children.TryGetValue(node.Id, out var nodeChildren))
            {
                foreach (var child in nodeChildren)
                {
                    queue.Enqueue((child, allowDown, denyDown));
                }
            }
        }

        return (allowed, denied);
    }
}

/// <summary>
/// Phase 1 stand-in for the Documents module. Treats every resource as an existing, published,
/// uncategorised item, so ACL entries addressed directly at a resource id already resolve.
/// Phase 2 replaces this with the real category tree and document state.
/// </summary>
public sealed class FlatResourceHierarchy : IResourceHierarchy
{
    public Task<ResourceDescriptor?> DescribeAsync(ResourceRef resource, CancellationToken cancellationToken) =>
        Task.FromResult<ResourceDescriptor?>(new ResourceDescriptor(resource, []));

    public Task<IReadOnlyList<CategoryNode>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CategoryNode>>([]);
}
