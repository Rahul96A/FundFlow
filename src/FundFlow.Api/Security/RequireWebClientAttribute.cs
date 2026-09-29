using FundFlow.Application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace FundFlow.Api.Security;

public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}

/// <summary>
/// CSRF protection for the only cookie-authenticated endpoints (refresh/logout). Together with the SameSite=Strict
/// cookie it requires a custom header — which a cross-site form or image tag cannot send and a cross-origin script
/// can only send if CORS explicitly allows that origin — and rejects requests whose Origin is not ours.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireWebClientAttribute : Attribute, IAsyncActionFilter
{
    public const string HeaderName = "X-FundFlow-Client";
    public const string HeaderValue = "web";

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;

        if (request.Headers[HeaderName] != HeaderValue)
        {
            throw new ForbiddenException("This request must originate from the FundFlow web client.", "csrf_header_missing");
        }

        var origin = request.Headers.Origin.ToString();
        if (origin.Length > 0 && !IsTrustedOrigin(context.HttpContext, origin))
        {
            throw new ForbiddenException("This request came from an untrusted origin.", "csrf_origin_rejected");
        }

        return next();
    }

    private static bool IsTrustedOrigin(HttpContext context, string origin)
    {
        var allowed = context.RequestServices.GetRequiredService<IOptions<CorsSettings>>().Value.AllowedOrigins;
        if (allowed.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        // Same-origin requests (SPA and API served from one host through the reverse proxy).
        var request = context.Request;
        return string.Equals(origin, $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase);
    }
}
