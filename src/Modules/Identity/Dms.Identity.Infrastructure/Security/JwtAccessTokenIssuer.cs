using System.Security.Claims;
using System.Text;
using Dms.Identity.Application;
using Dms.Identity.Domain;
using Dms.SharedKernel;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Dms.Identity.Infrastructure.Security;

/// <summary>
/// Short lived access token. It deliberately carries no permissions: every request re-evaluates
/// authorization server side, so a token cannot outlive a revoked grant.
/// </summary>
public sealed class JwtAccessTokenIssuer(
    IOptions<JwtOptions> jwtOptions,
    IOptions<IdentityModuleOptions> identityOptions,
    TimeProvider timeProvider) : IAccessTokenIssuer
{
    public IssuedAccessToken Issue(User user, SessionId sessionId)
    {
        var settings = jwtOptions.Value;
        var now = timeProvider.GetUtcNow();
        var expires = now.Add(identityOptions.Value.AccessTokenLifetime);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.Value.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
                new Claim("sid", sessionId.Value.ToString()),
                new Claim("name", user.DisplayName),
                new Claim("username", user.Username),
                .. user.MustChangePassword ? [new Claim(PasswordChangeGate.ClaimType, "true")] : Array.Empty<Claim>(),
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new IssuedAccessToken(token, expires);
    }
}
