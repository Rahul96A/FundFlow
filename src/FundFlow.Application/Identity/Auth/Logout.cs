using FundFlow.Application.Common.Abstractions;
using FundFlow.Domain.Audit;
using FundFlow.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FundFlow.Application.Identity.Auth;

/// <summary>
/// Ends the caller's session. The session is identified by the refresh-token cookie and, when the request is
/// authenticated, by the access token's <c>sid</c> claim. Logging out twice is harmless.
/// </summary>
public sealed record LogoutCommand(string? RefreshToken) : IRequest;

public sealed class LogoutHandler(
    IAppDbContext db,
    ITenantScope tenantScope,
    ISecretTokens secrets,
    ISessionRevocations revocations,
    IAuditLogger audit,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var sessions = new Dictionary<Guid, UserSession>();

        if (!string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            var hash = secrets.Hash(command.RefreshToken);
            var byToken = await db.UserSessions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.RefreshTokenHash == hash || s.PreviousRefreshTokenHash == hash, cancellationToken);
            if (byToken is not null)
            {
                sessions[byToken.Id] = byToken;
            }
        }

        if (currentUser.SessionId is { } sessionId && !sessions.ContainsKey(sessionId))
        {
            var bySid = await db.UserSessions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == currentUser.UserId, cancellationToken);
            if (bySid is not null)
            {
                sessions[bySid.Id] = bySid;
            }
        }

        var revoked = new List<Guid>();
        foreach (var session in sessions.Values.Where(s => !s.IsRevoked))
        {
            tenantScope.UseScopeOf(session.TenantId);
            session.Revoke("logout", now);
            revoked.Add(session.Id);

            var email = await db.Users.IgnoreQueryFilters()
                .Where(u => u.Id == session.UserId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken);

            audit.Record(new AuditEntry(
                AuditActions.Logout,
                nameof(User),
                session.UserId.ToString(),
                NewValues: new { SessionId = session.Id },
                TenantId: session.TenantId,
                UserId: session.UserId,
                UserEmail: email));
        }

        if (revoked.Count == 0)
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
        await revocations.RevokeAsync(revoked, cancellationToken);
    }
}
