using FundFlow.Application.Common.Abstractions;
using FundFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundFlow.Infrastructure.Security;

/// <summary>
/// Answers "what can this user do?" from their roles, cached for a couple of minutes. Handlers that change roles
/// or role permissions call <see cref="InvalidateAsync"/> so the change takes effect immediately, not at cache expiry.
/// Deactivated users resolve to no access at all.
/// </summary>
public sealed class UserAccessProvider(AppDbContext db, ICacheService cache) : IUserAccessProvider
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    private static string Key(Guid userId) => $"access:{userId:N}";

    public async Task<UserAccess> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync<CachedAccess>(Key(userId), cancellationToken);
        if (cached is not null)
        {
            return cached.ToAccess();
        }

        // Keyed by the id from a validated token, so bypassing tenant filters here cannot cross tenants.
        var roles = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive)
            .SelectMany(u => u.UserRoles)
            .Select(ur => new { ur.Role.Name, Permissions = ur.Role.Permissions.Select(p => p.PermissionName).ToList() })
            .ToListAsync(cancellationToken);

        var access = new CachedAccess(
            roles.SelectMany(r => r.Permissions).Distinct().Order().ToArray(),
            roles.Select(r => r.Name).Distinct().Order().ToArray());

        await cache.SetAsync(Key(userId), access, CacheDuration, cancellationToken);
        return access.ToAccess();
    }

    public async Task InvalidateAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        foreach (var id in userIds.Distinct())
        {
            await cache.RemoveAsync(Key(id), cancellationToken);
        }
    }

    private sealed record CachedAccess(string[] Permissions, string[] Roles)
    {
        public UserAccess ToAccess() => new(Permissions.ToHashSet(StringComparer.Ordinal), Roles.ToHashSet(StringComparer.Ordinal));
    }
}
