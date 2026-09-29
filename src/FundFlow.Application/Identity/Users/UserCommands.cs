using FluentValidation;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Exceptions;
using FundFlow.Application.Common.Validation;
using FundFlow.Application.Identity.Services;
using FundFlow.Contracts.Identity;
using FundFlow.Domain.Audit;
using FundFlow.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FundFlow.Application.Identity.Users;

public sealed record InviteUserCommand(
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    IReadOnlyList<Guid> RoleIds) : IRequest<UserDetailResponse>;

public sealed class InviteUserValidator : AbstractValidator<InviteUserCommand>
{
    public InviteUserValidator()
    {
        RuleFor(x => x.Email).MustBeEmail();
        RuleFor(x => x.FirstName).MustBePersonName("First name");
        RuleFor(x => x.LastName).MustBePersonName("Last name");
        RuleFor(x => x.PhoneNumber).MustBeOptionalPhone();
        RuleFor(x => x.RoleIds)
            .NotEmpty().WithMessage("Choose at least one role.")
            .Must(ids => ids.Count <= 20).WithMessage("Too many roles.");
    }
}

public sealed class InviteUserHandler(
    IAppDbContext db,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IRoleAssignmentGuard guard,
    IAuditLogger audit,
    TimeProvider clock) : IRequestHandler<InviteUserCommand, UserDetailResponse>
{
    public async Task<UserDetailResponse> Handle(InviteUserCommand command, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId
                       ?? throw new ForbiddenException("Users can only be invited into an organization.");
        var now = clock.GetUtcNow();
        var normalized = User.NormalizeEmail(command.Email);

        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken))
        {
            throw ConflictException.ForField("email", "A user with this email address already exists.", "email_taken");
        }

        var roles = await LoadRolesAsync(db, command.RoleIds, cancellationToken);
        await guard.EnsureCanGrantAsync(roles.SelectMany(r => r.Permissions.Select(p => p.PermissionName)), cancellationToken);

        var user = User.Invite(tenantId, command.Email, command.FirstName, command.LastName, command.PhoneNumber);
        user.SetRoles(roles, currentUser.UserId, now);
        db.Users.Add(user);

        audit.Record(new AuditEntry(
            AuditActions.UserInvited, nameof(User), user.Id.ToString(),
            NewValues: new { user.Email, user.FirstName, user.LastName, Roles = roles.Select(r => r.Name).Order() }));
        await db.SaveChangesAsync(cancellationToken);

        return (await db.GetUserDetailAsync(user.Id, now, cancellationToken))!;
    }

    internal static async Task<List<Role>> LoadRolesAsync(IAppDbContext db, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken)
    {
        var distinct = roleIds.Distinct().ToList();
        var roles = await db.Roles.Include(r => r.Permissions).Where(r => distinct.Contains(r.Id)).ToListAsync(cancellationToken);
        var missing = distinct.Except(roles.Select(r => r.Id)).FirstOrDefault();
        if (missing != Guid.Empty)
        {
            throw new NotFoundException(nameof(Role), missing);
        }

        return roles;
    }
}

public sealed record UpdateUserCommand(Guid UserId, string FirstName, string LastName, string? PhoneNumber) : IRequest<UserDetailResponse>;

public sealed class UpdateUserValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.FirstName).MustBePersonName("First name");
        RuleFor(x => x.LastName).MustBePersonName("Last name");
        RuleFor(x => x.PhoneNumber).MustBeOptionalPhone();
    }
}

public sealed class UpdateUserHandler(
    IAppDbContext db,
    IRoleAssignmentGuard guard,
    IAuditLogger audit,
    TimeProvider clock) : IRequestHandler<UpdateUserCommand, UserDetailResponse>
{
    public async Task<UserDetailResponse> Handle(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken)
                   ?? throw new NotFoundException(nameof(User), command.UserId);
        await guard.EnsureCanManageUserAsync(user.Id, cancellationToken);

        var before = new { user.FirstName, user.LastName, user.PhoneNumber };
        user.UpdateProfile(command.FirstName, command.LastName, command.PhoneNumber);

        audit.Record(new AuditEntry(
            AuditActions.UserUpdated, nameof(User), user.Id.ToString(),
            OldValues: before,
            NewValues: new { user.FirstName, user.LastName, user.PhoneNumber }));
        await db.SaveChangesAsync(cancellationToken);

        return (await db.GetUserDetailAsync(user.Id, clock.GetUtcNow(), cancellationToken))!;
    }
}

public sealed record SetUserRolesCommand(Guid UserId, IReadOnlyList<Guid> RoleIds) : IRequest<UserDetailResponse>;

public sealed class SetUserRolesValidator : AbstractValidator<SetUserRolesCommand>
{
    public SetUserRolesValidator()
    {
        RuleFor(x => x.RoleIds)
            .NotEmpty().WithMessage("Choose at least one role.")
            .Must(ids => ids.Count <= 20).WithMessage("Too many roles.");
    }
}

