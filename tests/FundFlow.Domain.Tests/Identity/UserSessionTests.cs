using FundFlow.Domain.Identity;
using FundFlow.Domain.SharedKernel;

namespace FundFlow.Domain.Tests.Identity;

public class UserSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Sliding = TimeSpan.FromDays(14);
    private static readonly TimeSpan Absolute = TimeSpan.FromDays(30);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(10);

    private static UserSession NewSession() =>
        UserSession.Start(
            User.Provision(Guid.NewGuid(), "u@example.org", "U", "Ser", "hash"),
            "hash-1", Start, Sliding, Absolute, "203.0.113.7", "Mozilla/5.0");

    [Fact]
    public void A_current_token_rotates_and_pushes_the_sliding_expiry_forward()
    {
        var session = NewSession();
        var later = Start.AddDays(5);

        var outcome = session.Rotate("hash-1", "hash-2", later, Sliding, Grace, "198.51.100.1", "Firefox");

        outcome.Should().Be(RefreshOutcome.Rotated);
        session.RefreshTokenHash.Should().Be("hash-2");
        session.PreviousRefreshTokenHash.Should().Be("hash-1");
        session.ExpiresAt.Should().Be(later.Add(Sliding));
        session.IpAddress.Should().Be("198.51.100.1");
    }

    [Fact]
    public void Sliding_expiry_never_exceeds_the_absolute_lifetime()
    {
        var session = NewSession();

        // An active user keeps sliding the window forward...
        session.Rotate("hash-1", "hash-2", Start.AddDays(10), Sliding, Grace, null, null).Should().Be(RefreshOutcome.Rotated);
        session.Rotate("hash-2", "hash-3", Start.AddDays(20), Sliding, Grace, null, null).Should().Be(RefreshOutcome.Rotated);

        // ...but never past the hard cap: 20 + 14 days would be day 34.
        session.ExpiresAt.Should().Be(Start.Add(Absolute));
        session.IpAddress.Should().Be("203.0.113.7", "a missing IP must not erase the known one");

        session.Rotate("hash-3", "hash-4", Start.Add(Absolute).AddSeconds(1), Sliding, Grace, null, null)
            .Should().Be(RefreshOutcome.Expired, "the absolute lifetime forces a fresh sign-in");
    }

    [Fact]
    public void Replaying_the_previous_token_immediately_is_rejected_without_revoking()
    {
        var session = NewSession();
        session.Rotate("hash-1", "hash-2", Start.AddMinutes(1), Sliding, Grace, null, null);

        var outcome = session.Rotate("hash-1", "hash-3", Start.AddMinutes(1).AddSeconds(3), Sliding, Grace, null, null);

        outcome.Should().Be(RefreshOutcome.ReuseWithinGrace);
        session.IsRevoked.Should().BeFalse("two browser tabs racing must not log the user out");
        session.RefreshTokenHash.Should().Be("hash-2");
    }

    [Fact]
    public void Replaying_the_previous_token_after_the_grace_period_is_treated_as_theft()
    {
        var session = NewSession();
        session.Rotate("hash-1", "hash-2", Start.AddMinutes(1), Sliding, Grace, null, null);

        var outcome = session.Rotate("hash-1", "hash-3", Start.AddMinutes(1).AddSeconds(11), Sliding, Grace, null, null);

        outcome.Should().Be(RefreshOutcome.ReuseDetected);
        session.IsRevoked.Should().BeTrue();
        session.RevokedReason.Should().Be("refresh_token_reuse");

        // ...and the legitimate holder's newest token is dead as well.
        session.Rotate("hash-2", "hash-4", Start.AddMinutes(2), Sliding, Grace, null, null).Should().Be(RefreshOutcome.Revoked);
    }

    [Fact]
    public void Expired_sessions_cannot_be_refreshed()
    {
        var session = NewSession();

        session.Rotate("hash-1", "hash-2", Start.Add(Sliding).AddSeconds(1), Sliding, Grace, null, null)
            .Should().Be(RefreshOutcome.Expired);
    }

    [Fact]
    public void A_revoked_session_stays_revoked_and_keeps_the_first_reason()
    {
        var session = NewSession();
        session.Revoke("logout", Start.AddHours(1));
        session.Revoke("something_else", Start.AddHours(2));

        session.RevokedReason.Should().Be("logout");
        session.RevokedAt.Should().Be(Start.AddHours(1));
        session.IsActive(Start.AddHours(3)).Should().BeFalse();
        session.Rotate("hash-1", "hash-2", Start.AddHours(3), Sliding, Grace, null, null).Should().Be(RefreshOutcome.Revoked);
    }

    [Fact]
    public void A_hash_matching_neither_slot_is_a_programming_error()
    {
        var session = NewSession();

        var act = () => session.Rotate("unknown", "hash-2", Start, Sliding, Grace, null, null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Overlong_client_metadata_is_truncated()
    {
        var user = User.Provision(Guid.NewGuid(), "u@example.org", "U", "Ser", "hash");

        var session = UserSession.Start(user, "h", Start, Sliding, Absolute, new string('1', 200), new string('x', 2000));

        session.IpAddress!.Length.Should().Be(64);
        session.UserAgent!.Length.Should().Be(512);
    }
}
