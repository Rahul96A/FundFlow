using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FundFlow.Api;

/// <summary>Builds RFC 7807 responses in one consistent shape for every error the API can produce.</summary>
public static class Problems
{
    public const string BaseUri = "https://api.fundflow.com/errors/";

    public static string TypeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => BaseUri + "validation",
        StatusCodes.Status401Unauthorized => BaseUri + "unauthorized",
        StatusCodes.Status403Forbidden => BaseUri + "forbidden",
        StatusCodes.Status404NotFound => BaseUri + "not-found",
        StatusCodes.Status409Conflict => BaseUri + "conflict",
        StatusCodes.Status422UnprocessableEntity => BaseUri + "business-rule",
        StatusCodes.Status429TooManyRequests => BaseUri + "rate-limited",
        _ => BaseUri + "internal",
    };

    public static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Validation failed",
        StatusCodes.Status401Unauthorized => "Authentication required",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Resource not found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status422UnprocessableEntity => "Business rule violated",
        StatusCodes.Status429TooManyRequests => "Too many requests",
        _ => "An unexpected error occurred",
    };

    /// <summary>Validation-style problem with a field → messages map. Field names are camelCased for JSON clients.</summary>
    public static ValidationProblemDetails Validation(
        int status,
        IEnumerable<KeyValuePair<string, string[]>> errors,
        string? code = null,
        string? detail = null)
    {
        var problem = new ValidationProblemDetails
        {
            Type = TypeFor(status),
            Title = TitleFor(status),
            Status = status,
            Detail = detail,
        };

        foreach (var (field, messages) in errors)
        {
            var key = CamelCasePath(field);
            problem.Errors[key] = problem.Errors.TryGetValue(key, out var existing) ? [.. existing, .. messages] : messages;
        }

        if (code is not null)
        {
            problem.Extensions["code"] = code;
        }

        return problem;
    }

    public static ProblemDetails Simple(int status, string? detail, string? code = null, string? title = null)
    {
        var problem = new ProblemDetails
        {
            Type = TypeFor(status),
            Title = title ?? TitleFor(status),
            Status = status,
            Detail = detail,
        };

        if (code is not null)
        {
            problem.Extensions["code"] = code;
        }

        return problem;
    }

    /// <summary>
    /// Adds what every problem must carry, however it was produced: the RFC 7807 type/title, the request path, the
    /// trace id (for support to find the request in the logs) and the caller's correlation id.
    /// </summary>
    public static void Enrich(HttpContext context, ProblemDetails problem)
    {
        var status = problem.Status ?? context.Response.StatusCode;

        // Framework-generated problems (bare 401/403/404 from UseStatusCodePages) get the same shape as ours.
        if (string.IsNullOrEmpty(problem.Type) || problem.Type.StartsWith("https://tools.ietf.org", StringComparison.Ordinal))
        {
            problem.Type = TypeFor(status);
        }

        problem.Title ??= TitleFor(status);
        problem.Instance ??= context.Request.Path;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        if (context.Items[CorrelationIdMiddleware.ItemKey] is string correlationId)
        {
            problem.Extensions["correlationId"] = correlationId;
        }
    }

    /// <summary>"RoleIds[0].Name" → "roleIds[0].name".</summary>
    public static string CamelCasePath(string path) =>
        string.Join('.', path.Split('.').Select(segment =>
        {
            var bracket = segment.IndexOf('[', StringComparison.Ordinal);
            var name = bracket < 0 ? segment : segment[..bracket];
            var suffix = bracket < 0 ? string.Empty : segment[bracket..];
            return JsonNamingPolicy.CamelCase.ConvertName(name) + suffix;
        }));
}

/// <summary>
/// Enriches every problem returned from a controller (model-binding failures, explicit <c>Problem(...)</c>/<c>NotFound()</c>
/// results) so it carries the same trace and correlation ids as problems written by the exception handler.
/// </summary>
public sealed class ProblemDetailsEnrichmentFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { Value: ProblemDetails problem })
        {
            Problems.Enrich(context.HttpContext, problem);
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
