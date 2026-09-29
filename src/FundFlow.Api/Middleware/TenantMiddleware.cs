using System.Security.Claims;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Infrastructure.Security;

namespace FundFlow.Api;

/// <summary>
/// Establishes the tenant scope for the request <em>before</em> any handler runs. The tenant comes from the access
/// token; for anonymous public endpoints it comes from the <c>{tenantSlug}</c> route value. A request that names one
/// tenant while authenticated as another is rejected outright.
/// Must run after authentication and routing.
/// </summary>
public sealed class TenantMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ITenantProvider tenantProvider,
        ITenantScope tenantScope,
        IProblemDetailsService problemDetails)
    {
        var principal = context.User;
        var authenticated = principal.Identity?.IsAuthenticated == true;
        var claimTenantId = Guid.TryParse(principal.FindFirstValue(ClaimNames.TenantId), out var parsed) ? parsed : (Guid?)null;
        var platformPrincipal = authenticated && principal.FindFirstValue(ClaimNames.Scope) == ClaimNames.PlatformScope;
        var routeSlug = context.GetRouteValue("tenantSlug") as string;

        var resolution = await tenantProvider.ResolveAsync(
            new TenantRequestInfo(authenticated, platformPrincipal, claimTenantId, routeSlug),
            context.RequestAborted);

        switch (resolution.Status)
        {
            case TenantResolutionStatus.Resolved:
                tenantScope.UseTenant(resolution.Tenant!.Id);
                break;

            case TenantResolutionStatus.Platform:
                tenantScope.UsePlatform();
                break;

            case TenantResolutionStatus.Anonymous:
                break;

            case TenantResolutionStatus.Suspended when authenticated:
                await Reject(context, problemDetails, StatusCodes.Status403Forbidden,
                    "This organization has been suspended. Contact support.", "organization_suspended");
                return;

            case TenantResolutionStatus.Mismatch:
                await Reject(context, problemDetails, StatusCodes.Status403Forbidden,
                    "You do not have access to this organization.", "tenant_mismatch");
                return;

            case TenantResolutionStatus.NotFound when authenticated:
                await Reject(context, problemDetails, StatusCodes.Status401Unauthorized,
                    "Your organization no longer exists.", "unknown_tenant");
                return;

            default:
                // Unknown or suspended organization on an anonymous public URL: indistinguishable from "not found".
                await Reject(context, problemDetails, StatusCodes.Status404NotFound,
                    "The organization was not found.", "not_found");
                return;
        }

        await next(context);
    }

    private static async Task Reject(HttpContext context, IProblemDetailsService problemDetails, int status, string detail, string code)
    {
        context.Response.StatusCode = status;
        await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = Problems.Simple(status, detail, code),
        });
    }
}
