using System.Net;
using FundFlow.Api.IntegrationTests.Infrastructure;
using FundFlow.Contracts.Audit;
using FundFlow.Contracts.Common;
using FundFlow.Contracts.Identity;
using FundFlow.Contracts.Organizations;
using FundFlow.Domain.Audit;
using FundFlow.Domain.Identity;
using FundFlow.Domain.Organizations;
using FundFlow.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FundFlow.Api.IntegrationTests;

/// <summary>
/// "A user from Organization A must NEVER retrieve Organization B's data." Tested twice over: through the public API
/// (what an attacker can do) and at the data-access layer (what a coding mistake could do).
/// </summary>
[Collection(ApiCollection.Name)]
public class TenantIsolationTests(ApiFixture fixture)
{
    private readonly TestKit _kit = new(fixture.Factory);

    // ---- API level -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_user_from_organization_A_can_never_retrieve_organization_Bs_user()
    {
        var a = await _kit.CreateOrganizationAsync();
        var b = await _kit.CreateOrganizationAsync();
        var bUser = await _kit.CreateUserAsync(b, SystemRoles.Staff);

        // Even A's most privileged user cannot see B's user by id...
        using var byId = await a.Owner.GetAsync($"/api/v1/users/{bUser.Id}");
        await byId.ShouldBeProblemAsync(HttpStatusCode.NotFound);

        // ...nor find them by searching for their exact email...
        var search = await a.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>($"/api/v1/users?search={Uri.EscapeDataString(bUser.Email)}");
        search.TotalCount.Should().Be(0);

        // ...nor in the full listing.
        var all = await a.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>("/api/v1/users?pageSize=100");
        all.Items.Select(u => u.Email).Should().OnlyContain(email => email == a.OwnerEmail);
        all.Items.Select(u => u.Id).Should().NotContain(bUser.Id);
    }

    [Fact]
    public async Task Modifying_another_tenants_user_is_indistinguishable_from_modifying_a_user_that_does_not_exist()
    {
        var a = await _kit.CreateOrganizationAsync();
        var b = await _kit.CreateOrganizationAsync();
        var bUser = await _kit.CreateUserAsync(b, SystemRoles.Staff);
        var aStaffRole = await _kit.RoleIdAsync(a.Owner, SystemRoles.Staff);
        var missing = Guid.NewGuid();

        var attempts = new (string Name, Func<Guid, Task<HttpResponseMessage>> Send)[]
        {
            ("update", id => a.Owner.PutAsync($"/api/v1/users/{id}", new UpdateUserRequest("Hacked", "Name", null))),
            ("set roles", id => a.Owner.PutAsync($"/api/v1/users/{id}/roles", new SetUserRolesRequest([aStaffRole]))),
            ("deactivate", id => a.Owner.PostAsync($"/api/v1/users/{id}/deactivate")),
            ("reactivate", id => a.Owner.PostAsync($"/api/v1/users/{id}/reactivate")),
            ("unlock", id => a.Owner.PostAsync($"/api/v1/users/{id}/unlock")),
            ("resend invitation", id => a.Owner.PostAsync($"/api/v1/users/{id}/resend-invitation")),
        };

        foreach (var (name, send) in attempts)
        {
            using var foreign = await send(bUser.Id);
            using var absent = await send(missing);
            var foreignProblem = await foreign.ShouldBeProblemAsync(HttpStatusCode.NotFound);
            var absentProblem = await absent.ShouldBeProblemAsync(HttpStatusCode.NotFound);

            // Same status, same message once the (caller-supplied) id is masked: B's user leaves no fingerprint.
            foreignProblem.Detail!.Replace(bUser.Id.ToString(), "<id>").Should().Be(
                absentProblem.Detail!.Replace(missing.ToString(), "<id>"), $"'{name}' must not distinguish a foreign user from a missing one");
        }

        // And nothing actually changed.
        var stillFine = await b.Owner.GetJsonAsync<UserDetailResponse>($"/api/v1/users/{bUser.Id}");
        stillFine.FirstName.Should().Be("Casey");
        stillFine.IsActive.Should().BeTrue();
        stillFine.Roles.Select(r => r.Name).Should().Equal(SystemRoles.Staff);
    }

