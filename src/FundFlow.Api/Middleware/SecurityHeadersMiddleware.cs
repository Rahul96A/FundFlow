using FundFlow.Api.Configuration;

namespace FundFlow.Api;

/// <summary>
/// Defence-in-depth response headers. The API only returns JSON, so its CSP forbids everything; Swagger UI
/// (Development) and the Hangfire dashboard need a looser policy scoped to their own paths. When this host also serves
/// the React app (single-origin hosting) the app shell gets the policy the nginx image uses.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IConfiguration configuration, IWebHostEnvironment environment)
{
    private const string ApiCsp = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
    private const string AppShellCsp = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    private const string ToolingCsp = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'";

    private readonly bool _servesApp = SpaHosting.IsSpaHosted(configuration, environment);

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            var path = context.Request.Path;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=(), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-site";

            var isTooling = path.StartsWithSegments("/swagger") || path.StartsWithSegments("/hangfire");
            headers["Content-Security-Policy"] = isTooling ? ToolingCsp : _servesApp && !SpaHosting.IsReserved(path) ? AppShellCsp : ApiCsp;

            // API responses carry private, per-user data: never let a browser or shared cache keep them.
            if (path.StartsWithSegments("/api"))
            {
                headers.CacheControl = "no-store";
                headers.Pragma = "no-cache";
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
