using FundFlow.Api.IntegrationTests.Infrastructure;
using FundFlow.Domain.Identity;
using FundFlow.Infrastructure.BackgroundJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FundFlow.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class MaintenanceJobTests(ApiFixture fixture)
{
    private readonly TestKit _kit = new(fixture.Factory);

    private static readonly TimeSpan Sliding = TimeSpan.FromDays(14);
    private static readonly TimeSpan Absolute = TimeSpan.FromDays(30);

    [Fact]
    public async Task Session_cleanup_removes_only_long_dead_rows_and_can_safely_run_twice()
    {
        var org = await _kit.CreateOrganizationAsync();
        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N");

        var ids = await _kit.WithDbAsync(org.Id, async (db, _) =>
        {
            var user = await db.Users.FirstAsync();

            // Sessions
            var longExpired = UserSession.Start(user, $"long-expired-{suffix}", now.AddDays(-90), Sliding, Absolute, null, null);      // expired ~76 days ago
            var revokedLongAgo = UserSession.Start(user, $"revoked-old-{suffix}", now.AddDays(-40), Sliding, Absolute, null, null);
            revokedLongAgo.Revoke("logout", now.AddDays(-35));
            var revokedRecently = UserSession.Start(user, $"revoked-new-{suffix}", now.AddDays(-5), Sliding, Absolute, null, null);
            revokedRecently.Revoke("logout", now.AddDays(-4));                                                                         // kept for investigations
            var live = UserSession.Start(user, $"live-{suffix}", now, Sliding, Absolute, null, null);

            // Email tokens
            var expiredToken = UserToken.Issue(user.Id, org.Id, UserTokenPurpose.PasswordReset, $"tok-expired-{suffix}", now.AddDays(-60), TimeSpan.FromHours(1));
            var spentLongAgo = UserToken.Issue(user.Id, org.Id, UserTokenPurpose.EmailVerification, $"tok-spent-{suffix}", now.AddDays(-45), TimeSpan.FromDays(2));
            spentLongAgo.Consume(now.AddDays(-44));
            var pending = UserToken.Issue(user.Id, org.Id, UserTokenPurpose.Invitation, $"tok-pending-{suffix}", now, TimeSpan.FromDays(7));

            db.UserSessions.AddRange(longExpired, revokedLongAgo, revokedRecently, live);
            db.UserTokens.AddRange(expiredToken, spentLongAgo, pending);
            await db.SaveChangesAsync();

            return new
            {
                Removed = new[] { longExpired.Id, revokedLongAgo.Id },
                Kept = new[] { revokedRecently.Id, live.Id },
                RemovedTokens = new[] { expiredToken.Id, spentLongAgo.Id },
                KeptTokens = new[] { pending.Id },
            };
        });

        async Task RunJobAsync()
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CleanupExpiredSessionsJob>().RunAsync(CancellationToken.None);
        }

        async Task<(List<Guid> Sessions, List<Guid> Tokens)> SurvivorsAsync() =>
            await _kit.WithDbAsync(org.Id, async (db, _) => (
                await db.UserSessions.IgnoreQueryFilters().Where(s => ids.Removed.Concat(ids.Kept).Contains(s.Id)).Select(s => s.Id).ToListAsync(),
                await db.UserTokens.IgnoreQueryFilters().Where(t => ids.RemovedTokens.Concat(ids.KeptTokens).Contains(t.Id)).Select(t => t.Id).ToListAsync()));

        await RunJobAsync();
        var afterFirst = await SurvivorsAsync();
        await RunJobAsync(); // idempotent: nothing more to remove, nothing breaks
        var afterSecond = await SurvivorsAsync();

        afterFirst.Sessions.Should().BeEquivalentTo(ids.Kept, "recently dead and live sessions are retained");
        afterFirst.Tokens.Should().BeEquivalentTo(ids.KeptTokens, "unspent, unexpired tokens are retained");
        afterSecond.Should().BeEquivalentTo(afterFirst);
    }
}
