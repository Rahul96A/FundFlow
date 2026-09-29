using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Infrastructure.Email;

namespace FundFlow.Api.IntegrationTests.Infrastructure;

/// <summary>Stands in for SMTP: records every email the consumer would have sent.</summary>
public sealed partial class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyCollection<EmailMessage> Sent => _sent.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Waits for an email to <paramref name="to"/> whose subject contains <paramref name="subjectPart"/>. Delivery is
    /// asynchronous (outbox → bus → consumer), which is precisely what these tests exercise.
    /// </summary>
    public async Task<EmailMessage> WaitForAsync(string to, string subjectPart, int skip = 0, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < deadline)
        {
            var matches = _sent
                .Where(m => string.Equals(m.ToAddress, to, StringComparison.OrdinalIgnoreCase)
                            && m.Subject.Contains(subjectPart, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count > skip)
            {
                return matches[skip];
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"No email to {to} with subject containing '{subjectPart}' arrived. Sent so far: " +
                                   string.Join("; ", _sent.Select(m => $"{m.ToAddress}: {m.Subject}")));
    }

    public int CountFor(string to) =>
        _sent.Count(m => string.Equals(m.ToAddress, to, StringComparison.OrdinalIgnoreCase));

    /// <summary>Pulls the one-time token out of the link in an email body.</summary>
    public static string ExtractToken(EmailMessage message)
    {
        var match = TokenPattern().Match(message.TextBody);
        if (!match.Success)
        {
            throw new InvalidOperationException($"No token link found in email '{message.Subject}': {message.TextBody}");
        }

        return Uri.UnescapeDataString(match.Groups[1].Value);
    }

    [GeneratedRegex(@"token=([A-Za-z0-9_%\-\.]+)")]
    private static partial Regex TokenPattern();
}
