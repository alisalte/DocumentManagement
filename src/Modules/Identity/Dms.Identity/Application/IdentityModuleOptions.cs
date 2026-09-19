namespace Dms.Identity.Application;

public sealed class IdentityModuleOptions
{
    public const string SectionName = "Dms:Identity";

    public int MaxFailedAccessAttempts { get; set; } = 5;

    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    public int MinimumPasswordLength { get; set; } = 12;
}

public sealed class JwtOptions
{
    public const string SectionName = "Dms:Jwt";

    public string Issuer { get; set; } = "dms";

    public string Audience { get; set; } = "dms-api";

    /// <summary>Never hard coded: supplied through Dms__Jwt__SigningKey in the environment.</summary>
    public string SigningKey { get; set; } = string.Empty;
}
