using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FundFlow.Api.Configuration;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Master switch (integration tests turn it off; everything else leaves it on).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Login and token refresh, per client IP per minute.</summary>
    public int AuthPerMinute { get; set; } = 20;

    /// <summary>Registration, password reset, verification and invitation flows, per client IP per minute.</summary>
    public int SensitivePerMinute { get; set; } = 10;

    /// <summary>General API traffic, per authenticated user (or IP when anonymous) per minute.</summary>
    public int GlobalPerMinute { get; set; } = 600;
}

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Sensitive = "sensitive";
}

public static class RateLimitingSetup
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();
        services.AddSingleton(settings);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await problems.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = Problems.Simple(StatusCodes.Status429TooManyRequests, "Too many requests. Please slow down and try again shortly.", "rate_limited"),
                });
            };

            options.AddPolicy(RateLimitPolicies.Auth, context => Partition(settings, context, "auth", settings.AuthPerMinute));
            options.AddPolicy(RateLimitPolicies.Sensitive, context => Partition(settings, context, "sensitive", settings.SensitivePerMinute));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!settings.Enabled || context.Request.Path.StartsWithSegments("/health"))
                {
                    return RateLimitPartition.GetNoLimiter("unlimited");
                }

                var key = context.User.FindFirstValue("sub") ?? ClientKey(context);
                return RateLimitPartition.GetTokenBucketLimiter($"global:{key}", _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = settings.GlobalPerMinute,
                    TokensPerPeriod = settings.GlobalPerMinute,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });
        });

        return services;
    }

    private static RateLimitPartition<string> Partition(RateLimitingOptions settings, HttpContext context, string name, int permits) =>
        settings.Enabled
            ? RateLimitPartition.GetFixedWindowLimiter($"{name}:{ClientKey(context)}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            })
            : RateLimitPartition.GetNoLimiter($"{name}:unlimited");

    // The remote address is already the real client IP when forwarded headers from a trusted proxy were applied.
    private static string ClientKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
