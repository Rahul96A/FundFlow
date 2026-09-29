using System.Net;
using FundFlow.Contracts.Common;
using FundFlow.Contracts.Identity;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FundFlow.Api.IntegrationTests.Infrastructure;

public sealed record AuthToken(string AccessToken, string TokenType, int ExpiresInSeconds, DateTimeOffset ExpiresAt);

public sealed record TestOrg(Guid Id, string Slug, string Name, string OwnerEmail, string Password, ApiSession Owner);

public sealed record TestUser(Guid Id, string Email, string Password, ApiSession Session);

/// <summary>
/// Builds realistic state through the public API only (register, receive the email, verify, log in, invite, accept),
/// so setup itself exercises the same paths customers use.
/// </summary>
public sealed class TestKit(ApiFactory factory)
{
    public const string Password = "Correct-Horse-Battery9";

    public ApiFactory Factory { get; } = factory;

    public static string Unique(string prefix = "t") => $"{prefix}{Guid.NewGuid():N}"[..14];

    public ApiSession NewSession() => new(Factory.CreateClient());

    public async Task<TestOrg> CreateOrganizationAsync(string? name = null, string? ownerEmail = null, string password = Password)
    {
        var suffix = Unique("");
        name ??= $"Test Org {suffix}";
        ownerEmail ??= $"owner-{suffix}@example.test";

        using var anonymous = NewSession();
        using var registered = await anonymous.PostAsync(
            "/api/v1/auth/register-organization",
            new RegisterOrganizationRequest(name, null, "Olivia", "Owner", ownerEmail, password, "America/New_York", "USD"),
            authenticated: false);
        var response = await registered.ExpectAsync<RegisterOrganizationResponse>(HttpStatusCode.Created);

        var email = await Factory.Emails.WaitForAsync(ownerEmail, "Verify your email");
        using var verified = await anonymous.PostAsync(
            "/api/v1/auth/verify-email",
            new VerifyEmailRequest(CapturingEmailSender.ExtractToken(email)),
            authenticated: false);
        await verified.ShouldBeAsync(HttpStatusCode.NoContent);

        var owner = await LoginAsync(ownerEmail, password);
        return new TestOrg(response.OrganizationId, response.Slug, name, ownerEmail, password, owner);
    }

    public async Task<ApiSession> LoginAsync(string email, string password = Password)
    {
        var session = NewSession();
        using var response = await session.PostAsync("/api/v1/auth/login", new LoginRequest(email, password), authenticated: false);
        var token = await response.ExpectAsync<AuthToken>();
        session.AccessToken = token.AccessToken;
        return session;
    }

    public async Task<Guid> RoleIdAsync(ApiSession admin, string roleName)
    {
        var roles = await admin.GetJsonAsync<List<RoleSummaryResponse>>("/api/v1/roles");
        return roles.Single(r => r.Name == roleName).Id;
    }

    /// <summary>Invites a user with the given system roles and completes the invitation, returning them signed in.</summary>
    public async Task<TestUser> CreateUserAsync(TestOrg org, params string[] roleNames)
    {
        var roleIds = new List<Guid>();
        foreach (var roleName in roleNames)
        {
            roleIds.Add(await RoleIdAsync(org.Owner, roleName));
        }

        return await CreateUserWithRolesAsync(org, org.Owner, roleIds);
    }

    public async Task<TestUser> CreateUserWithRolesAsync(TestOrg org, ApiSession inviter, IReadOnlyList<Guid> roleIds)
    {
        var suffix = Unique("");
        var email = $"user-{suffix}@example.test";

        using var invited = await inviter.PostAsync(
            "/api/v1/users",
            new InviteUserRequest(email, "Casey", "Colleague", null, roleIds));
        var user = await invited.ExpectAsync<UserDetailResponse>(HttpStatusCode.Created);

        var invitation = await Factory.Emails.WaitForAsync(email, "invited");
        using var anonymous = NewSession();
        using var accepted = await anonymous.PostAsync(
            "/api/v1/auth/accept-invitation",
            new AcceptInvitationRequest(CapturingEmailSender.ExtractToken(invitation), Password, null, null),
            authenticated: false);
        await accepted.ShouldBeAsync(HttpStatusCode.NoContent);

        return new TestUser(user.Id, email, Password, await LoginAsync(email));
    }

    /// <summary>Runs code inside a DI scope pinned to a tenant (or platform scope), exactly as a request would be.</summary>
    public async Task<T> WithDbAsync<T>(Guid? tenantId, Func<AppDbContext, IServiceProvider, Task<T>> action, bool platform = false)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantScope>();
        if (tenantId is { } id)
        {
            tenantScope.UseTenant(id);
        }
        else if (platform)
        {
            tenantScope.UsePlatform();
        }

        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider);
    }

    public async Task<PagedResponse<T>> PageAsync<T>(ApiSession session, string url) =>
        await session.GetJsonAsync<PagedResponse<T>>(url);
}
