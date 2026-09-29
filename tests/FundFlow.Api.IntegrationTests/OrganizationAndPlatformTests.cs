using System.Net;
using FundFlow.Api.IntegrationTests.Infrastructure;
using FundFlow.Contracts.Common;
using FundFlow.Contracts.Identity;
using FundFlow.Contracts.Organizations;
using FundFlow.Domain.Audit;
using FundFlow.Domain.Identity;
using FundFlow.Contracts.Audit;

namespace FundFlow.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class OrganizationAndPlatformTests(ApiFixture fixture)
{
    private readonly TestKit _kit = new(fixture.Factory);

    // ---- organization profile and settings -----------------------------------------------------------

    [Fact]
    public async Task The_profile_can_be_updated_and_round_trips_including_the_address()
    {
        var org = await _kit.CreateOrganizationAsync();
        var address = new AddressDto("1 Main Street", "Suite 4", "Springfield", "IL", "62701", "US");

        using var response = await org.Owner.PutAsync("/api/v1/organization",
            new UpdateOrganizationRequest("Hope Renamed", "Hope Foundation Inc.", "12-3456789", "https://hope.example.org", "hello@hope.example.org", "+1 217 555 0100", address));

        var updated = await response.ExpectAsync<OrganizationResponse>();
        updated.Name.Should().Be("Hope Renamed");
        updated.LegalName.Should().Be("Hope Foundation Inc.");
        updated.Address.Should().Be(address);
        updated.Slug.Should().Be(org.Slug, "the public address never changes when the name does");
        var reread = await org.Owner.GetJsonAsync<OrganizationResponse>("/api/v1/organization");
        reread.Should().BeEquivalentTo(updated);
    }

    [Fact]
    public async Task Profile_validation_reports_each_field()
    {
        var org = await _kit.CreateOrganizationAsync();

        using var response = await org.Owner.PutAsync("/api/v1/organization",
            new UpdateOrganizationRequest("", null, null, "ftp://not-http.example", "not-an-email", "call me", null));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        problem.Errors.Should().ContainKeys("name", "website", "contactEmail", "phoneNumber");
    }

