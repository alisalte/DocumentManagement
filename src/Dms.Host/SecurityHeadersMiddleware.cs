namespace Dms.Host;

/// <summary>
/// Baseline browser hardening headers (phase 9). Applied to every response, including health and
/// anonymous share-link traffic. HSTS is only set outside Development so local HTTP keeps working.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("X-Frame-Options", "DENY");
            headers.TryAdd("Referrer-Policy", "no-referrer");
            headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
            headers.TryAdd("X-Permitted-Cross-Domain-Policies", "none");
            // API responses are JSON/streams, not HTML documents — lock down script sources and framing.
            headers.TryAdd(
                "Content-Security-Policy",
                "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'");
            headers.TryAdd("Cross-Origin-Opener-Policy", "same-origin");
            if (!environment.IsDevelopment())
            {
                headers.TryAdd("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
            }

            return Task.CompletedTask;
        });

        await next(context);
    }
}

public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseDmsSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();
}
