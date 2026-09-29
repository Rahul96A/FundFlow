using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using FundFlow.Api.Security;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;

namespace FundFlow.Api.Configuration;

public static class ApiSetup
{
    public const string CorsPolicy = "FundFlowWeb";

    public static IServiceCollection AddApiCore(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddControllers(options =>
            {
                // Reject requests with an unsupported Accept header rather than silently returning something else.
                options.ReturnHttpNotAcceptable = true;

                // The API speaks JSON only; do not advertise (or silently produce) text/plain for strings.
                options.OutputFormatters.RemoveType<Microsoft.AspNetCore.Mvc.Formatters.StringOutputFormatter>();
                options.Filters.Add<ProblemDetailsEnrichmentFilter>();
            })
            .AddJsonOptions(json =>
            {
                json.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                json.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
            })
            .ConfigureApiBehaviorOptions(options =>
            {
                // Model-binding failures use the same problem shape as validation failures raised by handlers.
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(entry => entry.Value?.Errors.Count > 0)
                        .Select(entry => KeyValuePair.Create(
                            string.IsNullOrEmpty(entry.Key) ? "request" : entry.Key,
                            entry.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "The value is invalid." : e.ErrorMessage).ToArray()));

                    return new ObjectResult(Problems.Validation(StatusCodes.Status400BadRequest, errors))
                    {
                        StatusCode = StatusCodes.Status400BadRequest,
                        ContentTypes = { "application/problem+json" },
                    };
                };
            });

        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.AssumeDefaultVersionWhenUnspecified = false;
                options.ReportApiVersions = true;
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddMvc()
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context => Problems.Enrich(context.HttpContext, context.ProblemDetails);
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddOptions<CorsSettings>().Bind(configuration.GetSection(CorsSettings.SectionName));
        var origins = configuration.GetSection($"{CorsSettings.SectionName}:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
        {
            if (origins.Length > 0)
            {
                policy
                    .WithOrigins(origins) // explicit origins only: credentials + wildcard origins is never allowed
                    .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
                    .WithHeaders("Authorization", "Content-Type", "Accept", CorrelationIdMiddleware.HeaderName, RequireWebClientAttribute.HeaderName)
                    .WithExposedHeaders(CorrelationIdMiddleware.HeaderName, "Retry-After")
                    .AllowCredentials()
                    .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
            }
        }));

        // Behind a reverse proxy the socket address is the proxy's. Only honour X-Forwarded-* when told the proxy is ours.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            if (configuration.GetValue("Proxy:TrustForwardedHeaders", false))
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            }
            else
            {
                options.ForwardedHeaders = ForwardedHeaders.None;
            }
        });

        return services;
    }
}
