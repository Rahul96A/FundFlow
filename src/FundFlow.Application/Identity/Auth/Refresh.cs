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

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<AuthResult>;

public sealed class RefreshTokenValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
    }
}

/// <summary>
/// Exchanges a refresh token for a new access token and a <em>new</em> refresh token (rotation). Presenting a token
/// that was already rotated away is treated as token theft and revokes the whole session.
/// </summary>
public sealed class RefreshTokenHandler(
    IAppDbContext db,
    ITenantScope tenantScope,
    ISecretTokens secrets,
    ITokenService tokenService,
    ISessionRevocations revocations,
    IAuditLogger audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<AuthOptions> authOptions) : IRequestHandler<RefreshTokenCommand, AuthResult>
{
    private static UnauthorizedException Rejected() =>
        new("Your session has expired. Sign in again.", "invalid_refresh_token");

    public async Task<AuthResult> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var options = authOptions.Value;
        var now = clock.GetUtcNow();
        var presentedHash = secrets.Hash(command.RefreshToken);

        var session = await db.UserSessions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                s => s.RefreshTokenHash == presentedHash || s.PreviousRefreshTokenHash == presentedHash,
                cancellationToken);
        if (session is null)
        {
            throw Rejected();
        }

        var user = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == session.UserId, cancellationToken);
        if (user is null)
        {
            throw Rejected();
        }

        tenantScope.UseScopeOf(session.TenantId);

        var newRefreshToken = secrets.Generate();
        var outcome = session.Rotate(
            presentedHash,
            secrets.Hash(newRefreshToken),
            now,
            options.RefreshSlidingLifetime,
            options.RefreshReuseGrace,
            currentUser.IpAddress,
            currentUser.UserAgent);

        switch (outcome)
        {
            case RefreshOutcome.Rotated:
                break;

            case RefreshOutcome.ReuseDetected:
                audit.Record(new AuditEntry(
                    AuditActions.TokenReuseDetected,
                    nameof(UserSession),
                    session.Id.ToString(),
                    NewValues: new { session.IpAddress, session.UserAgent },
                    TenantId: session.TenantId,
                    UserId: user.Id,
                    UserEmail: user.Email));
                await db.SaveChangesAsync(cancellationToken);
                await revocations.RevokeAsync([session.Id], cancellationToken);
                AppMetrics.RecordTokenReuse();
                throw Rejected();

            default:
                // Revoked, expired, or a benign race between two tabs: nothing to persist, nothing to reveal.
                throw Rejected();
        }

        if (!await IsUserAllowedAsync(user, options, cancellationToken))
        {
            session.Revoke("account_state_changed", now);
            await db.SaveChangesAsync(cancellationToken);
            await revocations.RevokeAsync([session.Id], cancellationToken);
            throw Rejected();
        }

        var roles = await db.RoleNamesAsync(user.Id, cancellationToken);
        var accessToken = tokenService.CreateAccessToken(
            new AccessTokenRequest(user.Id, user.TenantId, session.Id, user.Email, user.FullName, roles));

        await db.SaveChangesAsync(cancellationToken);

        return new AuthResult(accessToken.Value, accessToken.ExpiresAt, newRefreshToken, session.ExpiresAt);
    }

    private async Task<bool> IsUserAllowedAsync(User user, AuthOptions options, CancellationToken cancellationToken)
    {
        if (!user.IsActive || (options.RequireConfirmedEmail && !user.EmailConfirmed))
        {
            return false;
        }

        if (user.TenantId is not { } tenantId)
        {
            return true;
        }

        var status = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == tenantId)
            .Select(o => o.Status)
            .FirstOrDefaultAsync(cancellationToken);
        return status == OrganizationStatus.Active;
    }
}
