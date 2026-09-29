using FundFlow.Domain.Identity;
using FundFlow.Domain.SharedKernel;

namespace FundFlow.Domain.Tests.Identity;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly LockoutPolicy Policy = new(MaxFailedAttempts: 3, Duration: TimeSpan.FromMinutes(15));

    private static User NewUser(Guid? tenant = null) =>
        User.Register(tenant ?? Guid.NewGuid(), "Ada@Example.org ", " Ada ", " Lovelace ", "hash");

    private static Role NewRole(Guid tenant, string name = "STAFF") =>
        Role.CreateSystem(tenant, RoleTemplates.Tenant.Single(t => t.Name == name));

    [Fact]
    public void Register_normalises_input_and_requests_email_verification()
    {
        var tenant = Guid.NewGuid();

        var user = User.Register(tenant, "Ada@Example.org ", " Ada ", " Lovelace ", "hash");

        user.Email.Should().Be("Ada@Example.org");
        user.NormalizedEmail.Should().Be("ADA@EXAMPLE.ORG");
        user.FirstName.Should().Be("Ada");
        user.FullName.Should().Be("Ada Lovelace");
        user.TenantId.Should().Be(tenant);
        user.EmailConfirmed.Should().BeFalse();
        user.IsActive.Should().BeTrue();
        user.DomainEvents.Should().Contain(e => e is UserCreatedDomainEvent)
            .And.Contain(e => e is EmailVerificationRequestedDomainEvent);
    }

    [Fact]
    public void Invite_creates_a_passwordless_user_and_raises_an_invitation()
    {
        var user = User.Invite(Guid.NewGuid(), "new@example.org", "New", "Person", "  +1 555 0100 ");

        user.HasPassword.Should().BeFalse();
        user.PhoneNumber.Should().Be("+1 555 0100");
        user.DomainEvents.OfType<UserInvitedDomainEvent>().Should().ContainSingle(e => e.Email == "new@example.org");
        user.DomainEvents.OfType<UserCreatedDomainEvent>().Should().ContainSingle(e => e.Kind == UserCreationKind.Invited);
    }

    [Fact]
    public void Provision_confirms_the_email_and_sends_nothing()
    {
        var user = User.Provision(null, "root@fundflow.test", "Platform", "Admin", "hash");

        user.EmailConfirmed.Should().BeTrue();
        user.TenantId.Should().BeNull();
        user.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<UserCreatedDomainEvent>();
    }

    [Theory]
    [InlineData("", "Ada", "Lovelace")]
    [InlineData("not-an-email", "Ada", "Lovelace")]
    [InlineData("ada@example.org", "", "Lovelace")]
    [InlineData("ada@example.org", "Ada", "  ")]
    public void Invalid_input_is_rejected_by_the_domain(string email, string first, string last)
    {
        var act = () => User.Register(Guid.NewGuid(), email, first, last, "hash");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Repeated_failures_lock_the_account_then_a_success_clears_the_counter()
    {
        var user = NewUser();

        user.RegisterFailedLogin(Now, Policy).Should().BeFalse();
        user.RegisterFailedLogin(Now, Policy).Should().BeFalse();
        user.RegisterFailedLogin(Now, Policy).Should().BeTrue("the third failure crosses the threshold");

        user.IsLockedOut(Now).Should().BeTrue();
        user.IsLockedOut(Now.AddMinutes(14)).Should().BeTrue();
        user.IsLockedOut(Now.AddMinutes(15)).Should().BeFalse("the lockout has expired");

        user.RegisterSuccessfulLogin(Now.AddMinutes(20));
        user.AccessFailedCount.Should().Be(0);
        user.LockoutEnd.Should().BeNull();
        user.LastLoginAt.Should().Be(Now.AddMinutes(20));
    }

    [Fact]
    public void Failures_below_the_threshold_do_not_lock()
    {
        var user = NewUser();

        user.RegisterFailedLogin(Now, Policy);
        user.RegisterFailedLogin(Now, Policy);

        user.IsLockedOut(Now).Should().BeFalse();
        user.AccessFailedCount.Should().Be(2);
    }

    [Fact]
    public void Unlock_clears_lockout_state()
    {
        var user = NewUser();
        for (var i = 0; i < 3; i++)
        {
            user.RegisterFailedLogin(Now, Policy);
        }

        user.Unlock();

        user.IsLockedOut(Now).Should().BeFalse();
        user.AccessFailedCount.Should().Be(0);
    }

    [Fact]
    public void Setting_a_password_rotates_the_security_stamp_and_clears_lockout()
    {
        var user = NewUser();
        var before = user.SecurityStamp;
        for (var i = 0; i < 3; i++)
        {
            user.RegisterFailedLogin(Now, Policy);
        }

        user.SetPassword("new-hash");

        user.PasswordHash.Should().Be("new-hash");
        user.SecurityStamp.Should().NotBe(before);
        user.IsLockedOut(Now).Should().BeFalse();
    }

    [Fact]
    public void Accepting_an_invitation_sets_the_password_and_confirms_the_email()
    {
        var user = User.Invite(Guid.NewGuid(), "new@example.org", "New", "Person", null);

        user.AcceptInvitation("hash", " Nova ", null);

        user.HasPassword.Should().BeTrue();
        user.EmailConfirmed.Should().BeTrue();
        user.FirstName.Should().Be("Nova");
        user.LastName.Should().Be("Person");
    }

    [Fact]
    public void Verification_cannot_be_requested_twice_for_a_confirmed_address()
    {
        var user = NewUser();
        user.ConfirmEmail();

        var act = user.RequestEmailVerification;

        act.Should().Throw<DomainException>().Which.Code.Should().Be("user.email_already_confirmed");
    }

    [Fact]
    public void Deactivated_users_cannot_request_a_password_reset()
    {
        var user = NewUser();
        user.Deactivate();

        var act = user.RequestPasswordReset;

        act.Should().Throw<DomainException>().Which.Code.Should().Be("user.deactivated");
    }

    [Fact]
    public void An_invitation_can_only_be_resent_until_it_is_accepted()
    {
        var invited = User.Invite(Guid.NewGuid(), "new@example.org", "New", "Person", null);
        invited.ClearDomainEvents();

        invited.ResendInvitation();
        invited.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<UserInvitedDomainEvent>();

        invited.AcceptInvitation("hash", null, null);
        var act = invited.ResendInvitation;
        act.Should().Throw<DomainException>().Which.Code.Should().Be("user.invitation_already_accepted");
    }

    [Fact]
    public void SetRoles_reports_exactly_what_changed()
    {
        var tenant = Guid.NewGuid();
        var user = NewUser(tenant);
        var staff = NewRole(tenant, "STAFF");
        var finance = NewRole(tenant, "FINANCE_MANAGER");
        var volunteer = NewRole(tenant, "VOLUNTEER");

        var first = user.SetRoles([staff, finance], assignedBy: null, Now);
        first.Added.Should().BeEquivalentTo(new[] { staff.Id, finance.Id });
        first.Removed.Should().BeEmpty();

        var second = user.SetRoles([finance, volunteer], assignedBy: Guid.NewGuid(), Now);
        second.Added.Should().Equal(volunteer.Id);
        second.Removed.Should().Equal(staff.Id);

        user.UserRoles.Select(ur => ur.RoleId).Should().BeEquivalentTo(new[] { finance.Id, volunteer.Id });
        user.UserRoles.Should().OnlyContain(ur => ur.TenantId == tenant);
    }

    [Fact]
    public void Setting_the_same_roles_again_changes_nothing()
    {
        var tenant = Guid.NewGuid();
        var user = NewUser(tenant);
        var staff = NewRole(tenant);
        user.SetRoles([staff], null, Now);

        var result = user.SetRoles([staff], null, Now);

        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
    }

    [Fact]
    public void UpdateProfile_trims_and_clears_blank_phone_numbers()
    {
        var user = NewUser();

        user.UpdateProfile("  Grace ", " Hopper ", "   ");

        user.FirstName.Should().Be("Grace");
        user.LastName.Should().Be("Hopper");
        user.PhoneNumber.Should().BeNull();
    }
}
