using FundFlow.Domain.SharedKernel;

namespace FundFlow.Domain.Identity;

public enum RefreshOutcome
{
    /// <summary>The presented token was current; the session now holds the new token.</summary>
    Rotated = 1,

    /// <summary>The session was already revoked.</summary>
    Revoked = 2,

    /// <summary>The session passed its (sliding or absolute) lifetime.</summary>
    Expired = 3,

    /// <summary>
    /// The presented token was the previous one, used moments after rotation (e.g. two browser tabs racing).
    /// Rejected without revoking the session.
    /// </summary>
    ReuseWithinGrace = 4,

    /// <summary>A previously-rotated token was replayed after the grace period: treated as theft, session revoked.</summary>
    ReuseDetected = 5,
}

/// <summary>
/// One signed-in device/browser. Holds the hash of the current opaque refresh token; refresh tokens rotate on every
/// use and replay of a rotated token revokes the whole session. Doubles as the "active sessions" list in the UI.
/// </summary>
public sealed class UserSession : Entity, IOptionalTenantEntity
{
    private UserSession()
    {
    }

    public Guid? TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string RefreshTokenHash { get; private set; } = default!;
    public string? PreviousRefreshTokenHash { get; private set; }
    public DateTimeOffset? RotatedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>Sliding expiry, pushed forward on each refresh but never beyond <see cref="AbsoluteExpiresAt"/>.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset AbsoluteExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }

    /// <summary>Concurrency token: two simultaneous refreshes of one session cannot both succeed.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public bool IsRevoked => RevokedAt is not null;

    public bool IsActive(DateTimeOffset now) => !IsRevoked && now < ExpiresAt;

    public static UserSession Start(
        User user,
        string refreshTokenHash,
        DateTimeOffset now,
        TimeSpan slidingLifetime,
        TimeSpan absoluteLifetime,
        string? ipAddress,
        string? userAgent) =>
        new()
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            RefreshTokenHash = refreshTokenHash,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now.Add(slidingLifetime),
            AbsoluteExpiresAt = now.Add(absoluteLifetime),
            IpAddress = Truncate(ipAddress, 64),
            UserAgent = Truncate(userAgent, 512),
        };

    public RefreshOutcome Rotate(
        string presentedHash,
        string newHash,
        DateTimeOffset now,
        TimeSpan slidingLifetime,
        TimeSpan reuseGracePeriod,
        string? ipAddress,
        string? userAgent)
    {
        if (IsRevoked)
        {
            return RefreshOutcome.Revoked;
        }

        if (now >= ExpiresAt || now >= AbsoluteExpiresAt)
        {
            return RefreshOutcome.Expired;
        }

        if (string.Equals(presentedHash, RefreshTokenHash, StringComparison.Ordinal))
        {
            PreviousRefreshTokenHash = RefreshTokenHash;
            RefreshTokenHash = newHash;
            RotatedAt = now;
            LastSeenAt = now;
            var slid = now.Add(slidingLifetime);
            ExpiresAt = slid < AbsoluteExpiresAt ? slid : AbsoluteExpiresAt;
            IpAddress = Truncate(ipAddress, 64) ?? IpAddress;
            UserAgent = Truncate(userAgent, 512) ?? UserAgent;
            return RefreshOutcome.Rotated;
        }

        if (PreviousRefreshTokenHash is not null
            && string.Equals(presentedHash, PreviousRefreshTokenHash, StringComparison.Ordinal))
        {
            if (RotatedAt is { } rotatedAt && now - rotatedAt <= reuseGracePeriod)
            {
                return RefreshOutcome.ReuseWithinGrace;
            }

            Revoke("refresh_token_reuse", now);
            return RefreshOutcome.ReuseDetected;
        }

        // A hash that matches neither slot cannot have been looked up to reach this session.
        throw new DomainException("session.token_mismatch", "The refresh token does not belong to this session.");
    }

    public void Revoke(string reason, DateTimeOffset now)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = Truncate(reason, 100);
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
