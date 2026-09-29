using System.Reflection;
using FundFlow.Domain.Identity;

namespace FundFlow.Domain.Tests.Identity;

public class PermissionCatalogTests
{
    private static IEnumerable<string> ConstantNames() =>
        typeof(Permissions).GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    [Fact]
    public void Every_permission_constant_is_listed_in_the_catalogue()
    {
        var listed = Permissions.All.Select(p => p.Name).ToHashSet();

        ConstantNames().Should().OnlyContain(name => listed.Contains(name));
        listed.Should().BeEquivalentTo(ConstantNames());
    }

    [Fact]
    public void Permission_names_are_unique_and_follow_the_Module_Action_convention()
    {
        Permissions.All.Select(p => p.Name).Should().OnlyHaveUniqueItems();
        Permissions.All.Should().OnlyContain(p => p.Name.Split('.', StringSplitOptions.None).Length == 2);
        Permissions.All.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Description) && !string.IsNullOrWhiteSpace(p.Module));
    }

    [Fact]
    public void Spec_permissions_exist()
    {
        string[] required =
        [
            "Donor.Read", "Donor.Create", "Donor.Update", "Donor.Delete",
            "Campaign.Read", "Campaign.Create", "Campaign.Update", "Campaign.Publish",
            "Donation.Read", "Donation.Create", "Donation.Refund",
            "Event.Read", "Event.Create", "Event.Update", "Event.Publish",
            "Auction.Read", "Auction.Create", "Auction.Manage", "Auction.Close",
            "Report.Read", "Report.Export", "User.Manage", "Audit.Read",
        ];

        Permissions.All.Select(p => p.Name).Should().Contain(required);
    }

    [Fact]
    public void Tenant_scoped_permissions_exclude_platform_operator_permissions()
    {
        Permissions.TenantScoped.Select(p => p.Name).Should().NotContain(Permissions.Platform.Manage);
        Permissions.All.Select(p => p.Name).Should().Contain(Permissions.Platform.Manage);
    }

    [Fact]
    public void Every_role_template_only_references_known_permissions()
    {
        foreach (var template in RoleTemplates.Tenant.Append(RoleTemplates.SuperAdmin))
        {
            template.Permissions.Should().OnlyContain(p => Permissions.IsKnown(p), $"template {template.Name} must not reference unknown permissions");
            template.Permissions.Should().OnlyHaveUniqueItems($"template {template.Name} must not repeat permissions");
        }
    }

    [Fact]
    public void Tenant_roles_never_carry_platform_permissions()
    {
        RoleTemplates.Tenant.SelectMany(t => t.Permissions).Should().NotContain(Permissions.Platform.Manage);
    }

    [Fact]
    public void Organization_admin_holds_every_tenant_permission()
    {
        var admin = RoleTemplates.Tenant.Single(t => t.Name == SystemRoles.OrganizationAdmin);

        admin.Permissions.Should().BeEquivalentTo(Permissions.TenantScoped.Select(p => p.Name));
    }

    [Fact]
    public void Super_admin_is_a_platform_role_without_business_data_access()
    {
        RoleTemplates.SuperAdmin.Permissions.Should().Contain(Permissions.Platform.Manage);
        RoleTemplates.SuperAdmin.Permissions.Should().NotContain(new[] { Permissions.Donor.Read, Permissions.Donation.Read, Permissions.User.Read });
    }

    [Fact]
    public void All_spec_roles_are_defined()
    {
        var names = RoleTemplates.Tenant.Select(t => t.Name).Append(RoleTemplates.SuperAdmin.Name);

        names.Should().BeEquivalentTo(
            "SUPER_ADMIN", "ORGANIZATION_ADMIN", "FUNDRAISING_MANAGER", "EVENT_MANAGER", "DONOR_MANAGER",
            "FINANCE_MANAGER", "MARKETING_MANAGER", "VOLUNTEER_MANAGER", "REPORT_VIEWER", "STAFF", "VOLUNTEER");
    }

    [Theory]
    [InlineData(SystemRoles.OrganizationAdmin, true)]
    [InlineData(SystemRoles.SuperAdmin, true)]
    [InlineData(SystemRoles.FundraisingManager, false)]
    [InlineData(SystemRoles.Staff, false)]
    public void Only_administrator_roles_are_administrators(string role, bool expected)
    {
        SystemRoles.IsAdministrator(role).Should().Be(expected);
    }
}
