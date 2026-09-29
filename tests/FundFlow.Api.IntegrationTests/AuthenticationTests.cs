using System.Net;
using FundFlow.Api.IntegrationTests.Infrastructure;
using FundFlow.Contracts.Identity;
using FundFlow.Domain.Identity;

namespace FundFlow.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthenticationTests(ApiFixture fixture)
{
    private readonly ApiFactory _factory = fixture.Factory;
    private readonly TestKit _kit = new(fixture.Factory);

    // ---- registration and email verification ---------------------------------------------------------

    [Fact]
    public async Task Registering_creates_a_tenant_whose_owner_must_verify_their_email_before_signing_in()
    {
        var suffix = TestKit.Unique("");
        var email = $"founder-{suffix}@example.test";
        using var session = _kit.NewSession();

        using var registered = await session.PostAsync(
            "/api/v1/auth/register-organization",
            new RegisterOrganizationRequest($"Sunrise Shelter {suffix}", null, "Fran", "Founder", email, TestKit.Password, "Europe/London", "GBP"),
            authenticated: false);
        var body = await registered.ExpectAsync<RegisterOrganizationResponse>(HttpStatusCode.Created);
        body.RequiresEmailVerification.Should().BeTrue();
        body.Slug.Should().StartWith("sunrise-shelter-");

        // Sign-in is refused until the address is verified, with a code the UI can act on.
        using var early = await session.PostAsync("/api/v1/auth/login", new LoginRequest(email, TestKit.Password), authenticated: false);
        await early.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "email_not_verified");

        // The verification email arrives asynchronously through the outbox and bus.
        var mail = await _factory.Emails.WaitForAsync(email, "Verify your email");
        mail.HtmlBody.Should().Contain("https://app.fundflow.test/verify-email?token=");
        var token = CapturingEmailSender.ExtractToken(mail);

        using var verified = await session.PostAsync("/api/v1/auth/verify-email", new VerifyEmailRequest(token), authenticated: false);
        await verified.ShouldBeAsync(HttpStatusCode.NoContent);

        // A link is single-use.
        using var replay = await session.PostAsync("/api/v1/auth/verify-email", new VerifyEmailRequest(token), authenticated: false);
        var problem = await replay.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        problem.Errors.Should().ContainKey("token");

