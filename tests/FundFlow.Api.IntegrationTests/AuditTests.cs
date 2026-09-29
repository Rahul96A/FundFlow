using System.Net;
using FundFlow.Api.IntegrationTests.Infrastructure;
using FundFlow.Contracts.Audit;
using FundFlow.Contracts.Common;
using FundFlow.Contracts.Identity;
using FundFlow.Domain.Audit;
using FundFlow.Domain.Identity;

namespace FundFlow.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuditTests(ApiFixture fixture)
{
    private readonly TestKit _kit = new(fixture.Factory);

    private static Task<PagedResponse<AuditLogResponse>> Logs(ApiSession session, string query = "") =>
        session.GetJsonAsync<PagedResponse<AuditLogResponse>>($"/api/v1/audit-logs?pageSize=100{query}");

    [Fact]
    public async Task Sensitive_operations_are_recorded_with_actor_time_address_and_correlation()
    {
        var org = await _kit.CreateOrganizationAsync();
        var staffRole = await _kit.RoleIdAsync(org.Owner, SystemRoles.Staff);
        var financeRole = await _kit.RoleIdAsync(org.Owner, SystemRoles.FinanceManager);
        var user = await _kit.CreateUserAsync(org, SystemRoles.Staff);

        using var change = await org.Owner.SendAsync(
            HttpMethod.Put, $"/api/v1/users/{user.Id}/roles", new SetUserRolesRequest([financeRole]),
            headers: new Dictionary<string, string> { ["X-Correlation-Id"] = "audit-test-corr-1" });
        await change.ShouldBeAsync(HttpStatusCode.OK);

        var logs = await Logs(org.Owner, "&action=User.RolesChanged");

        var entry = logs.Items.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditActions.UserRolesChanged);
        entry.EntityType.Should().Be("User");
        entry.EntityId.Should().Be(user.Id.ToString());
        entry.UserEmail.Should().Be(org.OwnerEmail);
        entry.UserId.Should().NotBeNull();
        entry.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        entry.Timestamp.Offset.Should().Be(TimeSpan.Zero, "timestamps are stored and served in UTC");
        entry.CorrelationId.Should().Be("audit-test-corr-1");
        entry.OldValues!.Value.GetProperty("roles")[0].GetString().Should().Be(SystemRoles.Staff);
        entry.NewValues!.Value.GetProperty("roles")[0].GetString().Should().Be(SystemRoles.FinanceManager);
        _ = staffRole;
    }

    [Fact]
    public async Task The_full_account_lifecycle_leaves_an_audit_trail()
    {
        var org = await _kit.CreateOrganizationAsync();
        var user = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        (await org.Owner.PostAsync($"/api/v1/users/{user.Id}/deactivate")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await org.Owner.PostAsync($"/api/v1/users/{user.Id}/reactivate")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await org.Owner.PostAsync("/api/v1/roles", new CreateRoleRequest("Audit Probe", null, [Permissions.Donor.Read]))).StatusCode.Should().Be(HttpStatusCode.Created);
        using var anonymous = _kit.NewSession();
        (await anonymous.PostAsync("/api/v1/auth/login", new LoginRequest(user.Email, "Definitely-Wrong-1"), authenticated: false)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var actions = (await Logs(org.Owner)).Items.Select(l => l.Action).ToHashSet();

        actions.Should().Contain([
            AuditActions.OrganizationRegistered,
            AuditActions.EmailVerified,
            AuditActions.Login,
            AuditActions.LoginFailed,
            AuditActions.UserInvited,
            AuditActions.UserInvitationAccepted,
            AuditActions.UserDeactivated,
            AuditActions.UserReactivated,
            AuditActions.RoleCreated,
        ]);
    }

    [Fact]
    public async Task Logout_password_reset_and_session_revocation_are_audited()
    {
        var org = await _kit.CreateOrganizationAsync();
        var user = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        var other = await _kit.LoginAsync(user.Email);
        var otherId = (await other.GetJsonAsync<List<SessionResponse>>("/api/v1/auth/sessions")).Single(s => s.IsCurrent).Id;
        (await user.Session.DeleteAsync($"/api/v1/auth/sessions/{otherId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var anonymous = _kit.NewSession();
        (await anonymous.PostAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(user.Email), authenticated: false)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var token = CapturingEmailSender.ExtractToken(await fixture.Factory.Emails.WaitForAsync(user.Email, "Reset your"));
        (await anonymous.PostAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(token, "Fresh-Passw0rd-77"), authenticated: false)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var relogin = await _kit.LoginAsync(user.Email, "Fresh-Passw0rd-77");
        (await relogin.PostAsync("/api/v1/auth/logout", authenticated: false, webClient: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var actions = (await Logs(org.Owner, $"&userId={user.Id}")).Items.Select(l => l.Action).ToList();

        actions.Should().Contain([AuditActions.SessionRevoked, AuditActions.PasswordResetRequested, AuditActions.PasswordReset, AuditActions.Logout]);
    }

    [Fact]
    public async Task Audit_records_never_contain_passwords_tokens_or_hashes()
    {
        var org = await _kit.CreateOrganizationAsync();
        const string secretPassword = "Sup3r-Secret-Value-For-Audit!";
        var user = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        using var anonymous = _kit.NewSession();
        await anonymous.PostAsync("/api/v1/auth/login", new LoginRequest(user.Email, secretPassword), authenticated: false);
        await user.Session.PostAsync("/api/v1/auth/change-password", new ChangePasswordRequest(user.Password, secretPassword));
        await anonymous.PostAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(user.Email), authenticated: false);
        var resetMail = await fixture.Factory.Emails.WaitForAsync(user.Email, "Reset your");
        var resetToken = CapturingEmailSender.ExtractToken(resetMail);

        using var response = await org.Owner.GetAsync("/api/v1/audit-logs?pageSize=100");
        var raw = await response.Content.ReadAsStringAsync();

        raw.Should().NotContain(secretPassword).And.NotContain(user.Password).And.NotContain(TestKit.Password)
            .And.NotContain(resetToken).And.NotContain("AQAAAA", "no ASP.NET password hashes")
            .And.NotContain(org.Owner.AccessToken!);
        raw.ToLowerInvariant().Should().NotContain("refreshtoken").And.NotContain("\"passwordhash\"");
    }

    [Fact]
    public async Task Audit_search_filters_by_action_module_entity_user_and_date_range()
    {
        var org = await _kit.CreateOrganizationAsync();
        var user = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        var start = DateTimeOffset.UtcNow.AddMinutes(-10);

        var authOnly = await Logs(org.Owner, "&action=Auth.");
        authOnly.Items.Should().NotBeEmpty().And.OnlyContain(l => l.Action.StartsWith("Auth.", StringComparison.Ordinal));

        var exact = await Logs(org.Owner, "&action=Auth.Login");
        exact.Items.Should().OnlyContain(l => l.Action == AuditActions.Login, "an exact action must not also match Auth.LoginFailed");

        var byEntity = await Logs(org.Owner, "&entityType=Organization");
        byEntity.Items.Should().OnlyContain(l => l.EntityType == "Organization");

        var byUser = await Logs(org.Owner, $"&userId={user.Id}");
        byUser.Items.Should().NotBeEmpty().And.OnlyContain(l => l.UserId == user.Id);

        var bySearch = await Logs(org.Owner, $"&search={Uri.EscapeDataString(user.Email)}");
        bySearch.Items.Should().NotBeEmpty().And.OnlyContain(l => l.UserEmail == user.Email || l.EntityId == user.Id.ToString() || l.Action.Contains(user.Email, StringComparison.OrdinalIgnoreCase));

        var inRange = await Logs(org.Owner, $"&from={Uri.EscapeDataString(start.ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"))}");
        inRange.TotalCount.Should().BeGreaterThan(0);
        var future = await Logs(org.Owner, $"&from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"))}");
        future.TotalCount.Should().Be(0);

        using var inverted = await org.Owner.GetAsync($"/api/v1/audit-logs?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}");
        await inverted.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Audit_logs_list_newest_first_by_default_and_support_sorting()
    {
        var org = await _kit.CreateOrganizationAsync();
        await _kit.CreateUserAsync(org, SystemRoles.Staff);

        var newestFirst = await Logs(org.Owner);
        var oldestFirst = await Logs(org.Owner, "&sortBy=timestamp&sortDirection=asc");

        newestFirst.Items.Select(l => l.Timestamp).Should().BeInDescendingOrder();
        oldestFirst.Items.Select(l => l.Timestamp).Should().BeInAscendingOrder();
        oldestFirst.Items.First().Action.Should().Be(AuditActions.OrganizationRegistered);
    }

    [Fact]
    public async Task Failed_sign_ins_are_audited_against_the_account_they_targeted()
    {
        var org = await _kit.CreateOrganizationAsync();
        var user = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        using var attacker = _kit.NewSession();
        for (var i = 0; i < 3; i++)
        {
            await attacker.PostAsync("/api/v1/auth/login", new LoginRequest(user.Email, "Nope-Nope-Nope-" + i), authenticated: false);
        }

        var failures = await Logs(org.Owner, $"&action=Auth.LoginFailed&userId={user.Id}");
        var lockouts = await Logs(org.Owner, "&action=Auth.AccountLocked");

        failures.TotalCount.Should().Be(3);
        failures.Items.Should().OnlyContain(l => l.NewValues!.Value.GetProperty("reason").GetString() == "invalid_password");
        lockouts.Items.Should().ContainSingle(l => l.UserId == user.Id);
    }
}
