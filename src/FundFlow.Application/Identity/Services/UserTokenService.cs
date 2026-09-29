using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Options;
using FundFlow.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FundFlow.Application.Identity.Services;

/// <summary>Issues and redeems the single-use links sent by email (verification, password reset, invitation).</summary>
public interface IUserTokenService
{
    /// <summary>
    /// Creates a token for the user, superseding any earlier unused token of the same purpose, and returns the raw
    /// secret to embed in the email. Only the hash is stored. The new row is staged, not saved.
    /// </summary>
    Task<string> IssueAsync(Guid userId, Guid? tenantId, UserTokenPurpose purpose, CancellationToken cancellationToken);

    /// <summary>Finds a still-usable token by its raw value, regardless of tenant (the caller is anonymous).</summary>
    Task<UserToken?> FindUsableAsync(string rawToken, UserTokenPurpose purpose, CancellationToken cancellationToken);
}

public sealed class UserTokenService(
    IAppDbContext db,
    ISecretTokens secrets,
    TimeProvider clock,
    IOptions<AuthOptions> authOptions) : IUserTokenService
{
    public async Task<string> IssueAsync(
        Guid userId,
        Guid? tenantId,
        UserTokenPurpose purpose,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var outstanding = await db.UserTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == userId && t.Purpose == purpose && t.ConsumedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var previous in outstanding)
        {
            previous.Supersede(now);
        }

        var raw = secrets.Generate();
        db.UserTokens.Add(UserToken.Issue(userId, tenantId, purpose, secrets.Hash(raw), now, LifetimeFor(purpose)));
        return raw;
    }

    public async Task<UserToken?> FindUsableAsync(
        string rawToken,
        UserTokenPurpose purpose,
        CancellationToken cancellationToken)
    {
        var hash = secrets.Hash(rawToken);
        var token = await db.UserTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose, cancellationToken);

        return token is not null && token.IsUsable(clock.GetUtcNow()) ? token : null;
    }

    private TimeSpan LifetimeFor(UserTokenPurpose purpose)
    {
        var options = authOptions.Value;
        return purpose switch
        {
            UserTokenPurpose.EmailVerification => TimeSpan.FromHours(options.EmailVerificationTokenHours),
            UserTokenPurpose.PasswordReset => TimeSpan.FromMinutes(options.PasswordResetTokenMinutes),
            UserTokenPurpose.Invitation => TimeSpan.FromDays(options.InvitationTokenDays),
            _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown token purpose."),
        };
    }
}
