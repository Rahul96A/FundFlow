using System.Diagnostics;
using FundFlow.Application.Common.Telemetry;
using FundFlow.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FundFlow.Infrastructure.BackgroundJobs;

/// <summary>
/// Housekeeping run by Hangfire. Every job is idempotent: running it twice (or concurrently after a retry) leaves
/// the same end state, because each one is just "delete rows older than X".
/// </summary>
public sealed class CleanupExpiredSessionsJob(AppDbContext db, TimeProvider clock, ILogger<CleanupExpiredSessionsJob> logger)
{
    public const string JobId = "cleanup-expired-sessions";

    /// <summary>How long dead sessions and used/expired email tokens are kept for investigations.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 3)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var cutoff = clock.GetUtcNow().Subtract(Retention);

        var sessions = await db.UserSessions.IgnoreQueryFilters()
            .Where(s => (s.RevokedAt != null && s.RevokedAt < cutoff) || s.AbsoluteExpiresAt < cutoff || s.ExpiresAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var tokens = await db.UserTokens.IgnoreQueryFilters()
            .Where(t => t.ExpiresAt < cutoff || (t.ConsumedAt != null && t.ConsumedAt < cutoff))
            .ExecuteDeleteAsync(cancellationToken);

        AppMetrics.RecordJob(JobId, stopwatch.Elapsed.TotalMilliseconds);
        logger.LogInformation("Cleanup removed {Sessions} dead sessions and {Tokens} spent email tokens", sessions, tokens);
    }
}
