using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.DomainEvents;
using FundFlow.Application.Common.Options;
using FundFlow.Application.Identity.Services;
using FundFlow.Application.Notifications;
using FundFlow.Contracts.IntegrationEvents;
using FundFlow.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FundFlow.Application.Identity.EventHandlers;

// These handlers run inside the unit of work (before commit). They only stage rows and outbox messages;
// nothing here talks to the outside world directly, so a failed commit sends nothing.

public sealed class SendVerificationEmailHandler(
    IUserTokenService tokens,
    IEmailQueue emailQueue,
    IOptions<AppOptions> app,
    IOptions<AuthOptions> auth)
    : INotificationHandler<DomainEventNotification<EmailVerificationRequestedDomainEvent>>
{
    public async Task Handle(DomainEventNotification<EmailVerificationRequestedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        var raw = await tokens.IssueAsync(e.UserId, e.TenantId, UserTokenPurpose.EmailVerification, cancellationToken);
        var link = app.Value.BuildLink("verify-email", ("token", raw));
        await emailQueue.EnqueueAsync(
            TransactionalEmails.EmailVerification(e.Email, e.FirstName, link, app.Value.ProductName, auth.Value.EmailVerificationTokenHours),
            cancellationToken);
    }
}

public sealed class SendPasswordResetEmailHandler(
    IUserTokenService tokens,
    IEmailQueue emailQueue,
    IOptions<AppOptions> app,
    IOptions<AuthOptions> auth)
    : INotificationHandler<DomainEventNotification<PasswordResetRequestedDomainEvent>>
{
    public async Task Handle(DomainEventNotification<PasswordResetRequestedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        var raw = await tokens.IssueAsync(e.UserId, e.TenantId, UserTokenPurpose.PasswordReset, cancellationToken);
        var link = app.Value.BuildLink("reset-password", ("token", raw));
        await emailQueue.EnqueueAsync(
            TransactionalEmails.PasswordReset(e.Email, e.FirstName, link, app.Value.ProductName, auth.Value.PasswordResetTokenMinutes),
            cancellationToken);
    }
}

public sealed class SendInvitationEmailHandler(
    IAppDbContext db,
    IUserTokenService tokens,
    IEmailQueue emailQueue,
    IOptions<AppOptions> app,
    IOptions<AuthOptions> auth)
    : INotificationHandler<DomainEventNotification<UserInvitedDomainEvent>>
{
    public async Task Handle(DomainEventNotification<UserInvitedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        var organizationName = e.TenantId is { } tenantId
            ? await db.Organizations.AsNoTracking()
                .Where(o => o.Id == tenantId)
                .Select(o => o.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? app.Value.ProductName
            : app.Value.ProductName;

        var raw = await tokens.IssueAsync(e.UserId, e.TenantId, UserTokenPurpose.Invitation, cancellationToken);
        var link = app.Value.BuildLink("accept-invitation", ("token", raw));
        await emailQueue.EnqueueAsync(
            TransactionalEmails.Invitation(e.Email, e.FirstName, organizationName, link, app.Value.ProductName, auth.Value.InvitationTokenDays),
            cancellationToken);
    }
}

public sealed class PublishUserCreatedHandler(IIntegrationEventPublisher publisher)
    : INotificationHandler<DomainEventNotification<UserCreatedDomainEvent>>
{
    public Task Handle(DomainEventNotification<UserCreatedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return publisher.PublishAsync(
            new UserCreatedIntegrationEvent(e.UserId, e.TenantId, e.Email, e.OccurredAt),
            cancellationToken);
    }
}
