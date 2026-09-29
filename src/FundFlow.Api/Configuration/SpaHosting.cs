namespace FundFlow.Api.Configuration;

/// <summary>
/// Optional single-origin hosting. When the published output contains <c>wwwroot/index.html</c> (the built React app),
/// the API serves it, so one small host runs the whole product with no separate web server, no CORS, and the
/// refresh-token cookie staying first-party. With no <c>wwwroot</c> nothing changes: local development uses the Vite
/// dev server and the Docker stack uses nginx.
/// </summary>
public static class SpaHosting
{
    /// <summary>Paths that always belong to the API or its tooling and must never fall back to the app shell.</summary>
    private static readonly string[] ReservedPrefixes = ["/api", "/health", "/swagger", "/hangfire"];

    private const string ImmutableCache = "public,max-age=31536000,immutable";

    /// <summary>True when the SPA is bundled with this host and not switched off with <c>Spa:Enabled=false</c>.</summary>
    public static bool IsSpaHosted(IConfiguration configuration, IWebHostEnvironment environment) =>
        configuration.GetValue("Spa:Enabled", true)
        && environment.WebRootFileProvider.GetFileInfo("index.html").Exists;

    public static bool IsReserved(PathString path) => ReservedPrefixes.Any(prefix => path.StartsWithSegments(prefix));

    /// <summary>Serves the built assets. Fingerprinted files under /assets are cached for a year; everything else revalidates.</summary>
    public static IApplicationBuilder UseSpaStaticFiles(this IApplicationBuilder app) =>
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                context.Context.Response.Headers.CacheControl =
                    context.Context.Request.Path.StartsWithSegments("/assets") ? ImmutableCache : "no-cache";
            },
        });

    /// <summary>
    /// Client-side routes (/login, /settings/users …) answer with the app shell. API, health and tooling paths, anything
    /// that looks like a file, and every method other than GET/HEAD are left to 404, so a typo never returns HTML where
    /// JSON or an asset is expected.
    /// </summary>
    public static IEndpointConventionBuilder MapSpaFallback(this WebApplication app) =>
        app
            .MapFallback("{*path:nonfile}", async context =>
            {
                var request = context.Request;
                var shell = app.Environment.WebRootFileProvider.GetFileInfo("index.html"); // per request: a redeploy may replace it
                if (IsReserved(request.Path) || !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) || !shell.Exists)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.Headers.CacheControl = "no-cache";
                await context.Response.SendFileAsync(shell, context.RequestAborted);
            })
            .AllowAnonymous(); // the shell is public; the fallback authorization policy would otherwise demand a token for /login
}
