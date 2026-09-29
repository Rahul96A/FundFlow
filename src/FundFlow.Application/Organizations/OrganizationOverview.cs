using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Exceptions;
using FundFlow.Contracts.Organizations;
using FundFlow.Domain.Identity;
using FundFlow.Domain.Organizations;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FundFlow.Application.Organizations;

public sealed record GetOrganizationOverviewQuery : IRequest<OrganizationOverviewResponse>;

/// <summary>
/// Data behind the dashboard. Each section is included only if the caller holds the permission that would
/// let them open the underlying list, so the dashboard never leaks more than the pages it links to.
/// </summary>
public sealed class GetOrganizationOverviewHandler(
    IAppDbContext db,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IUserAccessProvider accessProvider,
    TimeProvider clock) : IRequestHandler<GetOrganizationOverviewQuery, OrganizationOverviewResponse>
{
    private const int ActivityDays = 14;
    private const int RecentActivityCount = 8;

    public async Task<OrganizationOverviewResponse> Handle(GetOrganizationOverviewQuery request, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("This operation requires an organization.");
        var userId = currentUser.UserId ?? throw new UnauthorizedException("Authentication is required.");
        var access = await accessProvider.GetAsync(userId, cancellationToken);

        var profileCompleted = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == tenantId)
            .Select(o => o.PhoneNumber != null && o.Address.Line1 != null && o.Address.City != null && o.Address.Country != null)
            .FirstOrDefaultAsync(cancellationToken);

        TeamOverviewResponse? team = null;
        if (access.Has(Permissions.User.Read))
        {
            var totalUsers = await db.Users.CountAsync(cancellationToken);
            var activeUsers = await db.Users.CountAsync(u => u.IsActive, cancellationToken);
            var pending = await db.Users.CountAsync(u => u.IsActive && u.PasswordHash == null, cancellationToken);
            var roleCount = await db.Roles.CountAsync(cancellationToken);
            team = new TeamOverviewResponse(totalUsers, activeUsers, pending, roleCount);
        }

        ActivityOverviewResponse? activity = null;
        if (access.Has(Permissions.Audit.Read))
        {
            activity = await LoadActivityAsync(cancellationToken);
        }

        return new OrganizationOverviewResponse(profileCompleted, team, activity);
    }

    private async Task<ActivityOverviewResponse> LoadActivityAsync(CancellationToken cancellationToken)
    {
        var recent = await db.AuditLogs.AsNoTracking()
            .OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id)
            .Take(RecentActivityCount)
            .Select(a => new ActivityItemResponse(a.Id, a.Timestamp, a.Action, a.EntityType, a.EntityId, a.UserEmail))
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var firstDay = today.AddDays(-(ActivityDays - 1));
        var since = new DateTimeOffset(firstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var grouped = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Timestamp >= since)
            .GroupBy(a => new { a.Timestamp.Year, a.Timestamp.Month, a.Timestamp.Day })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var counts = grouped.ToDictionary(g => new DateOnly(g.Year, g.Month, g.Day), g => g.Count);
        var byDay = Enumerable.Range(0, ActivityDays)
            .Select(offset => firstDay.AddDays(offset))
            .Select(day => new ActivityPointResponse(day, counts.GetValueOrDefault(day)))
            .ToList();

        return new ActivityOverviewResponse(recent, byDay);
    }
}
