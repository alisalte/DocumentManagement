using Dms.Identity.Application;
using Dms.Identity.Domain;
using Dms.Identity.Infrastructure.Persistence;
using Dms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dms.Identity.Infrastructure;

public sealed class BootstrapOptions
{
    public const string SectionName = "Dms:Bootstrap";

    public string AdminUsername { get; set; } = "admin";

    public string AdminDisplayName { get; set; } = "System Administrator";

    /// <summary>Supplied through Dms__Bootstrap__AdminPassword. Never has a default.</summary>
    public string? AdminPassword { get; set; }
}

/// <summary>
/// Creates the first administrator, once, when the user table is empty. The account is forced to
/// change its password on first sign-in.
/// </summary>
public sealed class BootstrapAdminSeeder(
    IdentityDbContext context,
    IPasswordHasher passwordHasher,
    IOptions<BootstrapOptions> options,
    TimeProvider timeProvider,
    ILogger<BootstrapAdminSeeder> logger) : IDataSeeder
{
    public int Order => 20;

    public string Name => "bootstrap administrator";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await context.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.AdminPassword))
        {
            logger.LogWarning(
                "No bootstrap administrator password configured (Dms__Bootstrap__AdminPassword); " +
                "skipping. The system has no users and nobody can sign in until one is created.");
            return;
        }

        var admin = User.CreateLocal(
            settings.AdminUsername,
            settings.AdminDisplayName,
            email: null,
            passwordHasher.Hash(settings.AdminPassword),
            isSystemAdmin: true,
            mustChangePassword: true,
            timeProvider.GetUtcNow());

        context.Users.Add(admin);
        logger.LogInformation("Created bootstrap administrator '{Username}'.", admin.Username);
    }
}