public sealed class SetUserRolesHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IRoleAssignmentGuard guard,
    IUserAccessProvider accessProvider,
    IAuditLogger audit,
    TimeProvider clock) : IRequestHandler<SetUserRolesCommand, UserDetailResponse>
{
    public async Task<UserDetailResponse> Handle(SetUserRolesCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var user = await db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                       .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken)
                   ?? throw new NotFoundException(nameof(User), command.UserId);

        await guard.EnsureCanManageUserAsync(user.Id, cancellationToken);

        var desired = await InviteUserHandler.LoadRolesAsync(db, command.RoleIds, cancellationToken);
        var before = user.UserRoles.Select(ur => ur.Role.Name).Order().ToList();

        // Only newly granted roles need the escalation check; keeping an existing role grants nothing new.
        var currentRoleIds = user.UserRoles.Select(ur => ur.RoleId).ToHashSet();
        var newlyGranted = desired.Where(r => !currentRoleIds.Contains(r.Id));
        await guard.EnsureCanGrantAsync(newlyGranted.SelectMany(r => r.Permissions.Select(p => p.PermissionName)), cancellationToken);

        var losesAdministrator = before.Contains(SystemRoles.OrganizationAdmin)
                                 && desired.All(r => r.Name != SystemRoles.OrganizationAdmin);
        if (losesAdministrator)
        {
            await guard.EnsureAnotherAdministratorRemainsAsync(user.Id, cancellationToken);
        }

        user.SetRoles(desired, currentUser.UserId, now);

        audit.Record(new AuditEntry(
            AuditActions.UserRolesChanged, nameof(User), user.Id.ToString(),
            OldValues: new { Roles = before },
            NewValues: new { Roles = desired.Select(r => r.Name).Order().ToList() }));
        await db.SaveChangesAsync(cancellationToken);
        await accessProvider.InvalidateAsync([user.Id], cancellationToken);

        return (await db.GetUserDetailAsync(user.Id, now, cancellationToken))!;
    }
}

public sealed record DeactivateUserCommand(Guid UserId) : IRequest;

public sealed class DeactivateUserHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IRoleAssignmentGuard guard,
    IUserAccessProvider accessProvider,
    ISessionRevocations revocations,
    IAuditLogger audit,
    TimeProvider clock) : IRequestHandler<DeactivateUserCommand>
{
    public async Task Handle(DeactivateUserCommand command, CancellationToken cancellationToken)
    {
        if (command.UserId == currentUser.UserId)
        {
            throw new ConflictException("You cannot deactivate your own account.", "cannot_deactivate_self");
        }

        var user = await db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                       .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken)
                   ?? throw new NotFoundException(nameof(User), command.UserId);
        if (!user.IsActive)
        {
            return;
        }

        await guard.EnsureCanManageUserAsync(user.Id, cancellationToken);
        if (user.UserRoles.Any(ur => ur.Role.Name == SystemRoles.OrganizationAdmin))
        {
            await guard.EnsureAnotherAdministratorRemainsAsync(user.Id, cancellationToken);
        }

        user.Deactivate();
        var revoked = await db.RevokeAllSessionsAsync(user.Id, "user_deactivated", clock.GetUtcNow(), except: null, cancellationToken);

        audit.Record(new AuditEntry(
            AuditActions.UserDeactivated, nameof(User), user.Id.ToString(),
            NewValues: new { user.Email, SessionsRevoked = revoked.Count }));
        await db.SaveChangesAsync(cancellationToken);
        await revocations.RevokeAsync(revoked, cancellationToken);
        await accessProvider.InvalidateAsync([user.Id], cancellationToken);
    }
}

public sealed record ReactivateUserCommand(Guid UserId) : IRequest;

public sealed class ReactivateUserHandler(IAppDbContext db, IRoleAssignmentGuard guard, IAuditLogger audit)
    : IRequestHandler<ReactivateUserCommand>
{
    public async Task Handle(ReactivateUserCommand command, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken)
                   ?? throw new NotFoundException(nameof(User), command.UserId);
        if (user.IsActive)
        {
            return;
        }

        await guard.EnsureCanManageUserAsync(user.Id, cancellationToken);
        user.Reactivate();

        audit.Record(new AuditEntry(AuditActions.UserReactivated, nameof(User), user.Id.ToString(), NewValues: new { user.Email }));
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record UnlockUserCommand(Guid UserId) : IRequest;

public sealed class UnlockUserHandler(IAppDbContext db, IRoleAssignmentGuard guard, IAuditLogger audit)
    : IRequestHandler<UnlockUserCommand>
{
    public async Task Handle(UnlockUserCommand command, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken)
                   ?? throw new NotFoundException(nameof(User), command.UserId);

        await guard.EnsureCanManageUserAsync(user.Id, cancellationToken);
        user.Unlock();

        audit.Record(new AuditEntry(AuditActions.UserUnlocked, nameof(User), user.Id.ToString(), NewValues: new { user.Email }));
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record ResendInvitationCommand(Guid UserId) : IRequest;

public sealed class ResendInvitationHandler(IAppDbContext db, IRoleAssignmentGuard guard, IAuditLogger audit)
    : IRequestHandler<ResendInvitationCommand>
{
    public async Task Handle(ResendInvitationCommand command, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken)
                   ?? throw new NotFoundException(nameof(User), command.UserId);

        await guard.EnsureCanManageUserAsync(user.Id, cancellationToken);

        try
        {
            user.ResendInvitation();
        }
        catch (FundFlow.Domain.SharedKernel.DomainException ex)
        {
            throw new ConflictException(ex.Message, ex.Code);
        }

        audit.Record(new AuditEntry(AuditActions.UserInvited, nameof(User), user.Id.ToString(), NewValues: new { user.Email, Resent = true }));
        await db.SaveChangesAsync(cancellationToken);
    }
}