    [Fact]
    public async Task Roles_are_per_tenant_and_A_cannot_read_edit_delete_or_assign_Bs_roles()
    {
        var a = await _kit.CreateOrganizationAsync();
        var b = await _kit.CreateOrganizationAsync();
        using var created = await b.Owner.PostAsync("/api/v1/roles", new CreateRoleRequest("B Secret Role", "internal", [Permissions.Donor.Read]));
        var bRole = await created.ExpectAsync<RoleDetailResponse>(HttpStatusCode.Created);
        var bAdminRoleId = await _kit.RoleIdAsync(b.Owner, SystemRoles.OrganizationAdmin);
        var aStaff = await _kit.CreateUserAsync(a, SystemRoles.Staff);

        (await a.Owner.GetAsync($"/api/v1/roles/{bRole.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await a.Owner.PutAsync($"/api/v1/roles/{bRole.Id}", new UpdateRoleRequest("Renamed", null, []))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await a.Owner.DeleteAsync($"/api/v1/roles/{bRole.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Trying to give one's own colleague B's ADMIN role must not work either: the role is invisible.
        using var assign = await a.Owner.PutAsync($"/api/v1/users/{aStaff.Id}/roles", new SetUserRolesRequest([bAdminRoleId]));
        await assign.ShouldBeAsync(HttpStatusCode.NotFound);

        var aRoles = await a.Owner.GetJsonAsync<List<RoleSummaryResponse>>("/api/v1/roles");
        aRoles.Select(r => r.Name).Should().NotContain("B Secret Role");
        aRoles.Select(r => r.Id).Should().NotContain(bRole.Id).And.NotContain(bAdminRoleId);

        // A invites someone into A while naming B's role id: rejected, and the invitee must not exist.
        var email = $"sneaky-{TestKit.Unique("")}@example.test";
        using var invite = await a.Owner.PostAsync("/api/v1/users", new InviteUserRequest(email, "S", "N", null, [bAdminRoleId]));
        await invite.ShouldBeAsync(HttpStatusCode.NotFound);
        var search = await a.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>($"/api/v1/users?search={Uri.EscapeDataString(email)}");
        search.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Audit_logs_never_cross_tenants()
    {
        var a = await _kit.CreateOrganizationAsync();
        var b = await _kit.CreateOrganizationAsync();
        await _kit.CreateUserAsync(b, SystemRoles.Staff);

        var aLogs = await a.Owner.GetJsonAsync<PagedResponse<AuditLogResponse>>("/api/v1/audit-logs?pageSize=100");
        var bLogs = await b.Owner.GetJsonAsync<PagedResponse<AuditLogResponse>>("/api/v1/audit-logs?pageSize=100");

        aLogs.Items.Should().NotBeEmpty();
        aLogs.Items.Where(l => l.UserEmail is not null).Select(l => l.UserEmail).Distinct().Should().OnlyContain(e => e == a.OwnerEmail);
        bLogs.Items.Select(l => l.Id).Should().NotIntersectWith(aLogs.Items.Select(l => l.Id));
        bLogs.TotalCount.Should().BeGreaterThan(aLogs.TotalCount, "B did more, and none of it appears in A's log");
    }

    [Fact]
    public async Task Organization_profile_and_settings_are_only_ever_the_callers_own()
    {
        var a = await _kit.CreateOrganizationAsync();
        var b = await _kit.CreateOrganizationAsync();

        var aOrg = await a.Owner.GetJsonAsync<OrganizationResponse>("/api/v1/organization");
        var bOrg = await b.Owner.GetJsonAsync<OrganizationResponse>("/api/v1/organization");

        aOrg.Id.Should().Be(a.Id);
        bOrg.Id.Should().Be(b.Id);
        aOrg.Name.Should().NotBe(bOrg.Name);

        using var update = await a.Owner.PutAsync("/api/v1/organization", new UpdateOrganizationRequest("A Renamed", null, null, null, a.OwnerEmail, null, null));
        await update.ShouldBeAsync(HttpStatusCode.OK);
        (await b.Owner.GetJsonAsync<OrganizationResponse>("/api/v1/organization")).Name.Should().Be(bOrg.Name);
    }

    [Fact]
    public async Task A_token_for_one_tenant_cannot_be_pointed_at_another_tenants_public_route()
    {
        var a = await _kit.CreateOrganizationAsync();
        var b = await _kit.CreateOrganizationAsync();

        using var own = await a.Owner.GetAsync($"/api/v1/public/organizations/{a.Slug}");
        await own.ShouldBeAsync(HttpStatusCode.OK);

        using var foreign = await a.Owner.GetAsync($"/api/v1/public/organizations/{b.Slug}");
        await foreign.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "tenant_mismatch");
    }

    [Fact]
    public async Task Anonymous_visitors_see_only_the_public_fields_of_the_organization_named_in_the_url()
    {
        var a = await _kit.CreateOrganizationAsync();
        using var visitor = _kit.NewSession();

        using var response = await visitor.GetAsync($"/api/v1/public/organizations/{a.Slug}", authenticated: false);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var org = await response.ReadAsync<PublicOrganizationResponse>();
        org.Slug.Should().Be(a.Slug);
        body.Should().NotContain(a.OwnerEmail).And.NotContain("contactEmail").And.NotContain("taxId").And.NotContain(a.Id.ToString());

        using var unknown = await visitor.GetAsync("/api/v1/public/organizations/does-not-exist-at-all", authenticated: false);
        await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Platform_operators_manage_the_registry_but_cannot_read_any_tenants_data()
    {
        var a = await _kit.CreateOrganizationAsync();
        var operatorSession = await _kit.LoginAsync(ApiFactory.SuperAdminEmail, ApiFactory.SuperAdminPassword);

        var registry = await operatorSession.GetJsonAsync<PagedResponse<PlatformOrganizationResponse>>($"/api/v1/platform/organizations?search={a.Slug}");
        registry.Items.Should().ContainSingle(o => o.Id == a.Id).Which.UserCount.Should().Be(1);

        // Least privilege: no tenant permission, and even hitting tenant endpoints yields nothing.
        (await operatorSession.GetAsync("/api/v1/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await operatorSession.GetAsync("/api/v1/roles")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The operator's own audit view holds platform events only, none of the tenant's activity.
        var logs = await operatorSession.GetJsonAsync<PagedResponse<AuditLogResponse>>("/api/v1/audit-logs?pageSize=100");
        logs.Items.Should().NotContain(l => l.UserEmail == a.OwnerEmail);
    }

    // ---- data-access level ---------------------------------------------------------------------------

    [Fact]
    public async Task With_a_tenant_scope_only_that_tenants_rows_are_visible()
    {
        var a = await _kit.CreateOrganizationAsync();
        var b = await _kit.CreateOrganizationAsync();
        await _kit.CreateUserAsync(b, SystemRoles.Staff);

        var (users, roles, orgs, logs, settings) = await _kit.WithDbAsync(a.Id, async (db, _) => (
            await db.Users.Select(u => u.TenantId).Distinct().ToListAsync(),
            await db.Roles.Select(r => r.TenantId).Distinct().ToListAsync(),
            await db.Organizations.Select(o => o.Id).ToListAsync(),
            await db.AuditLogs.Select(l => l.TenantId).Distinct().ToListAsync(),
            await db.OrganizationSettings.Select(s => s.TenantId).ToListAsync()));

        users.Should().Equal((Guid?)a.Id);
        roles.Should().Equal((Guid?)a.Id);
        orgs.Should().Equal(a.Id);
        logs.Should().Equal((Guid?)a.Id);
        settings.Should().Equal(a.Id);
    }

    [Fact]
    public async Task Without_any_scope_tenant_data_fails_closed()
    {
        await _kit.CreateOrganizationAsync();

        var counts = await _kit.WithDbAsync(null, async (db, _) => new[]
        {
            await db.Users.CountAsync(),
            await db.Roles.CountAsync(),
            await db.UserRoles.CountAsync(),
            await db.RolePermissions.CountAsync(),
            await db.UserSessions.CountAsync(),
            await db.UserTokens.CountAsync(),
            await db.AuditLogs.CountAsync(),
            await db.Organizations.CountAsync(),
            await db.OrganizationSettings.CountAsync(),
        });

        counts.Should().OnlyContain(c => c == 0, "a forgotten tenant scope must return nothing, never everything");
    }

    [Fact]
    public async Task Platform_scope_sees_the_organization_registry_and_platform_rows_but_no_tenant_rows()
    {
        var a = await _kit.CreateOrganizationAsync();

        var (orgIds, userTenantIds, roleTenantIds, settingsCount) = await _kit.WithDbAsync(null, async (db, _) => (
            await db.Organizations.Select(o => o.Id).ToListAsync(),
            await db.Users.Select(u => u.TenantId).Distinct().ToListAsync(),
            await db.Roles.Select(r => r.TenantId).Distinct().ToListAsync(),
            await db.OrganizationSettings.CountAsync()), platform: true);

        orgIds.Should().Contain(a.Id);
        userTenantIds.Should().Equal((Guid?)null);
        roleTenantIds.Should().Equal((Guid?)null);
        settingsCount.Should().Be(0, "organization settings are tenant data");
    }

    [Fact]
    public async Task Writing_a_row_that_belongs_to_another_tenant_is_blocked_by_the_data_layer()
    {
        var a = await _kit.CreateOrganizationAsync();
        var b = await _kit.CreateOrganizationAsync();

        var act = () => _kit.WithDbAsync(a.Id, async (db, _) =>
        {
            db.Roles.Add(Role.CreateCustom(b.Id, "Planted In B", null, [Permissions.Donor.Read]));
            return await db.SaveChangesAsync();
        });

        await act.Should().ThrowAsync<TenantViolationException>();
        var planted = await _kit.WithDbAsync(b.Id, async (db, _) => await db.Roles.AnyAsync(r => r.Name == "Planted In B"));
        planted.Should().BeFalse();
    }

    [Fact]
    public async Task Modifying_a_tenant_owned_row_without_a_matching_scope_is_blocked()
    {
        var a = await _kit.CreateOrganizationAsync();

        // A buggy job that loads settings while ignoring filters and saves them with no tenant scope at all.
        var unscoped = () => _kit.WithDbAsync(null, async (db, _) =>
        {
            var settings = await db.OrganizationSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == a.Id);
            settings.Update("UTC", "USD", "en-US", 1, null, null);
            return await db.SaveChangesAsync();
        });
        await unscoped.Should().ThrowAsync<TenantViolationException>();

        // The same edit under the wrong tenant's scope is blocked too.
        var b = await _kit.CreateOrganizationAsync();
        var wrongScope = () => _kit.WithDbAsync(b.Id, async (db, _) =>
        {
            var settings = await db.OrganizationSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == a.Id);
            settings.Update("UTC", "USD", "en-US", 1, null, null);
            return await db.SaveChangesAsync();
        });
        await wrongScope.Should().ThrowAsync<TenantViolationException>();

        var untouched = await a.Owner.GetJsonAsync<OrganizationResponse>("/api/v1/organization");
        untouched.Settings.TimeZoneId.Should().Be("America/New_York");
    }

    [Fact]
    public async Task A_tenant_cannot_modify_another_tenants_organization_record()
    {
        var a = await _kit.CreateOrganizationAsync();
        var b = await _kit.CreateOrganizationAsync();

        var act = () => _kit.WithDbAsync(a.Id, async (db, _) =>
        {
            var victim = await db.Organizations.IgnoreQueryFilters().SingleAsync(o => o.Id == b.Id);
            victim.Suspend("hostile takeover", DateTimeOffset.UtcNow);
            return await db.SaveChangesAsync();
        });

        await act.Should().ThrowAsync<TenantViolationException>();
    }

    [Fact]
    public async Task The_audit_log_is_append_only_at_the_data_layer()
    {
        var a = await _kit.CreateOrganizationAsync();

        var edit = () => _kit.WithDbAsync(a.Id, async (db, _) =>
        {
            var entry = await db.AuditLogs.FirstAsync();
            db.Entry(entry).Property(e => e.Action).CurrentValue = AuditActions.Logout;
            return await db.SaveChangesAsync();
        });
        await edit.Should().ThrowAsync<InvalidOperationException>().WithMessage("*append-only*");

        var delete = () => _kit.WithDbAsync(a.Id, async (db, _) =>
        {
            db.AuditLogs.Remove(await db.AuditLogs.FirstAsync());
            return await db.SaveChangesAsync();
        });
        await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage("*append-only*");
    }

    [Fact]
    public async Task A_scope_cannot_be_switched_to_a_different_tenant_mid_request()
    {
        var a = await _kit.CreateOrganizationAsync();
        var b = await _kit.CreateOrganizationAsync();

        var act = () => _kit.WithDbAsync(a.Id, (_, services) =>
        {
            services.GetRequiredService<FundFlow.Application.Common.Abstractions.ITenantScope>().UseTenant(b.Id);
            return Task.FromResult(0);
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
