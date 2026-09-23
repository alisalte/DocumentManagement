using Dms.Application;
using Dms.Authorization.Application;
using Dms.Authorization.Contracts;
using Dms.Authorization.Infrastructure.Persistence;
using Dms.Infrastructure;
using Dms.Infrastructure.Persistence;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dms.Authorization.Infrastructure;

public static class AuthorizationModule
{
    public const int MigrationOrder = 20;

    public static IServiceCollection AddAuthorizationModule(this IServiceCollection services)
    {
        services.AddDmsModuleDbContext<AuthorizationDbContext>(MigrationOrder, AuthorizationDbContext.Schema);

        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<UserRoleRepository>();
        services.AddScoped<IUserRoleRepository>(sp => sp.GetRequiredService<UserRoleRepository>());
        services.AddScoped<IRoleMembershipReader>(sp => sp.GetRequiredService<UserRoleRepository>());
        services.AddScoped<IResourcePermissionRepository, ResourcePermissionRepository>();

        services.AddScoped<IDmsAuthorizer, Authorizer>();
        services.AddScoped<IAccessScopeProvider, AccessScopeProvider>();
        services.AddScoped<IResourceAclWriter, ResourceAclWriter>();

        // Replaced by the Documents module in phase 2.
        services.TryAddScoped<IResourceHierarchy, FlatResourceHierarchy>();

        services.AddScoped<ICommandHandler<GrantResourcePermissionCommand, Result<Guid>>,
            GrantResourcePermissionHandler>();
        services.AddScoped<ICommandHandler<RevokeResourcePermissionCommand, Result>,
            RevokeResourcePermissionHandler>();
        services.AddScoped<ICommandHandler<CreateRoleCommand, Result<Guid>>, CreateRoleHandler>();
        services.AddScoped<ICommandHandler<SetRolePermissionsCommand, Result>, SetRolePermissionsHandler>();
        services.AddScoped<ICommandHandler<AssignRoleCommand, Result>, AssignRoleHandler>();
        services.AddScoped<ICommandHandler<UnassignRoleCommand, Result>, UnassignRoleHandler>();

        services.AddScoped<IQueryHandler<ListRolesQuery, Result<IReadOnlyList<RoleDto>>>, ListRolesHandler>();
        services.AddScoped<IQueryHandler<GetResourcePermissionsQuery, Result<IReadOnlyList<ResourcePermissionDto>>>,
            GetResourcePermissionsHandler>();
        services.AddScoped<IQueryHandler<ExplainPermissionsQuery, Result<IReadOnlyList<EffectivePermissionDto>>>,
            ExplainPermissionsHandler>();
        services.AddScoped<IQueryHandler<GetMyPermissionsQuery, Result<IReadOnlyList<string>>>,
            GetMyPermissionsHandler>();

        services.AddScoped<IDataSeeder, PermissionCatalogSeeder>();
        services.AddScoped<IDataSeeder, SystemRolesSeeder>();

        return services;
    }
}

public sealed class AuthorizationDbContextFactory : IDesignTimeDbContextFactory<AuthorizationDbContext>
{
    public AuthorizationDbContext CreateDbContext(string[] args) => new(
        DesignTimeSupport.CreateOptions<AuthorizationDbContext>(AuthorizationDbContext.Schema),
        DesignTimeSupport.CreateSession());
}
