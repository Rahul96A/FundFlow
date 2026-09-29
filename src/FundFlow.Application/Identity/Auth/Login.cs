using FluentValidation;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Exceptions;
using FundFlow.Application.Common.Options;
using FundFlow.Application.Common.Telemetry;
using FundFlow.Domain.Audit;
using FundFlow.Domain.Identity;
using FundFlow.Domain.Organizations;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FundFlow.Application.Identity.Auth;

/// <summary>Outcome of login/refresh. The raw refresh token leaves the API only inside an HttpOnly cookie.</summary>
public sealed record AuthResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record LoginCommand(string Email, string Password) : IRequest<AuthResult>;

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        // Deliberately shallow: password rules are for choosing a password, not for checking one.
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}

public sealed class LoginHandler(
    IAppDbContext db,
    ITenantScope tenantScope,
    IPasswordService passwords,
    ISecretTokens secrets,
    ITokenService tokenService,
    IAuditLogger audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<AuthOptions> authOptions) : IRequestHandler<LoginCommand, AuthResult>
{
    private static UnauthorizedException InvalidCredentials() =>
        new("Invalid email or password.", "invalid_credentials");

    public async Task<AuthResult> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var options = authOptions.Value;
        var now = clock.GetUtcNow();
        var email = command.Email.Trim();
        var normalized = User.NormalizeEmail(email);

        // Login is the one flow that has to find the user before it knows the tenant.
        var user = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);

        if (user is null)
        {
            passwords.SimulateVerification(command.Password);
            audit.Record(new AuditEntry(
                AuditActions.LoginFailed,
                nameof(User),
                NewValues: new { Email = email, Reason = "unknown_account" },
                UserEmail: email));
            await db.SaveChangesAsync(cancellationToken);
            AppMetrics.RecordLogin("unknown_account");
            throw InvalidCredentials();
        }

        tenantScope.UseScopeOf(user.TenantId);

        if (user.IsLockedOut(now))
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((user.LockoutEnd!.Value - now).TotalMinutes));
            audit.Record(new AuditEntry(
                AuditActions.LoginFailed,
                nameof(User),
                user.Id.ToString(),
                NewValues: new { Reason = "locked_out" },
                TenantId: user.TenantId,
                UserId: user.Id,
                UserEmail: user.Email));
            await db.SaveChangesAsync(cancellationToken);
            AppMetrics.RecordLogin("locked_out");
            throw new UnauthorizedException(
                $"Too many failed sign-in attempts. Try again in {minutes} minute{(minutes == 1 ? string.Empty : "s")}.",
                "account_locked");
        }

        var verification = PasswordVerification.Failed;
        if (user.PasswordHash is null)
        {
            passwords.SimulateVerification(command.Password);
        }
        else
        {
            verification = passwords.Verify(user.PasswordHash, command.Password);
        }

        if (verification == PasswordVerification.Failed)
        {
            var lockedNow = user.RegisterFailedLogin(now, new LockoutPolicy(options.MaxFailedAccessAttempts, TimeSpan.FromMinutes(options.LockoutMinutes)));
            audit.Record(new AuditEntry(
                AuditActions.LoginFailed,
                nameof(User),
                user.Id.ToString(),
                NewValues: new { Reason = "invalid_password" },
                TenantId: user.TenantId,
                UserId: user.Id,
                UserEmail: user.Email));
            if (lockedNow)
            {
                audit.Record(new AuditEntry(
                    AuditActions.AccountLocked,
                    nameof(User),
                    user.Id.ToString(),
                    NewValues: new { LockoutEnd = user.LockoutEnd },
                    TenantId: user.TenantId,
                    UserId: user.Id,
                    UserEmail: user.Email));
            }

            await db.SaveChangesAsync(cancellationToken);
            AppMetrics.RecordLogin("invalid_password");
            throw InvalidCredentials();
        }

        // The password is correct from here on, so it is safe to be specific about why sign-in is refused.
        if (!user.IsActive)
        {
            await RecordRefusalAsync(user, "deactivated", cancellationToken);
            AppMetrics.RecordLogin("deactivated");
            throw new UnauthorizedException("This account has been deactivated. Contact your administrator.", "account_disabled");
        }

        if (user.TenantId is { } tenantId)
        {
            var status = await db.Organizations.AsNoTracking()
                .Where(o => o.Id == tenantId)
                .Select(o => o.Status)
                .FirstOrDefaultAsync(cancellationToken);
            if (status != OrganizationStatus.Active)
            {
                await RecordRefusalAsync(user, "organization_suspended", cancellationToken);
                AppMetrics.RecordLogin("organization_suspended");
                throw new ForbiddenException("This organization has been suspended. Contact support.", "organization_suspended");
            }
        }

        if (options.RequireConfirmedEmail && !user.EmailConfirmed)
        {
            await RecordRefusalAsync(user, "email_not_verified", cancellationToken);
            AppMetrics.RecordLogin("email_not_verified");
            throw new UnauthorizedException("Verify your email address before signing in. Check your inbox for the link.", "email_not_verified");
        }

        if (verification == PasswordVerification.SuccessRehashNeeded)
        {
            user.SetPassword(passwords.Hash(command.Password));
        }

        user.RegisterSuccessfulLogin(now);

        var refreshToken = secrets.Generate();
        var session = UserSession.Start(
            user,
            secrets.Hash(refreshToken),
            now,
            options.RefreshSlidingLifetime,
            options.RefreshAbsoluteLifetime,
            currentUser.IpAddress,
            currentUser.UserAgent);
        db.UserSessions.Add(session);

        var roles = await db.RoleNamesAsync(user.Id, cancellationToken);
        var accessToken = tokenService.CreateAccessToken(
            new AccessTokenRequest(user.Id, user.TenantId, session.Id, user.Email, user.FullName, roles));

        audit.Record(new AuditEntry(
            AuditActions.Login,
            nameof(User),
            user.Id.ToString(),
            NewValues: new { SessionId = session.Id },
            TenantId: user.TenantId,
            UserId: user.Id,
            UserEmail: user.Email));

        await db.SaveChangesAsync(cancellationToken);
        AppMetrics.RecordLogin("success");

        return new AuthResult(accessToken.Value, accessToken.ExpiresAt, refreshToken, session.ExpiresAt);
    }

    private async Task RecordRefusalAsync(User user, string reason, CancellationToken cancellationToken)
    {
        audit.Record(new AuditEntry(
            AuditActions.LoginFailed,
            nameof(User),
            user.Id.ToString(),
            NewValues: new { Reason = reason },
            TenantId: user.TenantId,
            UserId: user.Id,
            UserEmail: user.Email));
        await db.SaveChangesAsync(cancellationToken);
    }
}
