using FundFlow.Domain.Identity;

namespace FundFlow.Domain.Tests.Identity;

public class PrivilegeEscalationPolicyTests
{
    private static IReadOnlySet<string> Held(params string[] permissions) => permissions.ToHashSet();

    [Fact]
    public void A_user_can_grant_permissions_they_hold()
    {
        var actor = Held(Permissions.Donor.Read, Permissions.Donor.Update, Permissions.User.Manage);

        PrivilegeEscalationPolicy.CanGrant(actor, actorIsAdministrator: false, [Permissions.Donor.Read]).Should().BeTrue();
        PrivilegeEscalationPolicy.CanGrant(actor, false, []).Should().BeTrue();
    }

    [Fact]
    public void A_user_cannot_grant_a_permission_they_do_not_hold()
    {
        var actor = Held(Permissions.Donor.Read, Permissions.User.Manage);

        PrivilegeEscalationPolicy.CanGrant(actor, false, [Permissions.Donor.Read, Permissions.Donation.Refund]).Should().BeFalse();
        PrivilegeEscalationPolicy.Missing(actor, false, [Permissions.Donor.Read, Permissions.Donation.Refund, Permissions.Audit.Read])
            .Should().Equal(Permissions.Audit.Read, Permissions.Donation.Refund);
    }

    [Fact]
    public void Administrators_may_grant_anything()
    {
        var actor = Held(Permissions.User.Manage);

        PrivilegeEscalationPolicy.CanGrant(actor, actorIsAdministrator: true, [Permissions.Donation.Refund, Permissions.Audit.Read]).Should().BeTrue();
        PrivilegeEscalationPolicy.Missing(actor, true, [Permissions.Donation.Refund]).Should().BeEmpty();
    }
}
