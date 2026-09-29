namespace FundFlow.Contracts.IntegrationEvents;

/// <summary>
/// Integration events cross module (and eventually service) boundaries through the message bus.
/// They are delivered at-least-once via the transactional outbox, so consumers must be idempotent.
/// </summary>
public sealed record UserCreatedIntegrationEvent(
    Guid UserId,
    Guid? TenantId,
    string Email,
    DateTimeOffset OccurredAt);

public sealed record OrganizationRegisteredIntegrationEvent(
    Guid OrganizationId,
    string Slug,
    string Name,
    DateTimeOffset OccurredAt);

public sealed record OrganizationSuspendedIntegrationEvent(
    Guid OrganizationId,
    string Reason,
    DateTimeOffset OccurredAt);
