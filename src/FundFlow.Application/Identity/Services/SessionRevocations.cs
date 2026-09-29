using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Options;
using FundFlow.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FundFlow.Application.Identity.Services;

public sealed class SessionRevocations(ICacheService cache, IOptions<AuthOptions> authOptions) : ISessionRevocations
{
    private static string Key(Guid sessionId) => $"session:revoked:{sessionId:N}";

    public async Task RevokeAsync(IEnumerable<Guid> sessionIds, CancellationToken cancellationToken)
    {
        // An entry only has to outlive the longest-lived access token; after that the JWT itself has expired.
        var ttl = authOptions.Value.AccessTokenLifetime + TimeSpan.FromMinutes(5);
        foreach (var id in sessionIds.Distinct())
        {
            await cache.SetAsync(Key(id), new RevokedMarker(true), ttl, cancellationToken);
        }
    }

    public Task<bool> IsRevokedAsync(Guid sessionId, CancellationToken cancellationToken) =>
        cache.ExistsAsync(Key(sessionId), cancellationToken);

    private sealed record RevokedMarker(bool Revoked);
}

public static class SessionExtensions
{
    /// <summary>
    /// Revokes every active session of a user (optionally sparing one). Returns the revoked ids so the caller can
    /// also put them on the access-token deny-list. Staged, not saved.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> RevokeAllSessionsAsync(
        this IAppDbContext db,
        Guid userId,
        string reason,
        DateTimeOffset now,
        Guid? except,
        CancellationToken cancellationToken)
    {
        var sessions = await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.Id != except)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.Revoke(reason, now);
        }

        return sessions.Select(s => s.Id).ToArray();
    }
}
