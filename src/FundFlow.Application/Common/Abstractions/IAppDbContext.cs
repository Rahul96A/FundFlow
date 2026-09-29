using FundFlow.Domain.Audit;
using FundFlow.Domain.Identity;
using FundFlow.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace FundFlow.Application.Common.Abstractions;

/// <summary>
/// The application's view of the database. Handlers query the DbSets directly (projecting to DTOs with
/// <c>AsNoTracking</c> on read paths) instead of going through per-entity repositories.
/// Tenant isolation is applied by global query filters inside the implementation; the only way to see another
/// tenant's rows is an explicit, greppable <c>IgnoreQueryFilters()</c>.
/// </summary>
public interface IAppDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<OrganizationSettings> OrganizationSettings { get; }
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<UserSession> UserSessions { get; }
    DbSet<UserToken> UserTokens { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
