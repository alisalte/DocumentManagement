using Dms.Authorization.Application;
using Dms.Authorization.Contracts;
using Dms.Authorization.Domain;
using NSubstitute;
using Shouldly;
using static Dms.Authorization.UnitTests.Scenario;

namespace Dms.Authorization.UnitTests;

/// <summary>
/// The scope is what listings and search filter on. It has to agree with the single-decision rules,
/// otherwise a user could see a row in a list that they cannot open.
/// </summary>
public sealed class AccessScopeProviderTests
{
    private readonly IDmsAuthorizer _authorizer = Substitute.For<IDmsAuthorizer>();
    private readonly IResourcePermissionRepository _entries = Substitute.For<IResourcePermissionRepository>();
    private readonly IResourceHierarchy _hierarchy = Substitute.For<IResourceHierarchy>();

    public AccessScopeProviderTests()
    {
        _authorizer.GetPrincipalsAsync(Alice, Arg.Any<CancellationToken>()).Returns(User());

        // Root > Maintenance > Contracts
        _hierarchy.GetCategoriesAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<CategoryNode>>(
        [
            new CategoryNode(RootCategory, null),
            new CategoryNode(MaintenanceCategory, RootCategory),
            new CategoryNode(ContractsCategory, MaintenanceCategory),
        ]);
    }

    private AccessScopeProvider CreateProvider(params ResourcePermissionEntry[] entries)
    {
        _entries.GetForSubjectsAsync(
                Arg.Any<IReadOnlyCollection<SubjectRef>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(entries);

        return new AccessScopeProvider(_authorizer, _entries, _hierarchy);
    }

    private static ResourcePermissionEntry Entry(
        ResourceRef resource,
        PermissionEffect effect,
        bool inherit = true,
        string permission = "DOCUMENT_VIEW") =>
        ResourcePermissionEntry.Create(
            resource,
            SubjectType.User,
            Alice.Value,
            permission,
            effect,
            inherit && resource.Type == ResourceType.Category,
            reason: null,
            Alice,
            Now).Value;

    [Fact]
    public async Task An_inherited_allow_covers_the_whole_subtree()
    {
        var provider = CreateProvider(Entry(ResourceRef.Category(RootCategory), PermissionEffect.Allow));

        var scope = await provider.GetScopeAsync(Alice, PermissionCodes.DocumentView, CancellationToken.None);

        scope.AllowedCategories.ShouldBe(
            new[] { RootCategory, MaintenanceCategory, ContractsCategory },
            ignoreOrder: true);
    }

    [Fact]
    public async Task A_non_inheriting_allow_covers_only_its_own_category()
    {
        var provider = CreateProvider(
            Entry(ResourceRef.Category(MaintenanceCategory), PermissionEffect.Allow, inherit: false));

        var scope = await provider.GetScopeAsync(Alice, PermissionCodes.DocumentView, CancellationToken.None);

        scope.AllowedCategories.ShouldBe([MaintenanceCategory]);
    }

    [Fact]
    public async Task A_deny_deeper_in_the_tree_cuts_out_that_branch()
    {
        var provider = CreateProvider(
            Entry(ResourceRef.Category(RootCategory), PermissionEffect.Allow),
            Entry(ResourceRef.Category(ContractsCategory), PermissionEffect.Deny));

        var scope = await provider.GetScopeAsync(Alice, PermissionCodes.DocumentView, CancellationToken.None);

        scope.AllowedCategories.ShouldBe(new[] { RootCategory, MaintenanceCategory }, ignoreOrder: true);
        scope.DeniedCategories.ShouldContain(ContractsCategory);
        scope.Includes(DocumentId, ContractsCategory).ShouldBeFalse();
        scope.Includes(DocumentId, MaintenanceCategory).ShouldBeTrue();
    }

    [Fact]
    public async Task A_document_level_deny_wins_over_a_category_allow()
    {
        var provider = CreateProvider(
            Entry(ResourceRef.Category(RootCategory), PermissionEffect.Allow),
            Entry(ResourceRef.Document(DocumentId), PermissionEffect.Deny));

        var scope = await provider.GetScopeAsync(Alice, PermissionCodes.DocumentView, CancellationToken.None);

        scope.DeniedResources.ShouldContain(DocumentId);
        scope.Includes(DocumentId, MaintenanceCategory).ShouldBeFalse();
    }

    [Fact]
    public async Task A_document_level_allow_works_without_any_category_access()
    {
        var provider = CreateProvider(Entry(ResourceRef.Document(DocumentId), PermissionEffect.Allow));

        var scope = await provider.GetScopeAsync(Alice, PermissionCodes.DocumentView, CancellationToken.None);

        scope.AllowedCategories.ShouldBeEmpty();
        scope.Includes(DocumentId, ContractsCategory).ShouldBeTrue();
    }

    [Fact]
    public async Task An_inactive_user_gets_an_empty_scope()
    {
        _authorizer.GetPrincipalsAsync(Bob, Arg.Any<CancellationToken>())
            .Returns(PrincipalSet.Anonymous(Bob));

        var provider = CreateProvider(Entry(ResourceRef.Category(RootCategory), PermissionEffect.Allow));

        var scope = await provider.GetScopeAsync(Bob, PermissionCodes.DocumentView, CancellationToken.None);

        scope.AllowedCategories.ShouldBeEmpty();
        scope.AllowedResources.ShouldBeEmpty();
    }
}
