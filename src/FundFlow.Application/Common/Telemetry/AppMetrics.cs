using System.Diagnostics.Metrics;

namespace FundFlow.Application.Common.Telemetry;

/// <summary>
/// Business and operational metrics, exported through OpenTelemetry (meter "FundFlow"). Framework metrics (HTTP request
/// duration, runtime, SQL client) come from their own instrumentation; these cover what only the application knows.
/// Tags are low-cardinality on purpose (never ids, emails or tenant names).
/// </summary>
public static class AppMetrics
{
    public const string MeterName = "FundFlow";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> Logins = Meter.CreateCounter<long>(
        "fundflow.auth.logins", description: "Sign-in attempts by outcome.");

    private static readonly Counter<long> TokenReuse = Meter.CreateCounter<long>(
        "fundflow.auth.refresh_token_reuse", description: "Rotated refresh tokens replayed after the grace period (possible theft).");

    private static readonly Counter<long> Exceptions = Meter.CreateCounter<long>(
        "fundflow.exceptions", description: "Exceptions turned into HTTP error responses, by type and status.");

    private static readonly Counter<long> Emails = Meter.CreateCounter<long>(
        "fundflow.email.processed", description: "System emails handled by the consumer, by outcome.");

    private static readonly Histogram<double> EmailDuration = Meter.CreateHistogram<double>(
        "fundflow.email.duration", unit: "ms", description: "Time to hand an email to the SMTP relay.");

    private static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>(
        "fundflow.job.duration", unit: "ms", description: "Background job execution time, by job.");

    /// <param name="result">success, invalid_password, unknown_account, locked_out, deactivated, organization_suspended, email_not_verified</param>
    public static void RecordLogin(string result) =>
        Logins.Add(1, new KeyValuePair<string, object?>("result", result));

    public static void RecordTokenReuse() => TokenReuse.Add(1);

    public static void RecordException(string exceptionType, int statusCode) =>
        Exceptions.Add(
            1,
            new KeyValuePair<string, object?>("exception", exceptionType),
            new KeyValuePair<string, object?>("status", statusCode));

    public static void RecordEmail(string outcome, double durationMs)
    {
        Emails.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        EmailDuration.Record(durationMs, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public static void RecordJob(string job, double durationMs) =>
        JobDuration.Record(durationMs, new KeyValuePair<string, object?>("job", job));
}
