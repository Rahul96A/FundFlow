using System.Net;
using System.Text;
using System.Text.Json;
using FundFlow.Api.IntegrationTests.Infrastructure;
using FundFlow.Contracts.Common;
using FundFlow.Contracts.Identity;
using FundFlow.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FundFlow.Api.IntegrationTests;

/// <summary>Cross-cutting API contract: error format, headers, pagination, health and documentation.</summary>
[Collection(ApiCollection.Name)]
public class ApiBehaviorTests(ApiFixture fixture)
{
    private readonly TestKit _kit = new(fixture.Factory);

    // ---- RFC 7807 problem details --------------------------------------------------------------------

    [Fact]
    public async Task Validation_failures_are_400_problems_with_a_camel_cased_errors_map()
    {
        using var session = _kit.NewSession();

        using var response = await session.PostAsync(
            "/api/v1/auth/register-organization",
            new RegisterOrganizationRequest("", "Bad Slug!", "", "", "not-an-email", "short", null, null),
            authenticated: false);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        problem.Type.Should().Be("https://api.fundflow.com/errors/validation");
        problem.Title.Should().Be("Validation failed");
        problem.Errors.Should().ContainKeys("organizationName", "organizationSlug", "firstName", "lastName", "email", "password");
        problem.Errors!["email"].Should().ContainSingle().Which.Should().Be("A valid email address is required.");
        problem.Errors["password"].Should().Contain(m => m.Contains("12 characters"));
        problem.Instance.Should().Be("/api/v1/auth/register-organization");
        problem.CorrelationId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Missing_resources_are_404_problems_and_never_leak_details()
    {
        var org = await _kit.CreateOrganizationAsync();

        using var response = await org.Owner.GetAsync($"/api/v1/users/{Guid.NewGuid()}");

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "not_found");
        problem.Type.Should().Be("https://api.fundflow.com/errors/not-found");
    }

    [Fact]
    public async Task Framework_generated_401_403_404_use_the_same_problem_shape()
    {
        var org = await _kit.CreateOrganizationAsync();
        var staff = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        using var anonymous = _kit.NewSession();

        (await (await anonymous.GetAsync("/api/v1/users", authenticated: false)).ShouldBeProblemAsync(HttpStatusCode.Unauthorized))
            .Type.Should().Be("https://api.fundflow.com/errors/unauthorized");
        (await (await staff.Session.GetAsync("/api/v1/users")).ShouldBeProblemAsync(HttpStatusCode.Forbidden))
            .Type.Should().Be("https://api.fundflow.com/errors/forbidden");
        (await (await org.Owner.GetAsync("/api/v1/definitely-not-a-route")).ShouldBeProblemAsync(HttpStatusCode.NotFound))
            .Type.Should().Be("https://api.fundflow.com/errors/not-found");
    }

    [Fact]
    public async Task Malformed_json_is_a_400_problem_not_a_500()
    {
        using var session = _kit.NewSession();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent("{ this is not json", Encoding.UTF8, "application/json"),
        };

        using var response = await session.Http.SendAsync(request);

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Wrong_types_and_bad_enum_values_are_400_problems()
    {
        var org = await _kit.CreateOrganizationAsync();

        using var badGuid = await org.Owner.GetAsync("/api/v1/users/not-a-guid");
        await badGuid.ShouldBeAsync(HttpStatusCode.NotFound); // route constraint :guid → no route matches

        using var badStatus = await org.Owner.GetAsync("/api/v1/users?status=Imaginary");
        await badStatus.ShouldBeProblemAsync(HttpStatusCode.BadRequest);

        using var badPage = await org.Owner.GetAsync("/api/v1/users?page=abc");
        await badPage.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unhandled_exceptions_become_a_generic_500_problem_without_internals()
    {
        using var session = _kit.NewSession();

        using var response = await session.GetAsync("/api/v1/_test/boom", authenticated: false);

        var raw = await response.Content.ReadAsStringAsync();
        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.InternalServerError, "internal_error");
        problem.Type.Should().Be("https://api.fundflow.com/errors/internal");
        problem.CorrelationId.Should().NotBeNullOrEmpty();
        raw.Should().NotContain("hunter2").And.NotContain("Server=prod").And.NotContain("InvalidOperationException").And.NotContain("   at ");
    }

