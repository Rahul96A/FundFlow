using FundFlow.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

namespace FundFlow.Api.Configuration;

public static class ObservabilitySetup
{
    public const string ServiceName = "fundflow-api";

    /// <summary>
    /// Structured logging: human-readable in Development, compact JSON everywhere else (one event per line, ready
    /// for any log shipper). Passwords, tokens and card data are never passed to the logger.
    /// </summary>
    public static IHostBuilder AddApiLogging(this IHostBuilder host) =>
        host.UseSerilog((context, services, logger) =>
        {
            logger
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Service", ServiceName);

            if (context.HostingEnvironment.IsDevelopment())
            {
                logger.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
            }
            else
            {
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());
            }
        });

    public static IServiceCollection AddApiObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"] ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName, serviceVersion: typeof(ObservabilitySetup).Assembly.GetName().Version?.ToString()))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(o => o.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                    .AddHttpClientInstrumentation()
                    .AddSqlClientInstrumentation()
                    .AddSource("MassTransit");
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(FundFlow.Application.Common.Telemetry.AppMetrics.MeterName);
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    metrics.AddOtlpExporter();
                }
            });

        // /health/live: the process is up. /health/ready: it can serve traffic (database, cache, message bus).
        // MassTransit registers its own bus check tagged "ready".
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database", tags: ["ready"])
            .AddCheck<CacheHealthCheck>("cache", tags: ["ready"]);

        return services;
    }

    /// <summary>Round-trips a value through the distributed cache (Redis in real deployments).</summary>
    private sealed class CacheHealthCheck(IDistributedCache cache) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                const string key = "health:probe";
                await cache.SetStringAsync(key, "ok", new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) }, cancellationToken);
                return await cache.GetStringAsync(key, cancellationToken) == "ok"
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy("Cache did not return the value that was written.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Cache is unreachable.", ex);
            }
        }
    }
}
