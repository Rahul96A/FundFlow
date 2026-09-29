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

namespace FundFlow.Application.Identity.Auth;

public sealed record GetCurrentUserQuery : IRequest<CurrentUserResponse>;

public sealed class GetCurrentUserHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IUserAccessProvider accessProvider) : IRequestHandler<GetCurrentUserQuery, CurrentUserResponse>
{
    public async Task<CurrentUserResponse> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException("Authentication is required.");

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.PhoneNumber, u.EmailConfirmed, u.TenantId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedException("Your account no longer exists.");

        var access = await accessProvider.GetAsync(userId, cancellationToken);

        CurrentOrganizationResponse? organization = null;
        if (user.TenantId is { } tenantId)
        {
            organization = await db.Organizations.AsNoTracking()
                .Where(o => o.Id == tenantId)
                .Select(o => new CurrentOrganizationResponse(
                    o.Id,
                    o.Name,
                    o.Slug,
                    o.Settings.TimeZoneId,
                    o.Settings.CurrencyCode,
                    o.Settings.Locale,
                    o.Settings.LogoUrl,
                    o.Settings.BrandColor))
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new CurrentUserResponse(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            $"{user.FirstName} {user.LastName}".Trim(),
            user.PhoneNumber,
            user.EmailConfirmed,
            IsPlatformUser: user.TenantId is null,
            access.Roles.Order().ToList(),
            access.Permissions.Order().ToList(),
            organization);
    }
}

public sealed record UpdateProfileCommand(string FirstName, string LastName, string? PhoneNumber) : IRequest<CurrentUserResponse>;

public sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileValidator()
    {
        RuleFor(x => x.FirstName).MustBePersonName("First name");
        RuleFor(x => x.LastName).MustBePersonName("Last name");
        RuleFor(x => x.PhoneNumber).MustBeOptionalPhone();
    }
}

public sealed class UpdateProfileHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IAuditLogger audit,
    ISender sender) : IRequestHandler<UpdateProfileCommand, CurrentUserResponse>
{
    public async Task<CurrentUserResponse> Handle(UpdateProfileCommand command, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException("Authentication is required.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw new NotFoundException(nameof(User), userId);

        var before = new { user.FirstName, user.LastName, user.PhoneNumber };
        user.UpdateProfile(command.FirstName, command.LastName, command.PhoneNumber);

        audit.Record(new AuditEntry(
            AuditActions.UserUpdated, nameof(User), user.Id.ToString(),
            OldValues: before,
            NewValues: new { user.FirstName, user.LastName, user.PhoneNumber }));
        await db.SaveChangesAsync(cancellationToken);

        return await sender.Send(new GetCurrentUserQuery(), cancellationToken);
    }
}

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest;

public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(256);
        RuleFor(x => x.NewPassword).MustBeStrongPassword();
        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("Choose a password different from your current one.");
    }
}

public sealed class ChangePasswordHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IPasswordService passwords,
    ISessionRevocations revocations,
    IAuditLogger audit,
    TimeProvider clock) : IRequestHandler<ChangePasswordCommand>
{
    public async Task Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException("Authentication is required.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw new NotFoundException(nameof(User), userId);

        if (user.PasswordHash is null || passwords.Verify(user.PasswordHash, command.CurrentPassword) == PasswordVerification.Failed)
        {
            throw Failures.Field("currentPassword", "Your current password is incorrect.");
        }

        user.SetPassword(passwords.Hash(command.NewPassword));

        // Keep this device signed in; end every other session.
        var revoked = await db.RevokeAllSessionsAsync(user.Id, "password_changed", clock.GetUtcNow(), currentUser.SessionId, cancellationToken);

        audit.Record(new AuditEntry(
            AuditActions.PasswordChanged, nameof(User), user.Id.ToString(),
            NewValues: new { OtherSessionsRevoked = revoked.Count }));
        await db.SaveChangesAsync(cancellationToken);
        await revocations.RevokeAsync(revoked, cancellationToken);
    }
}

public sealed record ListMySessionsQuery : IRequest<IReadOnlyList<SessionResponse>>;

public sealed class ListMySessionsHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<ListMySessionsQuery, IReadOnlyList<SessionResponse>>
{
    public async Task<IReadOnlyList<SessionResponse>> Handle(ListMySessionsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException("Authentication is required.");
        var now = clock.GetUtcNow();
        var currentSession = currentUser.SessionId;

        return await db.UserSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => new SessionResponse(
                s.Id, s.IpAddress, s.UserAgent, s.CreatedAt, s.LastSeenAt, s.ExpiresAt, s.Id == currentSession))
            .Take(50)
            .ToListAsync(cancellationToken);
    }
}

public sealed record RevokeSessionCommand(Guid SessionId) : IRequest;

public sealed class RevokeSessionHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ISessionRevocations revocations,
    IAuditLogger audit,
    TimeProvider clock) : IRequestHandler<RevokeSessionCommand>
{
    public async Task Handle(RevokeSessionCommand command, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException("Authentication is required.");

        // Scoped to the caller's own sessions: someone else's session id looks exactly like a missing one.
        var session = await db.UserSessions.FirstOrDefaultAsync(
                          s => s.Id == command.SessionId && s.UserId == userId && s.RevokedAt == null, cancellationToken)
                      ?? throw new NotFoundException(nameof(UserSession), command.SessionId);

        session.Revoke("revoked_by_user", clock.GetUtcNow());
        audit.Record(new AuditEntry(
            AuditActions.SessionRevoked, nameof(UserSession), session.Id.ToString(),
            NewValues: new { session.IpAddress, session.UserAgent }));
        await db.SaveChangesAsync(cancellationToken);
        await revocations.RevokeAsync([session.Id], cancellationToken);
    }
}
