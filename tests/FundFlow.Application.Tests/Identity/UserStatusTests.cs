using FundFlow.Application.Identity;
using FundFlow.Contracts.Identity;
using FundFlow.Domain.Identity;

namespace FundFlow.Application.Tests.Identity;

public class UserStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, null, true, true, UserStatus.Deactivated)]
    [InlineData(false, "future", false, false, UserStatus.Deactivated)]
    [InlineData(true, "future", true, true, UserStatus.Locked)]
    [InlineData(true, "past", true, true, UserStatus.Active)]
    [InlineData(true, null, false, false, UserStatus.Invited)]
    [InlineData(true, null, true, false, UserStatus.PendingVerification)]
    [InlineData(true, null, true, true, UserStatus.Active)]
    public void Status_is_derived_in_a_fixed_precedence(bool isActive, string? lockout, bool hasPassword, bool confirmed, UserStatus expected)
    {
        DateTimeOffset? lockoutEnd = lockout switch
        {
            "future" => Now.AddMinutes(5),
            "past" => Now.AddMinutes(-5),
            _ => null,
        };

        IdentityQueries.DetermineStatus(isActive, lockoutEnd, hasPassword, confirmed, Now).Should().Be(expected);
    }

    /// <summary>
    /// The list endpoint filters with an SQL predicate and displays with the C# function. They must agree for every
    /// combination of inputs, otherwise a "Locked" filter could show users labelled "Active".
    /// </summary>
    [Fact]
    public void The_sql_filter_predicate_agrees_with_the_displayed_status_for_every_combination()
    {
        bool[] flags = [true, false];
        DateTimeOffset?[] lockouts = [null, Now.AddMinutes(10), Now.AddMinutes(-10)];

        foreach (var isActive in flags)
        foreach (var hasPassword in flags)
        foreach (var confirmed in flags)
        foreach (var lockout in lockouts)
        {
            var user = User.Provision(Guid.NewGuid(), "a@b.org", "A", "B", "hash");
            SetPrivate(user, nameof(User.IsActive), isActive);
            SetPrivate(user, nameof(User.PasswordHash), hasPassword ? "hash" : null);
            SetPrivate(user, nameof(User.EmailConfirmed), confirmed);
            SetPrivate(user, nameof(User.LockoutEnd), lockout);

            var displayed = IdentityQueries.DetermineStatus(isActive, lockout, hasPassword, confirmed, Now);

            foreach (var status in Enum.GetValues<UserStatus>())
            {
                var matches = IdentityQueries.HasStatus(status, Now).Compile()(user);
                matches.Should().Be(
                    status == displayed,
                    $"active={isActive} password={hasPassword} confirmed={confirmed} lockout={lockout} filter={status}");
            }
        }
    }

    private static void SetPrivate(User user, string property, object? value) =>
        typeof(User).GetProperty(property)!.SetValue(user, value);
}
