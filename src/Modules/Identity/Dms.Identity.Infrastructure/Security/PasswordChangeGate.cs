using Dms.SharedKernel;
using Dms.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Dms.Identity.Infrastructure.Security;

/// <summary>
/// A user who must change their password (a new account, or one an administrator reset) may do
/// that and nothing else, whatever the client does (phase 1 review, fixed in phase 8). The access
/// token says so; changing the password ends every session, so the next token no longer does.
/// </summary>
public static class PasswordChangeGate
{
    public const string ClaimType = "pwd_change";

    /// <summary>Signing in, out, finding out who you are and changing the password stay open.</summary>
    private static readonly PathString Allowed = "/api/v1/auth";

    public static readonly Error Required = Error.Forbidden(
        "auth.password_change_required",
        "Change your password before doing anything else.");

    public static IApplicationBuilder UsePasswordChangeGate(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.User.HasClaim(ClaimType, "true") && !context.Request.Path.StartsWithSegments(Allowed))
            {
                await ApiResults.Problem(Required).ExecuteAsync(context);
                return;
            }

            await next(context);
        });
}
