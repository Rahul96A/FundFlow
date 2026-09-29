using FundFlow.Domain.Identity;
using FundFlow.Domain.SharedKernel;

namespace FundFlow.Domain.Tests.Identity;

public class RoleTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void System_roles_are_created_from_their_template()
    {
        var template = RoleTemplates.Tenant.Single(t => t.Name == SystemRoles.FinanceManager);

        var role = Role.CreateSystem(Tenant, template);

        role.IsSystem.Should().BeTrue();
        role.TenantId.Should().Be(Tenant);
        role.NormalizedName.Should().Be("FINANCE_MANAGER");
        role.Permissions.Select(p => p.PermissionName).Should().BeEquivalentTo(template.Permissions);
        role.Permissions.Should().OnlyContain(p => p.TenantId == Tenant && p.RoleId == role.Id);
    }

    [Fact]
    public void System_roles_are_immutable()
    {
        var role = Role.CreateSystem(Tenant, RoleTemplates.Tenant[0]);

        var act = () => role.Update("Renamed", null, [Permissions.Donor.Read]);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("role.system_role_immutable");
        Action ensureEditable = role.EnsureEditable;
        ensureEditable.Should().Throw<DomainException>();
    }

    [Fact]
    public void Custom_roles_can_be_edited_and_report_the_permission_diff()
    {
        var role = Role.CreateCustom(Tenant, "  Grant Writer ", "  Writes grants ", [Permissions.Donor.Read, Permissions.Campaign.Read]);

        role.IsSystem.Should().BeFalse();
        role.Name.Should().Be("Grant Writer");
        role.NormalizedName.Should().Be("GRANT WRITER");
        role.Description.Should().Be("Writes grants");

        var (added, removed) = role.ReplacePermissions([Permissions.Campaign.Read, Permissions.Report.Read, Permissions.Report.Read]);

        added.Should().Equal(Permissions.Report.Read);
        removed.Should().Equal(Permissions.Donor.Read);
        role.Permissions.Select(p => p.PermissionName).Should().BeEquivalentTo(Permissions.Campaign.Read, Permissions.Report.Read);
    }

    [Fact]
    public void Roles_reject_unknown_permissions()
    {
        var act = () => Role.CreateCustom(Tenant, "Odd", null, ["Donor.Read", "Nonsense.Everything"]);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("role.unknown_permission");
    }

    [Fact]
    public void Tenant_roles_can_never_hold_platform_permissions()
    {
        var act = () => Role.CreateCustom(Tenant, "Sneaky", null, [Permissions.Platform.Manage]);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("role.platform_permission_forbidden");
    }

    [Fact]
    public void The_platform_role_may_hold_platform_permissions()
    {
        var role = Role.CreateSystem(null, RoleTemplates.SuperAdmin);

        role.TenantId.Should().BeNull();
        role.Permissions.Select(p => p.PermissionName).Should().Contain(Permissions.Platform.Manage);
    }

    [Fact]
    public void A_custom_role_needs_a_name()
    {
        var act = () => Role.CreateCustom(Tenant, "   ", null, []);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("role.name_required");
    }
}
