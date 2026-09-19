using Dms.SharedKernel;

namespace Dms.Authorization.Contracts;

/// <summary>
/// The single entry point for permission questions. Handlers and endpoint filters call this;
/// no controller, endpoint or query ever reimplements the rules.
/// </summary>
public interface IDmsAuthorizer
{
    /// <summary>System-scope permission for the ambient caller (roles only, no ACL).</summary>
    Task<AuthorizationDecision> AuthorizeSystemAsync(string permissionCode, CancellationToken cancellationToken);

    /// <summary>Resource permission for the ambient caller.</summary>
    Task<AuthorizationDecision> AuthorizeAsync(
        string permissionCode,
        ResourceRef resource,
        CancellationToken cancellationToken);

    /// <summary>Resource permission for an explicit user, used by admin tooling and the explain endpoint.</summary>
    Task<AuthorizationDecision> AuthorizeAsync(
        UserId userId,
        string permissionCode,
        ResourceRef resource,
        CancellationToken cancellationToken);

    Task<PrincipalSet> GetPrincipalsAsync(UserId userId, CancellationToken cancellationToken);
}

public interface IAccessScopeProvider
{
    Task<AccessScope> GetScopeAsync(UserId userId, string permissionCode, CancellationToken cancellationToken);
}

public sealed record CategoryNode(Guid Id, Guid? ParentId);

/// <summary>
/// Supplies the category tree and the state of a resource. Implemented by the Documents module in
/// phase 2; phase 1 ships a flat implementation so ACL entries on a resource id already work.
/// </summary>
public interface IResourceHierarchy
{
    Task<ResourceDescriptor?> DescribeAsync(ResourceRef resource, CancellationToken cancellationToken);

    Task<IReadOnlyList<CategoryNode>> GetCategoriesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Extra, time limited grants. Implemented by Sharing and Workflow in later phases. An explicit
/// DENY always outranks these.
/// </summary>
public interface ITemporaryGrantSource
{
    Task<IReadOnlyCollection<TemporaryGrant>> GetGrantsAsync(
        UserId userId,
        ResourceRef resource,
        CancellationToken cancellationToken);
}
