using FundFlow.Domain.Identity;
using FundFlow.Domain.SharedKernel;

namespace FundFlow.Application.Common.Abstractions;

public enum PasswordVerification
{
    Failed = 0,
    Success = 1,
    SuccessRehashNeeded = 2,
}

public interface IPasswordService
{
    string Hash(string password);

    PasswordVerification Verify(string hash, string password);

    /// <summary>
    /// Performs the same amount of work as a real verification. Called when the account does not exist so that
    /// response time does not reveal which emails are registered.
    /// </summary>
    void SimulateVerification(string password);
}

public sealed record AccessTokenRequest(
    Guid UserId,
    Guid? TenantId,
    Guid SessionId,
    string Email,
    string FullName,
    IReadOnlyCollection<string> Roles);

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(AccessTokenRequest request);
}

/// <summary>Opaque random secrets (refresh tokens, email links). Only hashes are ever persisted.</summary>
public interface ISecretTokens
{
    string Generate();

    string Hash(string token);
}

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
        where T : class;

    Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken)
        where T : class;

    Task RemoveAsync(string key, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);
}

public sealed record UserAccess(IReadOnlySet<string> Permissions, IReadOnlySet<string> Roles)
{
    public static UserAccess None { get; } = new(new HashSet<string>(), new HashSet<string>());

    public bool IsAdministrator => Roles.Any(SystemRoles.IsAdministrator);

    public bool Has(string permission) => Permissions.Contains(permission);
}

/// <summary>Resolves what a user may do. Cached; invalidated whenever roles or role permissions change.</summary>
public interface IUserAccessProvider
{
    Task<UserAccess> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task InvalidateAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken);
}

/// <summary>
/// Short-lived deny-list of session ids. Access tokens are self-contained JWTs, so revoking a session also has to
/// stop its still-valid access token; the JWT validator consults this list.
/// </summary>
public interface ISessionRevocations
{
    Task RevokeAsync(IEnumerable<Guid> sessionIds, CancellationToken cancellationToken);

    Task<bool> IsRevokedAsync(Guid sessionId, CancellationToken cancellationToken);
}

public sealed record EmailMessage(
    string ToAddress,
    string? ToName,
    string Subject,
    string HtmlBody,
    string TextBody);

/// <summary>
/// Queues an email for asynchronous delivery. Implementations enlist in the current unit of work (transactional
/// outbox): the message is only sent if the surrounding <c>SaveChanges</c> commits.
/// </summary>
public interface IEmailQueue
{
    Task EnqueueAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Publishes integration events to the message bus through the transactional outbox.</summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken)
        where T : class;
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken);
}
