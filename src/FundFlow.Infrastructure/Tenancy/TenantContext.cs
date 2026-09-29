using FundFlow.Application.Common.Abstractions;

namespace FundFlow.Infrastructure.Tenancy;

/// <summary>
/// The scoped holder of "which tenant is this unit of work for". Deliberately one-way: once a scope is chosen it
/// cannot be widened or switched, so a bug cannot silently hop from one tenant's data to another's.
/// </summary>
public sealed class TenantContext : ITenantContext, ITenantScope
{
    public Guid? TenantId { get; private set; }

    public bool IsPlatformScope { get; private set; }

    public void UseTenant(Guid tenantId)
    {
        if (IsPlatformScope)
        {
            throw new InvalidOperationException("The unit of work is already in platform scope and cannot enter a tenant.");
        }

        if (TenantId is { } existing && existing != tenantId)
        {
            throw new InvalidOperationException("The unit of work is already scoped to a different tenant.");
        }

        TenantId = tenantId;
    }

    public void UsePlatform()
    {
        if (TenantId is not null)
        {
            throw new InvalidOperationException("The unit of work is already scoped to a tenant and cannot enter platform scope.");
        }

        IsPlatformScope = true;
    }
}

/// <summary>Used by design-time tooling and background jobs that intentionally run with no scope.</summary>
public sealed class NoTenantContext : ITenantContext
{
    public Guid? TenantId => null;

    public bool IsPlatformScope => false;
}
