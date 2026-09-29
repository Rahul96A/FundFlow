using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Exceptions;
using FundFlow.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace FundFlow.Application.Identity.Services;

/// <summary>
/// Guards the privilege model:
/// <list type="bullet">
/// <item>users cannot grant permissions they do not hold, unless they are authorized administrators;</item>
/// <item>only administrators may modify other administrators (a user manager must not be able to demote the owner);</item>
/// <item>an organization always keeps at least one active administrator.</item>
/// </list>
/// </summary>
public interface IRoleAssignmentGuard
{
    Task EnsureCanGrantAsync(IEnumerable<string> permissions, CancellationToken cancellationToken);

    Task EnsureCanManageUserAsync(Guid targetUserId, CancellationToken cancellationToken);

    Task EnsureAnotherAdministratorRemainsAsync(Guid userBeingChanged, CancellationToken cancellationToken);
}

public sealed class RoleAssignmentGuard(
    ICurrentUser currentUser,
    IUserAccessProvider accessProvider,
    IAppDbContext db) : IRoleAssignmentGuard
{
    public async Task EnsureCanGrantAsync(IEnumerable<string> permissions, CancellationToken cancellationToken)
    {
        var access = await GetActorAccessAsync(cancellationToken);

        var missing = PrivilegeEscalationPolicy.Missing(access.Permissions, access.IsAdministrator, permissions);
        if (missing.Count > 0)
        {
            throw new ForbiddenException(
                $"You cannot grant permissions you do not hold yourself: {string.Join(", ", missing)}.",
                "privilege_escalation");
        }
    }

    public async Task EnsureCanManageUserAsync(Guid targetUserId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAccessAsync(cancellationToken);
        if (actor.IsAdministrator)
        {
            return;
        }

        var target = await accessProvider.GetAsync(targetUserId, cancellationToken);
        if (target.IsAdministrator)
        {
            throw new ForbiddenException("Only administrators can change other administrators.", "privilege_escalation");
        }
    }

    public async Task EnsureAnotherAdministratorRemainsAsync(Guid userBeingChanged, CancellationToken cancellationToken)
    {
        var otherAdminExists = await db.Users.AsNoTracking()
            .AnyAsync(
                u => u.IsActive
                     && u.Id != userBeingChanged
                     && u.UserRoles.Any(ur => ur.Role.Name == SystemRoles.OrganizationAdmin),
                cancellationToken);

        if (!otherAdminExists)
        {
            throw new ConflictException(
                "An organization must keep at least one active administrator. Assign the administrator role to someone else first.",
                "last_administrator");
        }
    }

    private async Task<UserAccess> GetActorAccessAsync(CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("Authentication is required.");
        return await accessProvider.GetAsync(actorId, cancellationToken);
    }
}
