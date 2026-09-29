using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.DomainEvents;
using FundFlow.Contracts.IntegrationEvents;
using FundFlow.Domain.Organizations;
using MediatR;

namespace FundFlow.Application.Organizations.EventHandlers;

public sealed class PublishOrganizationRegisteredHandler(IIntegrationEventPublisher publisher)
    : INotificationHandler<DomainEventNotification<OrganizationRegisteredDomainEvent>>
{
    public Task Handle(DomainEventNotification<OrganizationRegisteredDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return publisher.PublishAsync(
            new OrganizationRegisteredIntegrationEvent(e.OrganizationId, e.Slug, e.Name, e.OccurredAt),
            cancellationToken);
    }
}

public sealed class PublishOrganizationSuspendedHandler(IIntegrationEventPublisher publisher)
    : INotificationHandler<DomainEventNotification<OrganizationSuspendedDomainEvent>>
{
    public Task Handle(DomainEventNotification<OrganizationSuspendedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return publisher.PublishAsync(
            new OrganizationSuspendedIntegrationEvent(e.OrganizationId, e.Reason, e.OccurredAt),
            cancellationToken);
    }
}
