namespace FundFlow.Domain.Identity;

/// <summary>
/// Business rule: users cannot hand out permissions they do not hold themselves, unless they are an
/// authorized administrator (ORGANIZATION_ADMIN / SUPER_ADMIN).
/// </summary>
public static class PrivilegeEscalationPolicy
{
    public static bool CanGrant(
        IReadOnlySet<string> actorPermissions,
        bool actorIsAdministrator,
        IEnumerable<string> permissionsToGrant)
    {
        ArgumentNullException.ThrowIfNull(actorPermissions);
        ArgumentNullException.ThrowIfNull(permissionsToGrant);

        return actorIsAdministrator || permissionsToGrant.All(actorPermissions.Contains);
    }

    /// <summary>Returns the permissions in <paramref name="permissionsToGrant"/> the actor is not allowed to grant.</summary>
    public static IReadOnlyList<string> Missing(
        IReadOnlySet<string> actorPermissions,
        bool actorIsAdministrator,
        IEnumerable<string> permissionsToGrant) =>
        actorIsAdministrator
            ? []
            : permissionsToGrant.Where(p => !actorPermissions.Contains(p)).Distinct().Order().ToArray();
}