    [Fact]
    public async Task Concurrent_edits_surface_as_409_and_business_rule_violations_as_422_with_their_code()
    {
        using var session = _kit.NewSession();

        using var concurrency = await session.GetAsync("/api/v1/_test/concurrency", authenticated: false);
        await concurrency.ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");

        using var rule = await session.GetAsync("/api/v1/_test/rule", authenticated: false);
        var problem = await rule.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "donation.amount_invalid");
        problem.Type.Should().Be("https://api.fundflow.com/errors/business-rule");
        problem.Detail.Should().Be("The donation amount must be greater than zero.");
    }

    // ---- headers -------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_response_carries_a_correlation_id_and_an_inbound_one_is_honoured_when_well_formed()
    {
        using var session = _kit.NewSession();

        using var generated = await session.GetAsync("/health/live", authenticated: false);
        generated.Headers.GetValues("X-Correlation-Id").Single().Should().NotBeNullOrWhiteSpace();

        using var honoured = await session.SendAsync(HttpMethod.Get, "/health/live", authenticated: false,
            headers: new Dictionary<string, string> { ["X-Correlation-Id"] = "trace-abc-12345" });
        honoured.Headers.GetValues("X-Correlation-Id").Single().Should().Be("trace-abc-12345");

        using var hostile = await session.SendAsync(HttpMethod.Get, "/health/live", authenticated: false,
            headers: new Dictionary<string, string> { ["X-Correlation-Id"] = "evil\"><script>alert(1)</script>" });
        hostile.Headers.GetValues("X-Correlation-Id").Single().Should().NotContain("<script>");
    }

    [Fact]
    public async Task The_correlation_id_appears_in_error_bodies()
    {
        using var session = _kit.NewSession();

        using var response = await session.SendAsync(HttpMethod.Post, "/api/v1/auth/login", new LoginRequest("", ""), authenticated: false,
            headers: new Dictionary<string, string> { ["X-Correlation-Id"] = "support-ticket-98765" });

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        problem.CorrelationId.Should().Be("support-ticket-98765");
    }

    [Fact]
    public async Task Api_responses_carry_security_headers_and_are_never_cacheable()
    {
        var org = await _kit.CreateOrganizationAsync();

        using var response = await org.Owner.GetAsync("/api/v1/auth/me");

        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().Equal("no-referrer");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("default-src 'none'").And.Contain("frame-ancestors 'none'");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.Contains("Server").Should().BeFalse("no framework fingerprint");
    }

    [Fact]
    public async Task Cross_origin_requests_are_only_allowed_for_configured_origins()
    {
        // The Testing environment configures no CORS origins, so a browser preflight must not be granted.
        using var session = _kit.NewSession();

        using var preflight = await session.SendAsync(HttpMethod.Options, "/api/v1/auth/login", authenticated: false,
            headers: new Dictionary<string, string>
            {
                ["Origin"] = "https://evil.example",
                ["Access-Control-Request-Method"] = "POST",
            });

        preflight.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Configured_origins_get_credentialed_cors_and_are_trusted_by_the_csrf_origin_check()
    {
        const string allowed = "https://app.fundflow.test";
        await using var factory = new ApiFactory(
            fixture.ServerConnectionString,
            new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = allowed });
        await factory.InitializeAsync();
        using var session = new ApiSession(factory.CreateClient());

        using var preflight = await session.SendAsync(HttpMethod.Options, "/api/v1/auth/refresh", authenticated: false,
            headers: new Dictionary<string, string>
            {
                ["Origin"] = allowed,
                ["Access-Control-Request-Method"] = "POST",
                ["Access-Control-Request-Headers"] = "x-fundflow-client,content-type",
            });
        // An explicit origin is echoed back, never '*' (credentialed CORS forbids the wildcard).
        preflight.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be(allowed);
        preflight.Headers.GetValues("Access-Control-Allow-Credentials").Should().ContainSingle().Which.Should().Be("true");
        string.Join(",", preflight.Headers.GetValues("Access-Control-Allow-Headers")).ToLowerInvariant().Should().Contain("x-fundflow-client");

        // The trusted origin passes the CSRF origin check (no cookie, so it stops at 401, not 403)...
        using var trusted = await session.SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", authenticated: false, webClient: true,
            headers: new Dictionary<string, string> { ["Origin"] = allowed });
        await trusted.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "invalid_refresh_token");

        // ...while any other origin is rejected outright.
        using var foreign = await session.SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", authenticated: false, webClient: true,
            headers: new Dictionary<string, string> { ["Origin"] = "https://evil.example" });
        await foreign.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "csrf_origin_rejected");
    }

    // ---- pagination, sorting, filtering --------------------------------------------------------------

    /// <summary>An organization plus <paramref name="extra"/> directly-seeded members (email domains are unique per call).</summary>
    private async Task<(TestOrg Org, string Domain)> OrgWithUsersAsync(int extra)
    {
        var org = await _kit.CreateOrganizationAsync();
        var domain = $"paging-{TestKit.Unique("")}.example.test";
        await _kit.WithDbAsync(org.Id, async (db, _) =>
        {
            for (var i = 0; i < extra; i++)
            {
                db.Users.Add(FundFlow.Domain.Identity.User.Provision(org.Id, $"member{i:D2}@{domain}", $"Member{i:D2}", $"Zed{(extra - i):D2}", "hash"));
            }

            await db.SaveChangesAsync();
            return 0;
        });
        return (org, domain);
    }

    [Fact]
    public async Task Lists_page_with_totals_and_a_stable_order()
    {
        var (org, _) = await OrgWithUsersAsync(12); // + the owner = 13

        var first = await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>("/api/v1/users?page=1&pageSize=5&sortBy=email&sortDirection=asc");
        var last = await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>("/api/v1/users?page=3&pageSize=5&sortBy=email&sortDirection=asc");
        var beyond = await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>("/api/v1/users?page=9&pageSize=5");

        first.Should().BeEquivalentTo(new { Page = 1, PageSize = 5, TotalCount = 13, TotalPages = 3 }, o => o.ExcludingMissingMembers());
        first.Items.Should().HaveCount(5);
        first.Items.Select(u => u.Email).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        last.Items.Should().HaveCount(3);
        beyond.Items.Should().BeEmpty();
        beyond.TotalCount.Should().Be(13);

        var everyone = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var p = await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>($"/api/v1/users?page={page}&pageSize=5&sortBy=email");
            everyone.AddRange(p.Items.Select(u => u.Id));
        }

        everyone.Should().OnlyHaveUniqueItems().And.HaveCount(13, "pages must neither overlap nor skip rows");
    }

    [Fact]
    public async Task Sorting_supports_both_directions_and_ignores_unknown_columns()
    {
        var (org, _) = await OrgWithUsersAsync(6);

        var byNameAsc = await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>("/api/v1/users?sortBy=name&sortDirection=asc&pageSize=100");
        var byNameDesc = await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>("/api/v1/users?sortBy=name&sortDirection=desc&pageSize=100");
        byNameAsc.Items.Select(u => u.LastName).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        byNameDesc.Items.Select(u => u.LastName).Should().BeInDescendingOrder(StringComparer.OrdinalIgnoreCase);

        using var injection = await org.Owner.GetAsync("/api/v1/users?sortBy=PasswordHash&sortDirection=asc");
        await injection.ShouldBeAsync(HttpStatusCode.OK);
        using var sqlish = await org.Owner.GetAsync("/api/v1/users?sortBy=email;DROP TABLE identity.Users");
        await sqlish.ShouldBeAsync(HttpStatusCode.OK);
        (await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>("/api/v1/users")).TotalCount.Should().Be(7);
    }

    [Fact]
    public async Task Page_size_is_capped_at_100_so_no_endpoint_returns_an_unbounded_collection()
    {
        var org = await _kit.CreateOrganizationAsync();

        var huge = await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>("/api/v1/users?pageSize=100000");
        var zero = await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>("/api/v1/users?pageSize=0&page=-3");

        huge.PageSize.Should().Be(100);
        zero.PageSize.Should().Be(1);
        zero.Page.Should().Be(1);
    }

    [Fact]
    public async Task Search_matches_names_and_emails_treats_wildcards_literally_and_filters_by_status_and_role()
    {
        var (org, domain) = await OrgWithUsersAsync(4);
        var staff = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        using var deactivate = await org.Owner.PostAsync($"/api/v1/users/{staff.Id}/deactivate");
        await deactivate.ShouldBeAsync(HttpStatusCode.NoContent);

        var byName = await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>($"/api/v1/users?search=member02%40{domain}");
        byName.Items.Should().ContainSingle().Which.Email.Should().Be($"member02@{domain}");

        var byFullName = await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>("/api/v1/users?search=Member03%20Zed");
        byFullName.TotalCount.Should().Be(1);

        var percent = await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>("/api/v1/users?search=%25");
        percent.TotalCount.Should().Be(0, "a literal percent sign is not a wildcard");
        var underscore = await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>("/api/v1/users?search=member_2");
        underscore.TotalCount.Should().Be(0, "an underscore is not a single-character wildcard");

        var deactivated = await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>("/api/v1/users?status=Deactivated");
        deactivated.Items.Should().ContainSingle().Which.Id.Should().Be(staff.Id);
        var active = await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>("/api/v1/users?status=Active&pageSize=100");
        active.Items.Should().OnlyContain(u => u.Status == UserStatus.Active).And.NotContain(u => u.Id == staff.Id);
        var staffRoleId = await _kit.RoleIdAsync(org.Owner, SystemRoles.Staff);
        var pendingEmail = $"pending-{TestKit.Unique("")}@example.test";
        (await org.Owner.PostAsync("/api/v1/users", new InviteUserRequest(pendingEmail, "Pen", "Ding", null, [staffRoleId]))).StatusCode.Should().Be(HttpStatusCode.Created);
        var invitedOnly = await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>("/api/v1/users?status=Invited&pageSize=100");
        invitedOnly.Items.Should().ContainSingle().Which.Email.Should().Be(pendingEmail);

        var adminRole = await _kit.RoleIdAsync(org.Owner, SystemRoles.OrganizationAdmin);
        var admins = await org.Owner.GetJsonAsync<PagedResponse<UserSummaryResponse>>($"/api/v1/users?roleId={adminRole}");
        admins.Items.Should().ContainSingle().Which.Email.Should().Be(org.OwnerEmail);

        var tooLong = await org.Owner.GetAsync($"/api/v1/users?search={new string('a', 101)}");
        await tooLong.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
    }

    // ---- health and documentation --------------------------------------------------------------------

    [Fact]
    public async Task Liveness_readiness_and_the_combined_health_endpoint_are_public_and_report_dependencies()
    {
        using var session = _kit.NewSession();

        using var live = await session.GetAsync("/health/live", authenticated: false);
        await live.ShouldBeAsync(HttpStatusCode.OK);

        using var ready = await session.GetAsync("/health/ready", authenticated: false);
        await ready.ShouldBeAsync(HttpStatusCode.OK);
        using var readyDoc = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
        readyDoc.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        var checks = readyDoc.RootElement.GetProperty("checks");
        checks.GetProperty("database").GetProperty("status").GetString().Should().Be("Healthy");
        checks.GetProperty("cache").GetProperty("status").GetString().Should().Be("Healthy");
        checks.TryGetProperty("masstransit-bus", out _).Should().BeTrue("the message bus is part of readiness");

        using var all = await session.GetAsync("/health", authenticated: false);
        await all.ShouldBeAsync(HttpStatusCode.OK);
        (await all.Content.ReadAsStringAsync()).Should().NotContain("Exception").And.NotContain("Server=");
    }

    [Fact]
    public async Task The_openapi_document_describes_auth_permissions_problem_responses_and_versioning()
    {
        using var session = _kit.NewSession();

        using var response = await session.GetAsync("/swagger/v1/swagger.json", authenticated: false);

        await response.ShouldBeAsync(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("info").GetProperty("title").GetString().Should().Be("FundFlow API");
        root.GetProperty("info").GetProperty("description").GetString().Should().Contain("Authentication").And.Contain("RFC 7807").And.Contain("Pagination");

        var paths = root.GetProperty("paths");
        foreach (var path in new[]
                 {
                     "/api/v1/auth/login", "/api/v1/auth/refresh", "/api/v1/users", "/api/v1/users/{id}/roles",
                     "/api/v1/roles", "/api/v1/permissions", "/api/v1/organization", "/api/v1/audit-logs",
                     "/api/v1/platform/organizations", "/api/v1/public/organizations/{tenantSlug}",
                 })
        {
            paths.TryGetProperty(path, out _).Should().BeTrue($"{path} must be documented");
        }

        root.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _).Should().BeTrue();

        var listUsers = paths.GetProperty("/api/v1/users").GetProperty("get");
        listUsers.GetProperty("description").GetString().Should().Contain("User.Read");
        listUsers.GetProperty("responses").TryGetProperty("401", out _).Should().BeTrue();
        listUsers.GetProperty("responses").TryGetProperty("403", out _).Should().BeTrue();
        listUsers.GetProperty("responses").GetProperty("400").GetProperty("content").TryGetProperty("application/problem+json", out _).Should().BeTrue();

        // Public operations do not advertise auth failures they cannot produce.
        paths.GetProperty("/api/v1/auth/login").GetProperty("post").GetProperty("responses").TryGetProperty("403", out _).Should().BeFalse();
    }

    [Fact]
    public async Task The_swagger_ui_is_served()
    {
        using var session = _kit.NewSession();

        using var ui = await session.GetAsync("/swagger/index.html", authenticated: false);

        await ui.ShouldBeAsync(HttpStatusCode.OK);
        ui.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("script-src 'self' 'unsafe-inline'");
    }

    [Fact]
    public async Task Api_versioning_is_part_of_the_url_and_an_unsupported_version_is_rejected()
    {
        var org = await _kit.CreateOrganizationAsync();

        using var v1 = await org.Owner.GetAsync("/api/v1/organization");
        v1.Headers.Contains("api-supported-versions").Should().BeTrue();

        using var v9 = await org.Owner.GetAsync("/api/v9/organization");
        v9.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Rate_limits_apply_to_authentication_endpoints_and_return_429_with_retry_after()
    {
        await using var limited = new ApiFactory(
            fixture.ServerConnectionString,
            new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = "true",
                ["RateLimiting:AuthPerMinute"] = "3",
            });
        await limited.InitializeAsync();
        using var session = new ApiSession(limited.CreateClient());

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 6; i++)
        {
            last?.Dispose();
            last = await session.PostAsync("/api/v1/auth/login", new LoginRequest("nobody@example.test", "Whatever-Passw0rd1"), authenticated: false);
            statuses.Add(last.StatusCode);
        }

        statuses.Take(3).Should().OnlyContain(s => s == HttpStatusCode.Unauthorized);
        statuses.Skip(3).Should().OnlyContain(s => s == HttpStatusCode.TooManyRequests);
        last.Should().NotBeNull();
        var problem = await last!.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited");
        problem.Type.Should().EndWith("/rate-limited");
        last.Headers.Contains("Retry-After").Should().BeTrue();
        last.Dispose();
    }

    [Fact]
    public async Task The_database_schema_matches_the_ef_model_with_no_pending_migrations()
    {
        var (pendingModelChanges, pendingMigrations, appliedCount) = await _kit.WithDbAsync(null, async (db, _) => (
            db.Database.HasPendingModelChanges(),
            (await db.Database.GetPendingMigrationsAsync()).ToList(),
            (await db.Database.GetAppliedMigrationsAsync()).Count()));

        pendingModelChanges.Should().BeFalse("every model change needs a migration (dotnet ef migrations add ...)");
        pendingMigrations.Should().BeEmpty();
        appliedCount.Should().BeGreaterThan(0);
    }
}
