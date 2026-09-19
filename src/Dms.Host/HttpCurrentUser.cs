using System.Diagnostics;
using System.Security.Claims;
using Dms.Application;
using Dms.SharedKernel;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Dms.Host;

/// <summary>
/// The caller, derived from the validated access token. The IP address comes from the connection
/// after ForwardedHeaders has applied only the configured trusted proxies, so audit rows cannot be
/// spoofed with a header.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public UserId? UserId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(value, out var id) ? new UserId(id) : null;
        }
    }

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString();

    public string CorrelationId =>
        Activity.Current?.TraceId.ToString() ?? accessor.HttpContext?.TraceIdentifier ?? string.Empty;
}

/// <summary>Non-HTTP callers: the migrator, the seeders and background jobs.</summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;

    public UserId? UserId => null;

    public string? IpAddress => null;

    public string? UserAgent => "system";

    public string CorrelationId => Activity.Current?.TraceId.ToString() ?? "system";
}
