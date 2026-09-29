using System.Linq.Expressions;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Contracts.Identity;
using FundFlow.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace FundFlow.Application.Identity;

/// <summary>Everything read from the database to describe a user; mapped to a response in memory.</summary>
public sealed record UserRow(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    bool IsActive,
    DateTimeOffset? LockoutEnd,
    bool HasPassword,
    bool EmailConfirmed,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    List<UserRoleResponse> Roles)
{
    public UserStatus Status(DateTimeOffset now) =>
        IdentityQueries.DetermineStatus(IsActive, LockoutEnd, HasPassword, EmailConfirmed, now);

    public string FullName => $"{FirstName} {LastName}".Trim();

    public UserSummaryResponse ToSummary(DateTimeOffset now) =>
        new(Id, Email, FirstName, LastName, FullName, Status(now), Roles.Select(r => r.Name).Order().ToList(), LastLoginAt, CreatedAt);

    public UserDetailResponse ToDetail(DateTimeOffset now) =>
        new(Id, Email, FirstName, LastName, FullName, PhoneNumber, Status(now), EmailConfirmed, IsActive, LockoutEnd, LastLoginAt, CreatedAt,
            Roles.OrderBy(r => r.Name).ToList());
}

/// <summary>Query fragments shared by several handlers so that "what is a user's status" has one definition.</summary>
public static class IdentityQueries
{
    /// <summary>Role names of a user (in the current tenant scope), ordered for stable token contents.</summary>
    public static async Task<IReadOnlyList<string>> RoleNamesAsync(
        this IAppDbContext db,
        Guid userId,
        CancellationToken cancellationToken) =>
        await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.Role.Name)
            .OrderBy(n => n)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The user lifecycle state. Order matters: deactivation trumps everything, then lockout, then "never set a
    /// password" (invited), then "password set but email unverified".
    /// </summary>
    public static UserStatus DetermineStatus(
        bool isActive,
        DateTimeOffset? lockoutEnd,
        bool hasPassword,
        bool emailConfirmed,
        DateTimeOffset now)
    {
        if (!isActive)
        {
            return UserStatus.Deactivated;
        }

        if (lockoutEnd > now)
        {
            return UserStatus.Locked;
        }

        if (!hasPassword)
        {
            return UserStatus.Invited;
        }

        return emailConfirmed ? UserStatus.Active : UserStatus.PendingVerification;
    }

    /// <summary>SQL predicate equivalent of <see cref="DetermineStatus"/>, for filtering lists.</summary>
    public static Expression<Func<User, bool>> HasStatus(UserStatus status, DateTimeOffset now) => status switch
    {
        UserStatus.Deactivated => u => !u.IsActive,
        UserStatus.Locked => u => u.IsActive && u.LockoutEnd > now,
        UserStatus.Invited => u => u.IsActive && !(u.LockoutEnd > now) && u.PasswordHash == null,
        UserStatus.PendingVerification => u => u.IsActive && !(u.LockoutEnd > now) && u.PasswordHash != null && !u.EmailConfirmed,
        UserStatus.Active => u => u.IsActive && !(u.LockoutEnd > now) && u.PasswordHash != null && u.EmailConfirmed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown user status."),
    };

    public static readonly Expression<Func<User, UserRow>> ToRow = u => new UserRow(
        u.Id,
        u.Email,
        u.FirstName,
        u.LastName,
        u.PhoneNumber,
        u.IsActive,
        u.LockoutEnd,
        u.PasswordHash != null,
        u.EmailConfirmed,
        u.LastLoginAt,
        u.CreatedAt,
        u.UserRoles.Select(ur => new UserRoleResponse(ur.RoleId, ur.Role.Name)).ToList());

    public static async Task<UserDetailResponse?> GetUserDetailAsync(
        this IAppDbContext db,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var row = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(ToRow)
            .FirstOrDefaultAsync(cancellationToken);

        return row?.ToDetail(now);
    }
}
