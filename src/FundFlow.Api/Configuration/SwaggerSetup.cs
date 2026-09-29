using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FundFlow.Api.Configuration;

public static class SwaggerSetup
{
    private const string Description = """
        FundFlow is a multi-tenant fundraising, donor, event and auction platform.

        **Authentication.** `POST /api/v1/auth/login` returns a short-lived JWT access token; send it as
        `Authorization: Bearer <token>`. The refresh token is set as an HttpOnly cookie (`ff_refresh`) and is used by
        `POST /api/v1/auth/refresh`, which rotates it on every call. Browser clients must send the header
        `X-FundFlow-Client: web` to the cookie-based endpoints.

        **Authorization.** Endpoints require a *permission* (for example `User.Manage`), listed on each operation.
        Roles are bundles of permissions. Every request is scoped to the caller's organization; data of other
        organizations is invisible and returns 404.

        **Errors.** All errors are RFC 7807 `application/problem+json`. Validation failures are `400` with an
        `errors` map of field name to messages; business-rule violations are `422`; conflicts are `409`.

        **Pagination.** List endpoints accept `page`, `pageSize` (max 100), `sortBy`, `sortDirection` (`asc`|`desc`)
        and `search`, and return `{ items, page, pageSize, totalCount, totalPages }`.

        **Correlation.** Send `X-Correlation-Id` to trace a request end to end; it is echoed on every response.

        **Webhooks.** Payment-provider webhooks (`POST /api/v1/payments/webhooks/{provider}`) arrive with the
        payments module; they are signature-verified and idempotent.
        """;

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
        services.AddSwaggerGen(options =>
        {
            options.SupportNonNullableReferenceTypes();
            options.CustomSchemaIds(type => type.FullName!.Replace('+', '.').Replace("FundFlow.", string.Empty, StringComparison.Ordinal));

            var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{typeof(SwaggerSetup).Assembly.GetName().Name}.xml");
            if (File.Exists(xmlFile))
            {
                options.IncludeXmlComments(xmlFile, includeControllerXmlComments: true);
            }

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Paste the access token returned by POST /api/v1/auth/login.",
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
            });

            options.OperationFilter<ProblemResponsesOperationFilter>();
        });

        return services;
    }

    private sealed class ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider) : IConfigureOptions<SwaggerGenOptions>
    {
        public void Configure(SwaggerGenOptions options)
        {
            foreach (var version in provider.ApiVersionDescriptions)
            {
                options.SwaggerDoc(version.GroupName, new OpenApiInfo
                {
                    Title = "FundFlow API",
                    Version = version.ApiVersion.ToString(),
                    Description = version.IsDeprecated ? Description + "\n\n**This API version is deprecated.**" : Description,
                });
            }
        }
    }

    /// <summary>Documents the error responses every operation can produce, so clients see them without per-action attributes.</summary>
    private sealed class ProblemResponsesOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var isAnonymous = context.MethodInfo.GetCustomAttributes(true).OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any()
                              || (context.MethodInfo.DeclaringType?.GetCustomAttributes(true).OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any() ?? false);

            var permissions = context.MethodInfo.GetCustomAttributes(true)
                .Concat(context.MethodInfo.DeclaringType?.GetCustomAttributes(true) ?? [])
                .OfType<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
                .Select(a => a.Policy)
                .Where(p => p is not null && p.StartsWith(Security.PermissionPolicy.Prefix, StringComparison.Ordinal))
                .Select(p => p![Security.PermissionPolicy.Prefix.Length..])
                .Distinct()
                .ToList();

            if (permissions.Count > 0)
            {
                operation.Description = $"{operation.Description}\n\n**Requires permission:** {string.Join(", ", permissions.Select(p => $"`{p}`"))}".TrimStart();
            }

            operation.Responses ??= new OpenApiResponses();
            AddProblem(operation, context, "400", "Validation failed (errors map of field to messages).");
            AddProblem(operation, context, "429", "Rate limit exceeded; see the Retry-After header.");
            if (!isAnonymous)
            {
                AddProblem(operation, context, "401", "Missing, invalid or expired access token.");
                AddProblem(operation, context, "403", "The caller lacks the required permission.");
            }
        }

        private static void AddProblem(OpenApiOperation operation, OperationFilterContext context, string status, string description)
        {
            if (operation.Responses!.ContainsKey(status))
            {
                return;
            }

            operation.Responses[status] = new OpenApiResponse
            {
                Description = description,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/problem+json"] = new()
                    {
                        Schema = context.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), context.SchemaRepository),
                    },
                },
            };
        }
    }
}
