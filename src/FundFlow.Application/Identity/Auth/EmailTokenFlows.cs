using FluentValidation;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Exceptions;
using FundFlow.Application.Common.Validation;
using FundFlow.Application.Identity.Services;
using FundFlow.Domain.Audit;
using FundFlow.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FundFlow.Application.Identity.Auth;

// Flows that start from an emailed link. The caller is anonymous, so each handler discovers the tenant from the
// token's owner and scopes the unit of work to it before touching anything else.

public sealed record VerifyEmailCommand(string Token) : IRequest;

public sealed class VerifyEmailValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailValidator() => RuleFor(x => x.Token).MustBeToken();
}

public sealed class VerifyEmailHandler(
    IAppDbContext db,
    ITenantScope tenantScope,
    IUserTokenService tokens,
    IAuditLogger audit,
    TimeProvider clock) : IRequestHandler<VerifyEmailCommand>
{
    public async Task Handle(VerifyEmailCommand command, CancellationToken cancellationToken)
    {
        var token = await tokens.FindUsableAsync(command.Token, UserTokenPurpose.EmailVerification, cancellationToken)
                    ?? throw Failures.InvalidLink();
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == token.UserId, cancellationToken)
                   ?? throw Failures.InvalidLink();

        tenantScope.UseScopeOf(user.TenantId);

        token.Consume(clock.GetUtcNow());
        user.ConfirmEmail();

        audit.Record(new AuditEntry(
            AuditActions.EmailVerified, nameof(User), user.Id.ToString(),
            TenantId: user.TenantId, UserId: user.Id, UserEmail: user.Email));
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record ResendVerificationCommand(string Email) : IRequest;

public sealed class ResendVerificationValidator : AbstractValidator<ResendVerificationCommand>
{
    public ResendVerificationValidator() => RuleFor(x => x.Email).MustBeEmail();
}

/// <summary>Always succeeds from the caller's point of view, so it cannot be used to discover registered emails.</summary>
public sealed class ResendVerificationHandler(IAppDbContext db, ITenantScope tenantScope)
    : IRequestHandler<ResendVerificationCommand>
{
    public async Task Handle(ResendVerificationCommand command, CancellationToken cancellationToken)
    {
        var normalized = User.NormalizeEmail(command.Email);
        var user = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);

        if (user is null || !user.IsActive || user.EmailConfirmed || !user.HasPassword)
        {
            return;
        }

        tenantScope.UseScopeOf(user.TenantId);
        user.RequestEmailVerification();
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record ForgotPasswordCommand(string Email) : IRequest;

public sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordValidator() => RuleFor(x => x.Email).MustBeEmail();
}

public sealed class ForgotPasswordHandler(IAppDbContext db, ITenantScope tenantScope, IAuditLogger audit)
    : IRequestHandler<ForgotPasswordCommand>
{
    public async Task Handle(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        var normalized = User.NormalizeEmail(command.Email);
        var user = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);

        // Same response whether or not the account exists.
        if (user is null || !user.IsActive)
        {
            return;
        }

        tenantScope.UseScopeOf(user.TenantId);
        user.RequestPasswordReset();

        audit.Record(new AuditEntry(
            AuditActions.PasswordResetRequested, nameof(User), user.Id.ToString(),
            TenantId: user.TenantId, UserId: user.Id, UserEmail: user.Email));
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record ResetPasswordCommand(string Token, string NewPassword) : IRequest;

public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Token).MustBeToken();
        RuleFor(x => x.NewPassword).MustBeStrongPassword();
    }
}

public sealed class ResetPasswordHandler(
    IAppDbContext db,
    ITenantScope tenantScope,
    IUserTokenService tokens,
    IPasswordService passwords,
    ISessionRevocations revocations,
    IAuditLogger audit,
    TimeProvider clock) : IRequestHandler<ResetPasswordCommand>
{
    public async Task Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var token = await tokens.FindUsableAsync(command.Token, UserTokenPurpose.PasswordReset, cancellationToken)
                    ?? throw Failures.InvalidLink();
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == token.UserId, cancellationToken)
                   ?? throw Failures.InvalidLink();

        tenantScope.UseScopeOf(user.TenantId);

        token.Consume(now);
        user.SetPassword(passwords.Hash(command.NewPassword));
        user.ConfirmEmail(); // following the emailed link proves control of the mailbox

        // A reset means the old credentials may be compromised: sign out everywhere.
        var revoked = await db.RevokeAllSessionsAsync(user.Id, "password_reset", now, except: null, cancellationToken);

        audit.Record(new AuditEntry(
            AuditActions.PasswordReset, nameof(User), user.Id.ToString(),
            NewValues: new { SessionsRevoked = revoked.Count },
            TenantId: user.TenantId, UserId: user.Id, UserEmail: user.Email));
        await db.SaveChangesAsync(cancellationToken);
        await revocations.RevokeAsync(revoked, cancellationToken);
    }
}

public sealed record AcceptInvitationCommand(string Token, string Password, string? FirstName, string? LastName) : IRequest;

public sealed class AcceptInvitationValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationValidator()
    {
        RuleFor(x => x.Token).MustBeToken();
        RuleFor(x => x.Password).MustBeStrongPassword();
        RuleFor(x => x.FirstName).MaximumLength(100);
        RuleFor(x => x.LastName).MaximumLength(100);
    }
}

public sealed class AcceptInvitationHandler(
    IAppDbContext db,
    ITenantScope tenantScope,
    IUserTokenService tokens,
    IPasswordService passwords,
    IAuditLogger audit,
    TimeProvider clock) : IRequestHandler<AcceptInvitationCommand>
{
    public async Task Handle(AcceptInvitationCommand command, CancellationToken cancellationToken)
    {
        var token = await tokens.FindUsableAsync(command.Token, UserTokenPurpose.Invitation, cancellationToken)
                    ?? throw Failures.InvalidLink();
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == token.UserId, cancellationToken)
                   ?? throw Failures.InvalidLink();

        if (!user.IsActive)
        {
            throw Failures.InvalidLink();
        }

        tenantScope.UseScopeOf(user.TenantId);

        token.Consume(clock.GetUtcNow());
        user.AcceptInvitation(passwords.Hash(command.Password), command.FirstName, command.LastName);

        audit.Record(new AuditEntry(
            AuditActions.UserInvitationAccepted, nameof(User), user.Id.ToString(),
            TenantId: user.TenantId, UserId: user.Id, UserEmail: user.Email));
        await db.SaveChangesAsync(cancellationToken);
    }
}
