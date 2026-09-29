using System.Net;
using FundFlow.Api.IntegrationTests.Infrastructure;
using FundFlow.Contracts.Common;
using FundFlow.Contracts.Identity;
using FundFlow.Contracts.Organizations;
using FundFlow.Domain.Identity;

namespace FundFlow.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthorizationTests(ApiFixture fixture)
{
    private readonly TestKit _kit = new(fixture.Factory);

    // ---- permission-based access ---------------------------------------------------------------------

    [Fact]
    public async Task Every_protected_endpoint_rejects_anonymous_callers_with_401()
    {
        using var anonymous = _kit.NewSession();
        var id = Guid.NewGuid();
        (HttpMethod Method, string Url)[] endpoints =
        [
            (HttpMethod.Get, "/api/v1/auth/me"), (HttpMethod.Put, "/api/v1/auth/me"),
            (HttpMethod.Post, "/api/v1/auth/change-password"), (HttpMethod.Get, "/api/v1/auth/sessions"),
            (HttpMethod.Get, "/api/v1/users"), (HttpMethod.Get, $"/api/v1/users/{id}"), (HttpMethod.Post, "/api/v1/users"),
            (HttpMethod.Put, $"/api/v1/users/{id}"), (HttpMethod.Put, $"/api/v1/users/{id}/roles"),
            (HttpMethod.Post, $"/api/v1/users/{id}/deactivate"),
            (HttpMethod.Get, "/api/v1/roles"), (HttpMethod.Get, "/api/v1/permissions"), (HttpMethod.Post, "/api/v1/roles"),
            (HttpMethod.Delete, $"/api/v1/roles/{id}"),
            (HttpMethod.Get, "/api/v1/organization"), (HttpMethod.Put, "/api/v1/organization"),
            (HttpMethod.Put, "/api/v1/organization/settings"), (HttpMethod.Get, "/api/v1/organization/overview"),
            (HttpMethod.Get, "/api/v1/audit-logs"),
            (HttpMethod.Get, "/api/v1/platform/organizations"), (HttpMethod.Post, $"/api/v1/platform/organizations/{id}/suspend"),
        ];

        foreach (var (method, url) in endpoints)
        {
            using var response = await anonymous.SendAsync(method, url, body: method == HttpMethod.Get || method == HttpMethod.Delete ? null : new { }, authenticated: false);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{method} {url}");
        }
    }

    [Theory]
    [InlineData(SystemRoles.OrganizationAdmin, "/api/v1/users", HttpStatusCode.OK)]
    [InlineData(SystemRoles.OrganizationAdmin, "/api/v1/roles", HttpStatusCode.OK)]
    [InlineData(SystemRoles.OrganizationAdmin, "/api/v1/audit-logs", HttpStatusCode.OK)]
    [InlineData(SystemRoles.OrganizationAdmin, "/api/v1/organization/overview", HttpStatusCode.OK)]
    [InlineData(SystemRoles.OrganizationAdmin, "/api/v1/platform/organizations", HttpStatusCode.Forbidden)]
    [InlineData(SystemRoles.FinanceManager, "/api/v1/audit-logs", HttpStatusCode.OK)]
    [InlineData(SystemRoles.FinanceManager, "/api/v1/users", HttpStatusCode.Forbidden)]
    [InlineData(SystemRoles.FinanceManager, "/api/v1/roles", HttpStatusCode.Forbidden)]
    [InlineData(SystemRoles.FundraisingManager, "/api/v1/audit-logs", HttpStatusCode.Forbidden)]
    [InlineData(SystemRoles.FundraisingManager, "/api/v1/users", HttpStatusCode.Forbidden)]
    [InlineData(SystemRoles.Staff, "/api/v1/users", HttpStatusCode.Forbidden)]
    [InlineData(SystemRoles.Staff, "/api/v1/roles", HttpStatusCode.Forbidden)]
    [InlineData(SystemRoles.Staff, "/api/v1/audit-logs", HttpStatusCode.Forbidden)]
    [InlineData(SystemRoles.Staff, "/api/v1/organization", HttpStatusCode.OK)]
    [InlineData(SystemRoles.Staff, "/api/v1/organization/overview", HttpStatusCode.OK)]
    [InlineData(SystemRoles.Staff, "/api/v1/auth/me", HttpStatusCode.OK)]
    [InlineData(SystemRoles.Volunteer, "/api/v1/users", HttpStatusCode.Forbidden)]
    [InlineData(SystemRoles.ReportViewer, "/api/v1/audit-logs", HttpStatusCode.Forbidden)]
    public async Task Access_follows_the_permissions_of_the_users_roles(string role, string url, HttpStatusCode expected)
    {
        var org = await _kit.CreateOrganizationAsync();
        var user = role == SystemRoles.OrganizationAdmin ? null : await _kit.CreateUserAsync(org, role);
        var session = user?.Session ?? org.Owner;

        using var response = await session.GetAsync(url);

        response.StatusCode.Should().Be(expected, $"{role} → GET {url}");
    }

