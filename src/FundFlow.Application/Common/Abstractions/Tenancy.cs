namespace FundFlow.Application.Common.Abstractions;

/// <summary>
/// Read-only view of the tenant the current unit of work is scoped to. The DbContext reads this to build its
/// global query filters, so <em>nothing</em> tenant-owned is visible until a scope has been established.
/// </summary>
public interface ITenantContext
{
    /// <summary>The tenant the request is scoped to; null when unscoped or in platform scope.</summary>
    Guid? TenantId { get; }

    /// <summary>
    /// True for platform operators. Platform scope sees platform-level rows (TenantId == null) and the
    /// organization registry, but never a tenant's business data.
    /// </summary>
    bool IsPlatformScope { get; }
}

/// <summary>
/// Establishes the tenant scope. Set exactly once per request by <c>TenantMiddleware</c>, or by
/// authentication flows (login, password reset, ...) once the credential owner is known.
/// Attempting to switch to a different tenant within the same scope throws.
/// </summary>
public interface ITenantScope
{
    void UseTenant(Guid tenantId);

    void UsePlatform();
}

public static class TenantScopeExtensions
{
    /// <summary>Scopes to a tenant, or to platform scope when <paramref name="tenantId"/> is null.</summary>
    public static void UseScopeOf(this ITenantScope scope, Guid? tenantId)
    {
        if (tenantId is { } id)
        {
            scope.UseTenant(id);
        }
        else
        {
            scope.UsePlatform();
        }
    }
}

public sealed record TenantInfo(Guid Id, string Slug, string Name, bool IsActive);

/// <summary>Cached lookup of tenants by id or public slug (used on every request, so it must be cheap).</summary>
public interface ITenantDirectory
{
    Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken cancellationToken);

    Task InvalidateAsync(Guid id, string slug, CancellationToken cancellationToken);
}

public enum TenantResolutionStatus
{
    /// <summary>A tenant was identified and is active.</summary>
    Resolved = 1,

    /// <summary>The caller is a platform operator; no tenant.</summary>
    Platform = 2,

    /// <summary>No tenant information was supplied (e.g. login). Endpoints must not touch tenant data.</summary>
    Anonymous = 3,

    NotFound = 4,
    Suspended = 5,

    /// <summary>The caller's token and the requested tenant slug disagree, or the token lacks a tenant.</summary>
    Mismatch = 6,
}

public sealed record TenantResolution(TenantResolutionStatus Status, TenantInfo? Tenant = null);

/// <summary>The ingredients from which a request's tenant is derived (kept free of HTTP types for testability).</summary>
public sealed record TenantRequestInfo(
    bool IsAuthenticated,
    bool IsPlatformPrincipal,
    Guid? ClaimTenantId,
    string? RouteSlug);

/// <summary>Decides which tenant a request belongs to. Token claims win over URL slugs; a disagreement is an error.</summary>
public interface ITenantProvider
{
    Task<TenantResolution> ResolveAsync(TenantRequestInfo request, CancellationToken cancellationToken);
}