        // Now sign-in works and the user is an administrator of their own organization.
        var owner = await _kit.LoginAsync(email);
        var me = await owner.GetJsonAsync<CurrentUserResponse>("/api/v1/auth/me");
        me.Email.Should().Be(email);
        me.Roles.Should().Equal(SystemRoles.OrganizationAdmin);
        me.Permissions.Should().BeEquivalentTo(Permissions.TenantScoped.Select(p => p.Name));
        me.IsPlatformUser.Should().BeFalse();
        me.Organization!.Slug.Should().Be(body.Slug);
        me.Organization.TimeZoneId.Should().Be("Europe/London");
        me.Organization.CurrencyCode.Should().Be("GBP");
    }

    [Fact]
    public async Task Registration_creates_the_default_roles_for_the_new_tenant()
    {
        var org = await _kit.CreateOrganizationAsync();

        var roles = await org.Owner.GetJsonAsync<List<RoleSummaryResponse>>("/api/v1/roles");

        roles.Select(r => r.Name).Should().BeEquivalentTo(RoleTemplates.Tenant.Select(t => t.Name));
        roles.Should().OnlyContain(r => r.IsSystem);
        roles.Single(r => r.Name == SystemRoles.OrganizationAdmin).UserCount.Should().Be(1);
    }

    [Fact]
    public async Task Registering_a_taken_email_or_slug_is_a_field_level_conflict()
    {
        var org = await _kit.CreateOrganizationAsync();
        using var session = _kit.NewSession();

        using var duplicateEmail = await session.PostAsync(
            "/api/v1/auth/register-organization",
            new RegisterOrganizationRequest("Another Org", null, "A", "B", org.OwnerEmail.ToUpperInvariant(), TestKit.Password, null, null),
            authenticated: false);
        var emailProblem = await duplicateEmail.ShouldBeProblemAsync(HttpStatusCode.Conflict, "email_taken");
        emailProblem.Errors.Should().ContainKey("email");

        using var duplicateSlug = await session.PostAsync(
            "/api/v1/auth/register-organization",
            new RegisterOrganizationRequest("Another Org", org.Slug, "A", "B", $"x-{TestKit.Unique()}@example.test", TestKit.Password, null, null),
            authenticated: false);
        var slugProblem = await duplicateSlug.ShouldBeProblemAsync(HttpStatusCode.Conflict, "slug_taken");
        slugProblem.Errors.Should().ContainKey("organizationSlug");
    }

    [Fact]
    public async Task Organizations_with_the_same_name_get_distinct_slugs()
    {
        var name = $"Twin Charity {TestKit.Unique("")}";

        var first = await _kit.CreateOrganizationAsync(name);
        var second = await _kit.CreateOrganizationAsync(name);

        first.Slug.Should().NotBe(second.Slug);
        second.Slug.Should().Be(first.Slug + "-2");
    }

    [Fact]
    public async Task Verification_can_be_requested_again_and_the_previous_link_stops_working()
    {
        var suffix = TestKit.Unique("");
        var email = $"resend-{suffix}@example.test";
        using var session = _kit.NewSession();
        using var registered = await session.PostAsync(
            "/api/v1/auth/register-organization",
            new RegisterOrganizationRequest($"Resend Org {suffix}", null, "R", "S", email, TestKit.Password, null, null),
            authenticated: false);
        await registered.ShouldBeAsync(HttpStatusCode.Created);
        var first = CapturingEmailSender.ExtractToken(await _factory.Emails.WaitForAsync(email, "Verify"));

        using var resend = await session.PostAsync("/api/v1/auth/resend-verification", new ResendVerificationRequest(email), authenticated: false);
        await resend.ShouldBeAsync(HttpStatusCode.Accepted);
        var second = CapturingEmailSender.ExtractToken(await _factory.Emails.WaitForAsync(email, "Verify", skip: 1));

        second.Should().NotBe(first);
        using var oldLink = await session.PostAsync("/api/v1/auth/verify-email", new VerifyEmailRequest(first), authenticated: false);
        await oldLink.ShouldBeAsync(HttpStatusCode.BadRequest);
        using var newLink = await session.PostAsync("/api/v1/auth/verify-email", new VerifyEmailRequest(second), authenticated: false);
        await newLink.ShouldBeAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Resending_verification_never_reveals_whether_an_address_is_registered()
    {
        using var session = _kit.NewSession();

        using var unknown = await session.PostAsync("/api/v1/auth/resend-verification", new ResendVerificationRequest("nobody-here@example.test"), authenticated: false);

        await unknown.ShouldBeAsync(HttpStatusCode.Accepted);
        _factory.Emails.CountFor("nobody-here@example.test").Should().Be(0);
    }

    // ---- credentials ---------------------------------------------------------------------------------

    [Fact]
    public async Task Wrong_password_and_unknown_account_are_indistinguishable()
    {
        var org = await _kit.CreateOrganizationAsync();
        using var session = _kit.NewSession();

        using var wrongPassword = await session.PostAsync("/api/v1/auth/login", new LoginRequest(org.OwnerEmail, "Not-The-Password-1"), authenticated: false);
        using var unknownAccount = await session.PostAsync("/api/v1/auth/login", new LoginRequest("ghost@example.test", "Not-The-Password-1"), authenticated: false);

        var a = await wrongPassword.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "invalid_credentials");
        var b = await unknownAccount.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "invalid_credentials");
        a.Detail.Should().Be(b.Detail);
    }

    [Fact]
    public async Task Email_addresses_are_case_insensitive_at_sign_in()
    {
        var org = await _kit.CreateOrganizationAsync();

        var session = await _kit.LoginAsync(org.OwnerEmail.ToUpperInvariant());

        session.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_even_against_the_correct_password_until_an_admin_unlocks_it()
    {
        var org = await _kit.CreateOrganizationAsync();
        var victim = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        using var attacker = _kit.NewSession();

        for (var i = 0; i < 3; i++) // Auth:MaxFailedAccessAttempts = 3 in the test host
        {
            using var attempt = await attacker.PostAsync("/api/v1/auth/login", new LoginRequest(victim.Email, "Guess-Number-" + i), authenticated: false);
            await attempt.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "invalid_credentials");
        }

        using var locked = await attacker.PostAsync("/api/v1/auth/login", new LoginRequest(victim.Email, victim.Password), authenticated: false);
        await locked.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "account_locked");

        var detail = await org.Owner.GetJsonAsync<UserDetailResponse>($"/api/v1/users/{victim.Id}");
        detail.Status.Should().Be(UserStatus.Locked);

        using var unlock = await org.Owner.PostAsync($"/api/v1/users/{victim.Id}/unlock");
        await unlock.ShouldBeAsync(HttpStatusCode.NoContent);

        var session = await _kit.LoginAsync(victim.Email, victim.Password);
        session.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_successful_login_resets_the_failure_counter()
    {
        var org = await _kit.CreateOrganizationAsync();
        var user = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        using var session = _kit.NewSession();

        for (var round = 0; round < 3; round++)
        {
            for (var i = 0; i < 2; i++)
            {
                using var bad = await session.PostAsync("/api/v1/auth/login", new LoginRequest(user.Email, "Wrong-Password-" + i), authenticated: false);
                await bad.ShouldBeAsync(HttpStatusCode.Unauthorized);
            }

            using var good = await session.PostAsync("/api/v1/auth/login", new LoginRequest(user.Email, user.Password), authenticated: false);
            await good.ShouldBeAsync(HttpStatusCode.OK); // never locked: 2 failures + success, three times over
        }
    }

    // ---- tokens and sessions -------------------------------------------------------------------------

    [Fact]
    public async Task The_refresh_token_is_an_httponly_strict_cookie_scoped_to_the_auth_endpoints_and_absent_from_the_body()
    {
        var org = await _kit.CreateOrganizationAsync();
        using var session = _kit.NewSession();

        using var response = await session.PostAsync("/api/v1/auth/login", new LoginRequest(org.OwnerEmail, org.Password), authenticated: false);

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("ff_refresh=", StringComparison.Ordinal));
        cookie.Should().Contain("httponly").And.Contain("samesite=strict").And.Contain("path=/api/v1/auth");
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("refresh", "the refresh token must never be readable from JavaScript");
        raw.Should().Contain("accessToken");
    }

    [Fact]
    public async Task Refreshing_rotates_the_token_and_replaying_an_old_one_after_the_grace_period_kills_the_session()
    {
        var org = await _kit.CreateOrganizationAsync();
        using var session = _kit.NewSession();
        using var login = await session.PostAsync("/api/v1/auth/login", new LoginRequest(org.OwnerEmail, org.Password), authenticated: false);
        var oldCookie = ReadRefreshCookie(login);

        using var rotated = await session.PostAsync("/api/v1/auth/refresh", authenticated: false, webClient: true);
        var token = await rotated.ExpectAsync<AuthToken>();
        var newCookie = ReadRefreshCookie(rotated);
        newCookie.Should().NotBe(oldCookie, "refresh tokens rotate on every use");

        // The new access token works.
        session.AccessToken = token.AccessToken;
        (await session.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        // An attacker replays the stolen (now rotated-away) token after the grace period (2s in the test host).
        await Task.Delay(TimeSpan.FromSeconds(3));
        using var thief = _kit.NewSession();
        using var replay = await thief.SendAsync(
            HttpMethod.Post, "/api/v1/auth/refresh", authenticated: false, webClient: true,
            headers: new Dictionary<string, string> { ["Cookie"] = $"ff_refresh={oldCookie}" });
        await replay.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "invalid_refresh_token");

        // Reuse means compromise: the legitimate holder's newest token and access token are dead too.
        using var victim = await session.PostAsync("/api/v1/auth/refresh", authenticated: false, webClient: true);
        await victim.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "invalid_refresh_token");
        (await session.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Two_tabs_racing_a_refresh_within_the_grace_period_do_not_log_the_user_out()
    {
        var org = await _kit.CreateOrganizationAsync();
        using var session = _kit.NewSession();
        using var login = await session.PostAsync("/api/v1/auth/login", new LoginRequest(org.OwnerEmail, org.Password), authenticated: false);
        var original = ReadRefreshCookie(login);

        using var first = await session.PostAsync("/api/v1/auth/refresh", authenticated: false, webClient: true);
        await first.ShouldBeAsync(HttpStatusCode.OK);

        // The second tab still holds the old cookie value and sends it moments later (its own request, its own jar).
        using var secondTab = _kit.NewSession();
        using var second = await secondTab.SendAsync(
            HttpMethod.Post, "/api/v1/auth/refresh", authenticated: false, webClient: true,
            headers: new Dictionary<string, string> { ["Cookie"] = $"ff_refresh={original}" });
        await second.ShouldBeAsync(HttpStatusCode.Unauthorized);

        // The session survives: the browser's current cookie still refreshes.
        using var third = await session.PostAsync("/api/v1/auth/refresh", authenticated: false, webClient: true);
        await third.ShouldBeAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_cookie_endpoints_require_the_web_client_header_and_a_trusted_origin()
    {
        var org = await _kit.CreateOrganizationAsync();
        using var session = _kit.NewSession();
        using var login = await session.PostAsync("/api/v1/auth/login", new LoginRequest(org.OwnerEmail, org.Password), authenticated: false);
        await login.ShouldBeAsync(HttpStatusCode.OK);

        using var noHeader = await session.PostAsync("/api/v1/auth/refresh", authenticated: false);
        await noHeader.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "csrf_header_missing");

        using var foreignOrigin = await session.SendAsync(
            HttpMethod.Post, "/api/v1/auth/refresh", authenticated: false, webClient: true,
            headers: new Dictionary<string, string> { ["Origin"] = "https://evil.example" });
        await foreignOrigin.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "csrf_origin_rejected");

        using var noHeaderLogout = await session.PostAsync("/api/v1/auth/logout", authenticated: false);
        await noHeaderLogout.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "csrf_header_missing");
    }

    [Fact]
    public async Task Refreshing_without_a_cookie_is_unauthorised_and_clears_nothing_dangerous()
    {
        using var session = _kit.NewSession();

        using var response = await session.PostAsync("/api/v1/auth/refresh", authenticated: false, webClient: true);

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "invalid_refresh_token");
    }

    [Fact]
    public async Task Logging_out_kills_the_access_token_immediately_not_at_its_expiry()
    {
        var org = await _kit.CreateOrganizationAsync();
        using var session = _kit.NewSession();
        using var login = await session.PostAsync("/api/v1/auth/login", new LoginRequest(org.OwnerEmail, org.Password), authenticated: false);
        session.AccessToken = (await login.ReadAsync<AuthToken>()).AccessToken;
        (await session.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var logout = await session.PostAsync("/api/v1/auth/logout", authenticated: false, webClient: true);
        await logout.ShouldBeAsync(HttpStatusCode.NoContent);

        (await session.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var refresh = await session.PostAsync("/api/v1/auth/refresh", authenticated: false, webClient: true);
        await refresh.ShouldBeAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logging_out_twice_is_harmless()
    {
        using var session = _kit.NewSession();

        using var first = await session.PostAsync("/api/v1/auth/logout", authenticated: false, webClient: true);
        using var second = await session.PostAsync("/api/v1/auth/logout", authenticated: false, webClient: true);

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Sessions_are_listed_per_device_and_can_be_revoked_individually()
    {
        var org = await _kit.CreateOrganizationAsync();
        var laptop = await _kit.LoginAsync(org.OwnerEmail);
        var phone = await _kit.LoginAsync(org.OwnerEmail);

        var sessions = await laptop.GetJsonAsync<List<SessionResponse>>("/api/v1/auth/sessions");

        sessions.Count.Should().BeGreaterThanOrEqualTo(3); // owner login from setup + laptop + phone
        sessions.Count(s => s.IsCurrent).Should().Be(1);

        var phoneSessions = await phone.GetJsonAsync<List<SessionResponse>>("/api/v1/auth/sessions");
        var phoneId = phoneSessions.Single(s => s.IsCurrent).Id;

        using var revoke = await laptop.DeleteAsync($"/api/v1/auth/sessions/{phoneId}");
        await revoke.ShouldBeAsync(HttpStatusCode.NoContent);

        (await phone.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await laptop.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sessions_of_other_users_cannot_be_revoked()
    {
        var org = await _kit.CreateOrganizationAsync();
        var other = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        var theirSession = (await other.Session.GetJsonAsync<List<SessionResponse>>("/api/v1/auth/sessions")).Single(s => s.IsCurrent);

        using var response = await org.Owner.DeleteAsync($"/api/v1/auth/sessions/{theirSession.Id}");

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound);
        (await other.Session.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Requests_with_a_forged_or_garbage_token_are_unauthorised()
    {
        var org = await _kit.CreateOrganizationAsync();
        var forged = org.Owner.AccessToken![..^4] + "AAAA";
        using var session = _kit.NewSession();

        foreach (var token in new[] { forged, "not-a-jwt", "a.b.c" })
        {
            session.AccessToken = token;
            (await session.GetAsync("/api/v1/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    // ---- password reset and change -------------------------------------------------------------------

    [Fact]
    public async Task Password_reset_replaces_the_password_ends_every_session_and_the_link_is_single_use()
    {
        var org = await _kit.CreateOrganizationAsync();
        var user = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        const string newPassword = "Brand-New-Passw0rd!";
        using var anonymous = _kit.NewSession();

        using var forgot = await anonymous.PostAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(user.Email), authenticated: false);
        await forgot.ShouldBeAsync(HttpStatusCode.Accepted);
        var mail = await _factory.Emails.WaitForAsync(user.Email, "Reset your");
        var token = CapturingEmailSender.ExtractToken(mail);

        using var weak = await anonymous.PostAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(token, "short"), authenticated: false);
        var weakProblem = await weak.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        weakProblem.Errors.Should().ContainKey("newPassword");

        using var reset = await anonymous.PostAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(token, newPassword), authenticated: false);
        await reset.ShouldBeAsync(HttpStatusCode.NoContent);

        (await user.Session.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a reset signs the account out everywhere");
        using var oldPassword = await anonymous.PostAsync("/api/v1/auth/login", new LoginRequest(user.Email, user.Password), authenticated: false);
        await oldPassword.ShouldBeAsync(HttpStatusCode.Unauthorized);
        (await _kit.LoginAsync(user.Email, newPassword)).AccessToken.Should().NotBeNullOrEmpty();

        using var replay = await anonymous.PostAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(token, "Another-Passw0rd!x"), authenticated: false);
        await replay.ShouldBeAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Forgot_password_is_silent_for_unknown_addresses()
    {
        using var anonymous = _kit.NewSession();

        using var response = await anonymous.PostAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest("nobody@example.test"), authenticated: false);

        await response.ShouldBeAsync(HttpStatusCode.Accepted);
        await Task.Delay(1500); // give the outbox a chance to (wrongly) deliver something
        _factory.Emails.CountFor("nobody@example.test").Should().Be(0);
    }

    [Fact]
    public async Task Changing_the_password_requires_the_current_one_and_keeps_only_this_device_signed_in()
    {
        var org = await _kit.CreateOrganizationAsync();
        var thisDevice = await _kit.LoginAsync(org.OwnerEmail);
        var otherDevice = await _kit.LoginAsync(org.OwnerEmail);
        const string newPassword = "Changed-Passw0rd-42";

        using var wrong = await thisDevice.PostAsync("/api/v1/auth/change-password", new ChangePasswordRequest("Wrong-Current-1", newPassword));
        var wrongProblem = await wrong.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        wrongProblem.Errors.Should().ContainKey("currentPassword");

        using var same = await thisDevice.PostAsync("/api/v1/auth/change-password", new ChangePasswordRequest(org.Password, org.Password));
        await same.ShouldBeAsync(HttpStatusCode.BadRequest);

        using var ok = await thisDevice.PostAsync("/api/v1/auth/change-password", new ChangePasswordRequest(org.Password, newPassword));
        await ok.ShouldBeAsync(HttpStatusCode.NoContent);

        (await thisDevice.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await otherDevice.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _kit.LoginAsync(org.OwnerEmail, newPassword)).AccessToken.Should().NotBeNullOrEmpty();
    }

    // ---- invitations ---------------------------------------------------------------------------------

    [Fact]
    public async Task Invited_users_cannot_sign_in_until_they_accept_and_then_hold_exactly_the_invited_roles()
    {
        var org = await _kit.CreateOrganizationAsync();
        var email = $"invitee-{TestKit.Unique("")}@example.test";
        var financeRole = await _kit.RoleIdAsync(org.Owner, SystemRoles.FinanceManager);

        using var invite = await org.Owner.PostAsync("/api/v1/users", new InviteUserRequest(email, "Ivy", "Invitee", "+1 555 0100", [financeRole]));
        var invited = await invite.ExpectAsync<UserDetailResponse>(HttpStatusCode.Created);
        invited.Status.Should().Be(UserStatus.Invited);
        invited.Roles.Select(r => r.Name).Should().Equal(SystemRoles.FinanceManager);

        using var early = _kit.NewSession();
        using var tooEarly = await early.PostAsync("/api/v1/auth/login", new LoginRequest(email, TestKit.Password), authenticated: false);
        await tooEarly.ShouldBeAsync(HttpStatusCode.Unauthorized);

        var mail = await _factory.Emails.WaitForAsync(email, "invited");
        mail.Subject.Should().Contain(org.Name);
        using var accept = await early.PostAsync(
            "/api/v1/auth/accept-invitation",
            new AcceptInvitationRequest(CapturingEmailSender.ExtractToken(mail), TestKit.Password, "Ivy", "Renamed"),
            authenticated: false);
        await accept.ShouldBeAsync(HttpStatusCode.NoContent);

        var session = await _kit.LoginAsync(email);
        var me = await session.GetJsonAsync<CurrentUserResponse>("/api/v1/auth/me");
        me.LastName.Should().Be("Renamed");
        me.Roles.Should().Equal(SystemRoles.FinanceManager);
        me.Permissions.Should().Contain(Permissions.Donation.Refund).And.NotContain(Permissions.User.Manage);
        me.Organization!.Id.Should().Be(org.Id);
    }

    [Fact]
    public async Task Inviting_an_existing_email_is_a_conflict_even_across_organizations()
    {
        var orgA = await _kit.CreateOrganizationAsync();
        var orgB = await _kit.CreateOrganizationAsync();
        var staff = await _kit.RoleIdAsync(orgA.Owner, SystemRoles.Staff);

        using var response = await orgA.Owner.PostAsync("/api/v1/users", new InviteUserRequest(orgB.OwnerEmail, "X", "Y", null, [staff]));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "email_taken");
        problem.Errors.Should().ContainKey("email");
        problem.Detail.Should().NotContain(orgB.Name, "the message must not leak that the address belongs to another organization");
    }

    [Fact]
    public async Task An_invitation_can_be_resent_and_only_the_newest_link_works()
    {
        var org = await _kit.CreateOrganizationAsync();
        var email = $"again-{TestKit.Unique("")}@example.test";
        var staff = await _kit.RoleIdAsync(org.Owner, SystemRoles.Staff);
        using var invite = await org.Owner.PostAsync("/api/v1/users", new InviteUserRequest(email, "Al", "Again", null, [staff]));
        var user = await invite.ExpectAsync<UserDetailResponse>(HttpStatusCode.Created);
        var first = CapturingEmailSender.ExtractToken(await _factory.Emails.WaitForAsync(email, "invited"));

        using var resend = await org.Owner.PostAsync($"/api/v1/users/{user.Id}/resend-invitation");
        await resend.ShouldBeAsync(HttpStatusCode.Accepted);
        var second = CapturingEmailSender.ExtractToken(await _factory.Emails.WaitForAsync(email, "invited", skip: 1));

        using var anonymous = _kit.NewSession();
        using var stale = await anonymous.PostAsync("/api/v1/auth/accept-invitation", new AcceptInvitationRequest(first, TestKit.Password, null, null), authenticated: false);
        await stale.ShouldBeAsync(HttpStatusCode.BadRequest);
        using var fresh = await anonymous.PostAsync("/api/v1/auth/accept-invitation", new AcceptInvitationRequest(second, TestKit.Password, null, null), authenticated: false);
        await fresh.ShouldBeAsync(HttpStatusCode.NoContent);

        using var again = await org.Owner.PostAsync($"/api/v1/users/{user.Id}/resend-invitation");
        await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "user.invitation_already_accepted");
    }

    [Fact]
    public async Task Deactivated_users_lose_access_immediately_and_cannot_sign_in_again_until_reactivated()
    {
        var org = await _kit.CreateOrganizationAsync();
        var user = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        (await user.Session.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var deactivate = await org.Owner.PostAsync($"/api/v1/users/{user.Id}/deactivate");
        await deactivate.ShouldBeAsync(HttpStatusCode.NoContent);

        (await user.Session.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var anonymous = _kit.NewSession();
        using var login = await anonymous.PostAsync("/api/v1/auth/login", new LoginRequest(user.Email, user.Password), authenticated: false);
        await login.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "account_disabled");

        using var reactivate = await org.Owner.PostAsync($"/api/v1/users/{user.Id}/reactivate");
        await reactivate.ShouldBeAsync(HttpStatusCode.NoContent);
        (await _kit.LoginAsync(user.Email, user.Password)).AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Users_can_update_their_own_profile()
    {
        var org = await _kit.CreateOrganizationAsync();

        using var response = await org.Owner.PutAsync("/api/v1/auth/me", new UpdateProfileRequest("Olivia-Jane", "Owner-Smith", "+44 20 7946 0958"));

        var me = await response.ExpectAsync<CurrentUserResponse>();
        me.FullName.Should().Be("Olivia-Jane Owner-Smith");
        me.PhoneNumber.Should().Be("+44 20 7946 0958");

        using var invalid = await org.Owner.PutAsync("/api/v1/auth/me", new UpdateProfileRequest("", "X", "call me"));
        var problem = await invalid.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        problem.Errors.Should().ContainKeys("firstName", "phoneNumber");
    }

    private static string ReadRefreshCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith("ff_refresh=", StringComparison.Ordinal))
            .Split(';')[0]["ff_refresh=".Length..];
}
