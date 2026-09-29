using FundFlow.Api;
using FundFlow.Api.Configuration;
using FundFlow.Application;
using FundFlow.Infrastructure;
using FundFlow.Infrastructure.Persistence;
using FundFlow.Infrastructure.Security;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddApiLogging();

builder.Services
    .AddApplication(builder.Configuration)
    .AddInfrastructure(builder.Configuration)
    .AddApiCore(builder.Configuration)
    .AddApiAuthentication()
    .AddApiRateLimiting(builder.Configuration)
    .AddApiDocumentation()
    .AddApiObservability(builder.Configuration);

var app = builder.Build();

// Fail fast on unsafe production configuration instead of starting up "working" with a well-known key.
var signingKey = app.Services.GetRequiredService<IOptions<JwtOptions>>().Value.SigningKey;
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")
    && signingKey.StartsWith("development-only", StringComparison.Ordinal))
{
    throw new InvalidOperationException("Jwt:SigningKey is still the development key. Set Jwt__SigningKey from a secret store.");
}

// --migrate: the explicit deployment step. Applies migrations and reference data, then exits without serving traffic.
if (args.Contains("--migrate"))
{
    var initializer = app.Services.GetRequiredService<DatabaseInitializer>();
    var database = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

    await initializer.MigrateAsync(CancellationToken.None);
    await initializer.SyncReferenceDataAsync(CancellationToken.None);

    if (!string.IsNullOrWhiteSpace(database.SuperAdminEmail) && !string.IsNullOrWhiteSpace(database.SuperAdminPassword))
    {
        await initializer.EnsureSuperAdminAsync(database.SuperAdminEmail, database.SuperAdminPassword, CancellationToken.None);
    }

    // Opt-in only (Database:SeedDemoData): sample organizations for a demo or evaluation deployment.
    if (database.SeedDemoData)
    {
        Log.Warning("Seeding demo organizations because Database:SeedDemoData is enabled. Do not enable this for real tenants");
        await initializer.SeedDemoDataAsync(CancellationToken.None);
    }

    // Runtime instances run without DDL rights (Hangfire:PrepareSchema=false). The deployment identity creates
    // Hangfire's tables here instead: resolving the storage installs its schema when PrepareSchema is true.
    if (app.Configuration.GetValue("Hangfire:Enabled", true))
    {
        _ = app.Services.GetRequiredService<JobStorage>();
    }

    Log.Information("Migration step complete");
    return;
}

// Development convenience only. Production never migrates implicitly; see docs/database.md.
var databaseOptions = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
if (databaseOptions.AutoMigrate)
{
    var initializer = app.Services.GetRequiredService<DatabaseInitializer>();
    await initializer.MigrateAsync(CancellationToken.None);
    await initializer.SyncReferenceDataAsync(CancellationToken.None);
    if (databaseOptions.SeedDemoData)
    {
        await initializer.SeedDemoDataAsync(CancellationToken.None);
    }
}

// Single-origin hosting: serve the built React app from wwwroot when it was published alongside the API.
var serveSpa = SpaHosting.IsSpaHosted(app.Configuration, app.Environment);

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (context, _, exception) =>
        exception is not null || context.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
        : context.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose
        : Serilog.Events.LogEventLevel.Information;
    options.EnrichDiagnosticContext = (diagnostics, httpContext) =>
    {
        diagnostics.Set("UserId", httpContext.User.FindFirst("sub")?.Value);
        diagnostics.Set("TenantId", httpContext.User.FindFirst("tenant_id")?.Value);
    };
});
app.UseExceptionHandler();
app.UseMiddleware<SecurityHeadersMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (app.Configuration.GetValue("Https:Redirect", false))
{
    app.UseHttpsRedirection();
}

if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "FundFlow API";
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "FundFlow API v1");
        options.EnablePersistAuthorization();
    });
}

if (serveSpa)
{
    app.UseSpaStaticFiles();
}

app.UseStatusCodePages();
app.UseRouting();
app.UseCors(ApiSetup.CorsPolicy);
app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync,
}).AllowAnonymous();
app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync }).AllowAnonymous();

if (serveSpa)
{
    app.MapSpaFallback();
}

if (app.Configuration.GetValue("Hangfire:Enabled", true))
{
    if (app.Environment.IsDevelopment())
    {
        // Dashboard exposes job payloads: Development only until it is fronted by SSO (Phase 8).
        app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [new AllowAllDashboardAuthorizationFilter()] });
    }

    app.Lifetime.ApplicationStarted.Register(() =>
    {
        using var scope = app.Services.CreateScope();
        FundFlow.Infrastructure.DependencyInjection.RegisterRecurringJobs(scope.ServiceProvider.GetRequiredService<IRecurringJobManager>());
    });
}

await app.RunAsync();

/// <summary>Makes the entry point visible to WebApplicationFactory in the integration tests.</summary>
public partial class Program;

internal sealed class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}

internal static class HealthResponseWriter
{
    /// <summary>Status per check, without exception messages or connection details.</summary>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = (int)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => new { status = e.Value.Status.ToString(), durationMs = (int)e.Value.Duration.TotalMilliseconds }),
        };
        return context.Response.WriteAsJsonAsync(payload);
    }
}
