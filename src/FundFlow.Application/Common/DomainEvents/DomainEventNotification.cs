using FundFlow.Application.Common.Abstractions;
using FundFlow.Domain.SharedKernel;
using MediatR;

namespace FundFlow.Application.Common.DomainEvents;

/// <summary>
/// Adapts a pure-domain event to a MediatR notification so the Domain project stays free of framework references.
/// Handlers implement <c>INotificationHandler&lt;DomainEventNotification&lt;TEvent&gt;&gt;</c>.
/// </summary>
public sealed record DomainEventNotification<TEvent>(TEvent DomainEvent) : INotification
    where TEvent : IDomainEvent;

/// <summary>
/// Publishes domain events in-process. It runs <em>inside</em> the unit of work, before the commit, so handlers may
/// add rows and outbox messages that commit atomically with the change that raised the event. Handlers must
/// therefore be free of external I/O (no HTTP/SMTP calls); side effects go through the outbox.
/// </summary>
public sealed class MediatRDomainEventDispatcher(IPublisher publisher) : IDomainEventDispatcher
{
    public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var notificationType = typeof(DomainEventNotification<>).MakeGenericType(domainEvent.GetType());
        var notification = Activator.CreateInstance(notificationType, domainEvent)!;
        return publisher.Publish(notification, cancellationToken);
    }
}