    [Fact]
    public async Task Write_endpoints_check_their_own_permission_not_just_authentication()
    {
        var org = await _kit.CreateOrganizationAsync();
        var readOnlyUser = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        var staffRole = await _kit.RoleIdAsync(org.Owner, SystemRoles.Staff);
        var s = readOnlyUser.Session;

        (await s.PostAsync("/api/v1/users", new InviteUserRequest("z@example.test", "Z", "Z", null, [staffRole]))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.PutAsync("/api/v1/organization", new UpdateOrganizationRequest("Hijacked", null, null, null, "x@example.test", null, null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.PutAsync("/api/v1/organization/settings", new UpdateOrganizationSettingsRequest("UTC", "USD", "en-US", 1, null, null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.PostAsync("/api/v1/roles", new CreateRoleRequest("Mine", null, []))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.PostAsync($"/api/v1/users/{readOnlyUser.Id}/deactivate")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_permission_catalogue_is_served_grouped_and_hides_platform_permissions_from_tenants()
    {
        var org = await _kit.CreateOrganizationAsync();

        var groups = await org.Owner.GetJsonAsync<List<PermissionGroupResponse>>("/api/v1/permissions");

        groups.SelectMany(g => g.Permissions).Select(p => p.Name).Should().BeEquivalentTo(Permissions.TenantScoped.Select(p => p.Name));
        groups.Select(g => g.Module).Should().Contain(["Donors", "Campaigns", "Users & Roles", "Audit"]);
        groups.SelectMany(g => g.Permissions).Should().NotContain(p => p.Name == Permissions.Platform.Manage);
    }

    // ---- privilege escalation ------------------------------------------------------------------------

    private async Task<(TestOrg Org, TestUser Delegate)> DelegateWithUserAndRoleManagementAsync()
    {
        var org = await _kit.CreateOrganizationAsync();
        using var created = await org.Owner.PostAsync(
            "/api/v1/roles",
            new CreateRoleRequest("Delegated Admin", "Manages users and roles, nothing else", [Permissions.User.Read, Permissions.User.Manage, Permissions.Role.Read, Permissions.Role.Manage]));
        var role = await created.ExpectAsync<RoleDetailResponse>(HttpStatusCode.Created);
        var delegated = await _kit.CreateUserWithRolesAsync(org, org.Owner, [role.Id]);
        return (org, delegated);
    }

    [Fact]
    public async Task A_user_manager_cannot_grant_roles_that_carry_permissions_they_do_not_hold()
    {
        var (org, delegated) = await DelegateWithUserAndRoleManagementAsync();
        var adminRole = await _kit.RoleIdAsync(delegated.Session, SystemRoles.OrganizationAdmin);
        var financeRole = await _kit.RoleIdAsync(delegated.Session, SystemRoles.FinanceManager);
        var email = $"target-{TestKit.Unique("")}@example.test";

        foreach (var forbidden in new[] { adminRole, financeRole })
        {
            using var response = await delegated.Session.PostAsync("/api/v1/users", new InviteUserRequest(email, "T", "T", null, [forbidden]));
            var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "privilege_escalation");
            problem.Detail.Should().Contain("cannot grant permissions you do not hold");
        }

        var search = await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>($"/api/v1/users?search={Uri.EscapeDataString(email)}");
        search.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task A_user_manager_can_grant_roles_that_are_within_their_own_permissions()
    {
        var (org, delegated) = await DelegateWithUserAndRoleManagementAsync();
        using var created = await delegated.Session.PostAsync("/api/v1/roles", new CreateRoleRequest("Directory Reader", null, [Permissions.User.Read]));
        var role = await created.ExpectAsync<RoleDetailResponse>(HttpStatusCode.Created);

        var invited = await _kit.CreateUserWithRolesAsync(org, delegated.Session, [role.Id]);

        var me = await invited.Session.GetJsonAsync<CurrentUserResponse>("/api/v1/auth/me");
        me.Permissions.Should().Equal(Permissions.User.Read);
    }

    [Fact]
    public async Task A_user_manager_cannot_create_or_edit_a_role_beyond_their_own_permissions()
    {
        var (_, delegated) = await DelegateWithUserAndRoleManagementAsync();
        using var ok = await delegated.Session.PostAsync("/api/v1/roles", new CreateRoleRequest("Small", null, [Permissions.User.Read]));
        var small = await ok.ExpectAsync<RoleDetailResponse>(HttpStatusCode.Created);

        using var create = await delegated.Session.PostAsync("/api/v1/roles", new CreateRoleRequest("Refunder", null, [Permissions.Donation.Refund]));
        await create.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "privilege_escalation");

        using var edit = await delegated.Session.PutAsync($"/api/v1/roles/{small.Id}", new UpdateRoleRequest("Small", null, [Permissions.User.Read, Permissions.Audit.Read]));
        await edit.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "privilege_escalation");
    }

    [Fact]
    public async Task Only_administrators_may_change_administrators()
    {
        var (org, delegated) = await DelegateWithUserAndRoleManagementAsync();
        var ownerId = (await org.Owner.GetJsonAsync<CurrentUserResponse>("/api/v1/auth/me")).Id;
        var staffRole = await _kit.RoleIdAsync(org.Owner, SystemRoles.Staff);

        (await delegated.Session.PutAsync($"/api/v1/users/{ownerId}", new UpdateUserRequest("Pwned", "Owner", null)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await delegated.Session.PutAsync($"/api/v1/users/{ownerId}/roles", new SetUserRolesRequest([staffRole])))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await delegated.Session.PostAsync($"/api/v1/users/{ownerId}/deactivate"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await delegated.Session.PostAsync($"/api/v1/users/{ownerId}/unlock"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await org.Owner.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK, "the owner is untouched");
    }

    [Fact]
    public async Task System_roles_are_read_only_for_everyone_including_administrators()
    {
        var org = await _kit.CreateOrganizationAsync();
        var staffRole = await _kit.RoleIdAsync(org.Owner, SystemRoles.Staff);

        using var edit = await org.Owner.PutAsync($"/api/v1/roles/{staffRole}", new UpdateRoleRequest("STAFF", null, Permissions.All.Select(p => p.Name).Where(n => n != Permissions.Platform.Manage).ToList()));
        await edit.ShouldBeProblemAsync(HttpStatusCode.Conflict, "role.system_role_immutable");

        using var delete = await org.Owner.DeleteAsync($"/api/v1/roles/{staffRole}");
        await delete.ShouldBeProblemAsync(HttpStatusCode.Conflict, "role.system_role_immutable");
    }

    [Fact]
    public async Task Custom_roles_cannot_be_given_platform_permissions()
    {
        var org = await _kit.CreateOrganizationAsync();

        using var response = await org.Owner.PostAsync("/api/v1/roles", new CreateRoleRequest("Sneaky", null, [Permissions.Platform.Manage]));

        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "role.platform_permission_forbidden");
    }

    // ---- guard rails ---------------------------------------------------------------------------------

    [Fact]
    public async Task An_organization_can_never_lose_its_last_administrator()
    {
        var org = await _kit.CreateOrganizationAsync();
        var ownerId = (await org.Owner.GetJsonAsync<CurrentUserResponse>("/api/v1/auth/me")).Id;
        var staffRole = await _kit.RoleIdAsync(org.Owner, SystemRoles.Staff);
        var adminRole = await _kit.RoleIdAsync(org.Owner, SystemRoles.OrganizationAdmin);

        using var self = await org.Owner.PostAsync($"/api/v1/users/{ownerId}/deactivate");
        await self.ShouldBeProblemAsync(HttpStatusCode.Conflict, "cannot_deactivate_self");

        using var demote = await org.Owner.PutAsync($"/api/v1/users/{ownerId}/roles", new SetUserRolesRequest([staffRole]));
        await demote.ShouldBeProblemAsync(HttpStatusCode.Conflict, "last_administrator");

        // Once a second administrator exists, the first may step down.
        var second = await _kit.CreateUserWithRolesAsync(org, org.Owner, [adminRole]);
        using var stepDown = await org.Owner.PutAsync($"/api/v1/users/{ownerId}/roles", new SetUserRolesRequest([staffRole]));
        await stepDown.ShouldBeAsync(HttpStatusCode.OK);

        // ...and now the second is the last one.
        using var deactivateLast = await second.Session.PostAsync($"/api/v1/users/{second.Id}/deactivate");
        await deactivateLast.ShouldBeProblemAsync(HttpStatusCode.Conflict, "cannot_deactivate_self");
        using var demoteLast = await second.Session.PutAsync($"/api/v1/users/{second.Id}/roles", new SetUserRolesRequest([staffRole]));
        await demoteLast.ShouldBeProblemAsync(HttpStatusCode.Conflict, "last_administrator");
    }

    [Fact]
    public async Task A_role_that_is_assigned_to_users_cannot_be_deleted()
    {
        var org = await _kit.CreateOrganizationAsync();
        using var created = await org.Owner.PostAsync("/api/v1/roles", new CreateRoleRequest("Temp Role", null, [Permissions.Donor.Read]));
        var role = await created.ExpectAsync<RoleDetailResponse>(HttpStatusCode.Created);
        var holder = await _kit.CreateUserWithRolesAsync(org, org.Owner, [role.Id]);

        using var blocked = await org.Owner.DeleteAsync($"/api/v1/roles/{role.Id}");
        await blocked.ShouldBeProblemAsync(HttpStatusCode.Conflict, "role_in_use");

        var staffRole = await _kit.RoleIdAsync(org.Owner, SystemRoles.Staff);
        using var move = await org.Owner.PutAsync($"/api/v1/users/{holder.Id}/roles", new SetUserRolesRequest([staffRole]));
        await move.ShouldBeAsync(HttpStatusCode.OK);
        using var deleted = await org.Owner.DeleteAsync($"/api/v1/roles/{role.Id}");
        await deleted.ShouldBeAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Role_names_are_unique_within_a_tenant_but_free_to_reuse_across_tenants()
    {
        var a = await _kit.CreateOrganizationAsync();
        var b = await _kit.CreateOrganizationAsync();

        (await a.Owner.PostAsync("/api/v1/roles", new CreateRoleRequest("Board Member", null, [Permissions.Report.Read]))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await b.Owner.PostAsync("/api/v1/roles", new CreateRoleRequest("Board Member", null, [Permissions.Report.Read]))).StatusCode.Should().Be(HttpStatusCode.Created);

        using var duplicate = await a.Owner.PostAsync("/api/v1/roles", new CreateRoleRequest("  board MEMBER ", null, []));
        var problem = await duplicate.ShouldBeProblemAsync(HttpStatusCode.Conflict, "role_name_taken");
        problem.Errors.Should().ContainKey("name");

        using var reserved = await a.Owner.PostAsync("/api/v1/roles", new CreateRoleRequest("staff", null, []));
        await reserved.ShouldBeProblemAsync(HttpStatusCode.Conflict, "role_name_taken");
    }

    [Fact]
    public async Task Changing_a_roles_permissions_takes_effect_on_the_very_next_request()
    {
        var org = await _kit.CreateOrganizationAsync();
        using var created = await org.Owner.PostAsync("/api/v1/roles", new CreateRoleRequest("Auditor", null, [Permissions.Audit.Read]));
        var role = await created.ExpectAsync<RoleDetailResponse>(HttpStatusCode.Created);
        var auditor = await _kit.CreateUserWithRolesAsync(org, org.Owner, [role.Id]);

        (await auditor.Session.GetAsync("/api/v1/audit-logs")).StatusCode.Should().Be(HttpStatusCode.OK); // warms the access cache

        using var revoke = await org.Owner.PutAsync($"/api/v1/roles/{role.Id}", new UpdateRoleRequest("Auditor", null, []));
        await revoke.ShouldBeAsync(HttpStatusCode.OK);

        (await auditor.Session.GetAsync("/api/v1/audit-logs")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "cached access must be invalidated, not left to expire");

        using var restore = await org.Owner.PutAsync($"/api/v1/roles/{role.Id}", new UpdateRoleRequest("Auditor", null, [Permissions.Audit.Read]));
        await restore.ShouldBeAsync(HttpStatusCode.OK);
        (await auditor.Session.GetAsync("/api/v1/audit-logs")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reassigning_a_users_roles_takes_effect_on_the_very_next_request()
    {
        var org = await _kit.CreateOrganizationAsync();
        var user = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        (await user.Session.GetAsync("/api/v1/audit-logs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var finance = await _kit.RoleIdAsync(org.Owner, SystemRoles.FinanceManager);
        using var promote = await org.Owner.PutAsync($"/api/v1/users/{user.Id}/roles", new SetUserRolesRequest([finance]));
        var detail = await promote.ExpectAsync<UserDetailResponse>();

        detail.Roles.Select(r => r.Name).Should().Equal(SystemRoles.FinanceManager);
        (await user.Session.GetAsync("/api/v1/audit-logs")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_permissions_are_rejected_by_the_domain_for_administrators_and_by_the_guard_for_everyone_else()
    {
        var (org, delegated) = await DelegateWithUserAndRoleManagementAsync();

        // An administrator may grant anything that exists...
        using var admin = await org.Owner.PostAsync("/api/v1/roles", new CreateRoleRequest("Odd", null, ["Nonsense.Everything"]));
        await admin.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "role.unknown_permission");

        // ...while a delegate cannot grant what they do not hold, and nobody holds a permission that does not exist.
        using var delegateAttempt = await delegated.Session.PostAsync("/api/v1/roles", new CreateRoleRequest("Odd 2", null, ["Nonsense.Everything"]));
        await delegateAttempt.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "privilege_escalation");
    }
}
