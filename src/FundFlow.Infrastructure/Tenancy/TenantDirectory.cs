using FundFlow.Application.Common.Abstractions;
using FundFlow.Domain.Organizations;
using FundFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundFlow.Infrastructure.Tenancy;

/// <summary>
/// Resolves tenants by id or slug with a short cache, since the middleware asks on every request.
/// The lookups deliberately bypass tenant filters: they are how the tenant is discovered in the first place.
/// </summary>
public sealed class TenantDirectory(AppDbContext db, ICacheService cache) : ITenantDirectory
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private static string IdKey(Guid id) => $"tenant:id:{id:N}";

    private static string SlugKey(string slug) => $"tenant:slug:{slug.ToLowerInvariant()}";

    public async Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync<TenantInfo>(IdKey(id), cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var tenant = await db.Organizations.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new TenantInfo(o.Id, o.Slug, o.Name, o.Status == OrganizationStatus.Active))
            .FirstOrDefaultAsync(cancellationToken);

        if (tenant is not null)
        {
            await StoreAsync(tenant, cancellationToken);
        }

        return tenant;
    }

    public async Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var cached = await cache.GetAsync<TenantInfo>(SlugKey(normalized), cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var tenant = await db.Organizations.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.Slug == normalized)
            .Select(o => new TenantInfo(o.Id, o.Slug, o.Name, o.Status == OrganizationStatus.Active))
            .FirstOrDefaultAsync(cancellationToken);

        if (tenant is not null)
        {
            await StoreAsync(tenant, cancellationToken);
        }

        return tenant;
    }

    public async Task InvalidateAsync(Guid id, string slug, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(IdKey(id), cancellationToken);
        await cache.RemoveAsync(SlugKey(slug), cancellationToken);
    }

    private async Task StoreAsync(TenantInfo tenant, CancellationToken cancellationToken)
    {
        await cache.SetAsync(IdKey(tenant.Id), tenant, CacheDuration, cancellationToken);
        await cache.SetAsync(SlugKey(tenant.Slug), tenant, CacheDuration, cancellationToken);
    }
}