    [Fact]
    public async Task Settings_are_validated_and_reflected_in_the_current_user_context()
    {
        var org = await _kit.CreateOrganizationAsync();

        using var ok = await org.Owner.PutAsync("/api/v1/organization/settings",
            new UpdateOrganizationSettingsRequest("Europe/Berlin", "eur", "de-DE", 4, "https://cdn.example.org/logo.png", "#1F5EFF"));
        var updated = await ok.ExpectAsync<OrganizationResponse>();
        updated.Settings.Should().BeEquivalentTo(new OrganizationSettingsResponse("Europe/Berlin", "EUR", "de-DE", 4, "https://cdn.example.org/logo.png", "#1F5EFF"));

        var me = await org.Owner.GetJsonAsync<CurrentUserResponse>("/api/v1/auth/me");
        me.Organization!.TimeZoneId.Should().Be("Europe/Berlin");
        me.Organization.CurrencyCode.Should().Be("EUR");
        me.Organization.BrandColor.Should().Be("#1F5EFF");
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons", "USD", 1, null, "timeZoneId")]
    [InlineData("UTC", "US", 1, null, "currencyCode")]
    [InlineData("UTC", "USD", 13, null, "fiscalYearStartMonth")]
    [InlineData("UTC", "USD", 1, "blue", "brandColor")]
    public async Task Invalid_settings_are_rejected_with_the_offending_field(string timeZone, string currency, int month, string? color, string field)
    {
        var org = await _kit.CreateOrganizationAsync();

        using var response = await org.Owner.PutAsync("/api/v1/organization/settings",
            new UpdateOrganizationSettingsRequest(timeZone, currency, "en-US", month, null, color));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        problem.Errors.Should().ContainKey(field);
    }

    [Fact]
    public async Task Organization_changes_are_audited_with_before_and_after_values()
    {
        var org = await _kit.CreateOrganizationAsync();
        using var response = await org.Owner.PutAsync("/api/v1/organization/settings",
            new UpdateOrganizationSettingsRequest("Asia/Tokyo", "JPY", "ja-JP", 4, null, null));
        await response.ShouldBeAsync(HttpStatusCode.OK);

        var logs = await org.Owner.GetJsonAsync<PagedResponse<AuditLogResponse>>($"/api/v1/audit-logs?action={AuditActions.OrganizationSettingsUpdated}");

        var entry = logs.Items.Should().ContainSingle().Subject;
        entry.OldValues!.Value.GetProperty("currencyCode").GetString().Should().Be("USD");
        entry.NewValues!.Value.GetProperty("currencyCode").GetString().Should().Be("JPY");
        entry.NewValues.Value.GetProperty("timeZoneId").GetString().Should().Be("Asia/Tokyo");
    }

    [Fact]
    public async Task The_dashboard_overview_reflects_the_team_and_recent_activity_and_respects_permissions()
    {
        var org = await _kit.CreateOrganizationAsync();
        var staff = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        var email = $"pending-{TestKit.Unique("")}@example.test";
        var staffRole = await _kit.RoleIdAsync(org.Owner, SystemRoles.Staff);
        (await org.Owner.PostAsync("/api/v1/users", new InviteUserRequest(email, "P", "Ending", null, [staffRole]))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await org.Owner.PostAsync($"/api/v1/users/{staff.Id}/deactivate")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var overview = await org.Owner.GetJsonAsync<OrganizationOverviewResponse>("/api/v1/organization/overview");

        overview.ProfileCompleted.Should().BeFalse();
        overview.Team.Should().BeEquivalentTo(new TeamOverviewResponse(TotalUsers: 3, ActiveUsers: 2, PendingInvitations: 1, RoleCount: 10));
        overview.Activity!.Recent.Should().HaveCount(8);
        overview.Activity.ByDay.Should().HaveCount(14);
        overview.Activity.ByDay.Last().Date.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
        overview.Activity.ByDay.Sum(p => p.Count).Should().BeGreaterThan(5);

        // Staff can open the dashboard but sees neither team nor activity sections.
        var staffSession = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        var limited = await staffSession.Session.GetJsonAsync<OrganizationOverviewResponse>("/api/v1/organization/overview");
        limited.Team.Should().BeNull();
        limited.Activity.Should().BeNull();

        using var complete = await org.Owner.PutAsync("/api/v1/organization",
            new UpdateOrganizationRequest("Complete Org", null, null, null, org.OwnerEmail, "+1 555 0100", new AddressDto("1 Main St", null, "Springfield", "IL", "62701", "US")));
        await complete.ShouldBeAsync(HttpStatusCode.OK);
        (await org.Owner.GetJsonAsync<OrganizationOverviewResponse>("/api/v1/organization/overview")).ProfileCompleted.Should().BeTrue();
    }

    // ---- platform tenant management ------------------------------------------------------------------

    [Fact]
    public async Task Suspending_an_organization_locks_its_users_out_immediately_and_reactivating_restores_access()
    {
        var org = await _kit.CreateOrganizationAsync();
        var member = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        var operatorSession = await _kit.LoginAsync(ApiFactory.SuperAdminEmail, ApiFactory.SuperAdminPassword);
        (await member.Session.GetAsync("/api/v1/organization")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var withoutReason = await operatorSession.PostAsync($"/api/v1/platform/organizations/{org.Id}/suspend", new SuspendOrganizationRequest(""));
        await withoutReason.ShouldBeProblemAsync(HttpStatusCode.BadRequest);

        using var suspend = await operatorSession.PostAsync($"/api/v1/platform/organizations/{org.Id}/suspend", new SuspendOrganizationRequest("Unpaid invoice"));
        await suspend.ShouldBeAsync(HttpStatusCode.NoContent);

        // Already-issued access tokens stop working on the next request...
        using var blocked = await member.Session.GetAsync("/api/v1/organization");
        await blocked.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "organization_suspended");
        (await org.Owner.GetAsync("/api/v1/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // ...new sign-ins are refused, refresh is refused, and the public page disappears.
        using var anonymous = _kit.NewSession();
        using var login = await anonymous.PostAsync("/api/v1/auth/login", new LoginRequest(member.Email, member.Password), authenticated: false);
        await login.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "organization_suspended");
        using var publicPage = await anonymous.GetAsync($"/api/v1/public/organizations/{org.Slug}", authenticated: false);
        await publicPage.ShouldBeAsync(HttpStatusCode.NotFound);

        // The registry shows why, and a second suspension is a conflict.
        var registry = await operatorSession.GetJsonAsync<PagedResponse<PlatformOrganizationResponse>>($"/api/v1/platform/organizations?search={org.Slug}");
        var row = registry.Items.Should().ContainSingle().Subject;
        row.Status.Should().Be(FundFlow.Contracts.Organizations.OrganizationStatus.Suspended);
        row.SuspensionReason.Should().Be("Unpaid invoice");
        using var again = await operatorSession.PostAsync($"/api/v1/platform/organizations/{org.Id}/suspend", new SuspendOrganizationRequest("Again"));
        await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "organization.already_suspended");

        using var activate = await operatorSession.PostAsync($"/api/v1/platform/organizations/{org.Id}/activate");
        await activate.ShouldBeAsync(HttpStatusCode.NoContent);

        (await member.Session.GetAsync("/api/v1/organization")).StatusCode.Should().Be(HttpStatusCode.OK, "the same token works again once reactivated");
        (await _kit.LoginAsync(member.Email, member.Password)).AccessToken.Should().NotBeNullOrEmpty();

        var history = await org.Owner.GetJsonAsync<PagedResponse<AuditLogResponse>>("/api/v1/audit-logs?action=Organization.&pageSize=100");
        history.Items.Select(l => l.Action).Should().Contain([AuditActions.OrganizationSuspended, AuditActions.OrganizationActivated]);
    }

    [Fact]
    public async Task Suspension_affects_only_the_suspended_organization()
    {
        var suspended = await _kit.CreateOrganizationAsync();
        var bystander = await _kit.CreateOrganizationAsync();
        var operatorSession = await _kit.LoginAsync(ApiFactory.SuperAdminEmail, ApiFactory.SuperAdminPassword);

        (await operatorSession.PostAsync($"/api/v1/platform/organizations/{suspended.Id}/suspend", new SuspendOrganizationRequest("Abuse"))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await suspended.Owner.GetAsync("/api/v1/organization")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await bystander.Owner.GetAsync("/api/v1/organization")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_platform_registry_can_be_searched_filtered_and_sorted()
    {
        var name = $"Registry {TestKit.Unique("")}";
        var org = await _kit.CreateOrganizationAsync(name);
        var operatorSession = await _kit.LoginAsync(ApiFactory.SuperAdminEmail, ApiFactory.SuperAdminPassword);
        await operatorSession.PostAsync($"/api/v1/platform/organizations/{org.Id}/suspend", new SuspendOrganizationRequest("Testing filters"));

        var byName = await operatorSession.GetJsonAsync<PagedResponse<PlatformOrganizationResponse>>($"/api/v1/platform/organizations?search={Uri.EscapeDataString(name)}");
        var suspendedOnly = await operatorSession.GetJsonAsync<PagedResponse<PlatformOrganizationResponse>>("/api/v1/platform/organizations?status=Suspended&pageSize=100");
        var byNewest = await operatorSession.GetJsonAsync<PagedResponse<PlatformOrganizationResponse>>("/api/v1/platform/organizations?sortBy=createdAt&sortDirection=desc&pageSize=100");

        byName.Items.Should().ContainSingle(o => o.Id == org.Id);
        suspendedOnly.Items.Should().OnlyContain(o => o.Status == FundFlow.Contracts.Organizations.OrganizationStatus.Suspended).And.Contain(o => o.Id == org.Id);
        byNewest.Items.Select(o => o.CreatedAt).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Platform_endpoints_return_404_for_unknown_organizations_and_are_off_limits_to_tenants()
    {
        var org = await _kit.CreateOrganizationAsync();
        var operatorSession = await _kit.LoginAsync(ApiFactory.SuperAdminEmail, ApiFactory.SuperAdminPassword);

        using var unknown = await operatorSession.PostAsync($"/api/v1/platform/organizations/{Guid.NewGuid()}/suspend", new SuspendOrganizationRequest("x"));
        await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound);

        (await org.Owner.GetAsync("/api/v1/platform/organizations")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await org.Owner.PostAsync($"/api/v1/platform/organizations/{org.Id}/suspend", new SuspendOrganizationRequest("self-inflicted"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_platform_operator_profile_has_no_organization_and_only_platform_permissions()
    {
        var operatorSession = await _kit.LoginAsync(ApiFactory.SuperAdminEmail, ApiFactory.SuperAdminPassword);

        var me = await operatorSession.GetJsonAsync<CurrentUserResponse>("/api/v1/auth/me");

        me.IsPlatformUser.Should().BeTrue();
        me.Organization.Should().BeNull();
        me.Roles.Should().Equal(SystemRoles.SuperAdmin);
        me.Permissions.Should().BeEquivalentTo(Permissions.Platform.Manage, Permissions.Audit.Read);
        (await operatorSession.GetAsync("/api/v1/organization")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "the operator has no organization of their own");
    }
}
