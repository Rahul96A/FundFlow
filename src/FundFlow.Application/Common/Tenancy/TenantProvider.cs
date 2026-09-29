using FundFlow.Application.Common.Abstractions;

namespace FundFlow.Application.Common.Tenancy;

public sealed class TenantProvider(ITenantDirectory directory) : ITenantProvider
{
    public async Task<TenantResolution> ResolveAsync(TenantRequestInfo request, CancellationToken cancellationToken)
    {
        if (request.IsAuthenticated)
        {
            if (request.IsPlatformPrincipal)
            {
                return new TenantResolution(TenantResolutionStatus.Platform);
            }

            // An authenticated tenant user without a tenant claim is a malformed token: never fall back to a URL hint.
            if (request.ClaimTenantId is not { } claimTenantId)
            {
                return new TenantResolution(TenantResolutionStatus.Mismatch);
            }

            var tenant = await directory.FindByIdAsync(claimTenantId, cancellationToken);
            if (tenant is null)
            {
                return new TenantResolution(TenantResolutionStatus.NotFound);
            }

            if (!tenant.IsActive)
            {
                return new TenantResolution(TenantResolutionStatus.Suspended, tenant);
            }

            // The token is authoritative. A URL that names a different organization is a cross-tenant attempt.
            if (request.RouteSlug is { Length: > 0 } slug
                && !string.Equals(slug, tenant.Slug, StringComparison.OrdinalIgnoreCase))
            {
                return new TenantResolution(TenantResolutionStatus.Mismatch, tenant);
            }

            return new TenantResolution(TenantResolutionStatus.Resolved, tenant);
        }

        if (string.IsNullOrWhiteSpace(request.RouteSlug))
        {
            return new TenantResolution(TenantResolutionStatus.Anonymous);
        }

        var bySlug = await directory.FindBySlugAsync(request.RouteSlug, cancellationToken);
        if (bySlug is null)
        {
            return new TenantResolution(TenantResolutionStatus.NotFound);
        }

        return bySlug.IsActive
            ? new TenantResolution(TenantResolutionStatus.Resolved, bySlug)
            : new TenantResolution(TenantResolutionStatus.Suspended, bySlug);
    }
}
