using System.Diagnostics;
using System.Text.Json;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Telemetry;
using FundFlow.Infrastructure.Email;
using MassTransit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace FundFlow.Infrastructure.Messaging;

/// <summary>
/// Message carried through the bus/outbox for a system email. The body contains one-time links, so it travels
/// (and rests in the outbox table and broker) encrypted with ASP.NET Core Data Protection.
/// </summary>
public sealed record SendTransactionalEmailMessage(string ProtectedPayload);

public interface IEmailPayloadProtector
{
    string Protect(EmailMessage message);

    EmailMessage Unprotect(string protectedPayload);
}

public sealed class EmailPayloadProtector(IDataProtectionProvider provider) : IEmailPayloadProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("FundFlow.TransactionalEmail.v1");

    public string Protect(EmailMessage message) =>
        _protector.Protect(JsonSerializer.Serialize(message));

    public EmailMessage Unprotect(string protectedPayload) =>
        JsonSerializer.Deserialize<EmailMessage>(_protector.Unprotect(protectedPayload))
        ?? throw new InvalidOperationException("Email payload was empty.");
}

/// <summary>
/// Queues system email through the transactional outbox: the row is written by the same <c>SaveChanges</c> as the
/// business change, then delivered to the broker asynchronously. No request ever waits on SMTP.
/// </summary>
public sealed class OutboxEmailQueue(IPublishEndpoint publish, IEmailPayloadProtector protector) : IEmailQueue
{
    public Task EnqueueAsync(EmailMessage message, CancellationToken cancellationToken) =>
        publish.Publish(new SendTransactionalEmailMessage(protector.Protect(message)), cancellationToken);
}

public sealed class OutboxIntegrationEventPublisher(IPublishEndpoint publish) : IIntegrationEventPublisher
{
    public Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken)
        where T : class =>
        publish.Publish(integrationEvent, cancellationToken);
}

public sealed class SendTransactionalEmailConsumer(
    IEmailPayloadProtector protector,
    IEmailSender sender,
    ILogger<SendTransactionalEmailConsumer> logger) : IConsumer<SendTransactionalEmailMessage>
{
    public async Task Consume(ConsumeContext<SendTransactionalEmailMessage> context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var message = protector.Unprotect(context.Message.ProtectedPayload);
            await sender.SendAsync(message, context.CancellationToken);
            AppMetrics.RecordEmail("sent", stopwatch.Elapsed.TotalMilliseconds);
            logger.LogInformation("Sent system email {Subject} (message {MessageId})", message.Subject, context.MessageId);
        }
        catch
        {
            // The exception propagates so MassTransit retries with back-off; the metric makes the failure rate visible.
            AppMetrics.RecordEmail("failed", stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
    }
}
