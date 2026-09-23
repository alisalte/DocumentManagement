using Dms.Application;
using Dms.Identity.Application;
using Dms.Identity.Contracts;
using Dms.Identity.Infrastructure.Persistence;
using Dms.Identity.Infrastructure.Security;
using Dms.Infrastructure;
using Dms.Infrastructure.Persistence;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dms.Identity.Infrastructure;

public static class IdentityModule
{
    public const int MigrationOrder = 10;

    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<IdentityModuleOptions>(configuration.GetSection(IdentityModuleOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<BootstrapOptions>(configuration.GetSection(BootstrapOptions.SectionName));

        services.AddDmsModuleDbContext<IdentityDbContext>(MigrationOrder, IdentityDbContext.Schema);

        services.AddScoped<UserRepository>();
        services.AddScoped<IUserRepository>(sp => sp.GetRequiredService<UserRepository>());
        services.AddScoped<IUserDirectory>(sp => sp.GetRequiredService<UserRepository>());
        services.AddScoped<MembershipRepository>();
        services.AddScoped<IMembershipRepository>(sp => sp.GetRequiredService<MembershipRepository>());
        services.AddScoped<IGroupMembershipReader>(sp => sp.GetRequiredService<MembershipRepository>());
        services.AddScoped<GroupRepository>();
        services.AddScoped<IGroupRepository>(sp => sp.GetRequiredService<GroupRepository>());
        services.AddScoped<IGroupDirectory>(sp => sp.GetRequiredService<GroupRepository>());
        services.AddScoped<IUserSessionRepository, UserSessionRepository>();

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

        services.AddScoped<ICommandHandler<LoginCommand, Result<AuthTokens>>, LoginHandler>();
        services.AddScoped<ICommandHandler<RefreshTokenCommand, Result<AuthTokens>>, RefreshTokenHandler>();
        services.AddScoped<ICommandHandler<LogoutCommand, Result>, LogoutHandler>();
        services.AddScoped<ICommandHandler<ChangeOwnPasswordCommand, Result>, ChangeOwnPasswordHandler>();
        services.AddScoped<ICommandHandler<CreateUserCommand, Result<Guid>>, CreateUserHandler>();
        services.AddScoped<ICommandHandler<SetUserActiveCommand, Result>, SetUserActiveHandler>();
        services.AddScoped<ICommandHandler<SetUserManagerCommand, Result>, SetUserManagerHandler>();
        services.AddScoped<ICommandHandler<CreateGroupCommand, Result<Guid>>, CreateGroupHandler>();
        services.AddScoped<ICommandHandler<AddUserToGroupCommand, Result>, AddUserToGroupHandler>();
        services.AddScoped<ICommandHandler<RemoveUserFromGroupCommand, Result>, RemoveUserFromGroupHandler>();

        services.AddScoped<IQueryHandler<GetCurrentUserQuery, Result<CurrentUserDto>>, GetCurrentUserHandler>();
        services.AddScoped<IQueryHandler<ListUsersQuery, Result<IReadOnlyList<UserDto>>>, ListUsersHandler>();
        services.AddScoped<IQueryHandler<ListGroupsQuery, Result<IReadOnlyList<GroupDto>>>, ListGroupsHandler>();

        services.AddScoped<IDataSeeder, BootstrapAdminSeeder>();

        return services;
    }
}

public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args) => new(
        DesignTimeSupport.CreateOptions<IdentityDbContext>(IdentityDbContext.Schema),
        DesignTimeSupport.CreateSession());
}
