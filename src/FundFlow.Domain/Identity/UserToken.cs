using FundFlow.Domain.SharedKernel;

namespace FundFlow.Domain.Identity;

public enum UserTokenPurpose
{
    EmailVerification = 1,
    PasswordReset = 2,
    Invitation = 3,
}

/// <summary>
/// A single-use, expiring secret sent by email (verification link, password reset, invitation). Only the SHA-256
/// hash is stored, so a database leak does not yield usable links.
/// </summary>
public sealed class UserToken : Entity, IOptionalTenantEntity
{
    private UserToken()
    {
    }

    public Guid? TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public UserTokenPurpose Purpose { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public static UserToken Issue(
        Guid userId,
        Guid? tenantId,
        UserTokenPurpose purpose,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan lifetime) =>
        new()
        {
            TenantId = tenantId,
            UserId = userId,
            Purpose = purpose,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
        };

    public bool IsUsable(DateTimeOffset now) => ConsumedAt is null && now < ExpiresAt;

    public void Consume(DateTimeOffset now)
    {
        if (!IsUsable(now))
        {
            throw new DomainException("token.invalid", "This link is invalid or has expired.");
        }

        ConsumedAt = now;
    }

    /// <summary>Invalidates the token without treating it as an error (used when a newer token supersedes it).</summary>
    public void Supersede(DateTimeOffset now) => ConsumedAt ??= now;
}
