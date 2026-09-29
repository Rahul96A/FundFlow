using System.Text.Json;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Exceptions;
using FundFlow.Application.Identity.Services;
using FundFlow.Domain.Identity;
using FundFlow.Infrastructure.Auditing;
using FundFlow.Infrastructure.Caching;
using FundFlow.Infrastructure.Messaging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FundFlow.Application.Tests.Infrastructure;

public class InfrastructureServicesTests
{
    // ---- audit redaction -------------------------------------------------------------------------------

    [Fact]
    public void Audit_snapshots_are_serialised_as_camel_case_json()
    {
        var json = AuditLogger.Serialize(new { FirstName = "Ada", Roles = new[] { "STAFF" } });

        using var doc = JsonDocument.Parse(json!);
        doc.RootElement.GetProperty("firstName").GetString().Should().Be("Ada");
        doc.RootElement.GetProperty("roles")[0].GetString().Should().Be("STAFF");
    }

    [Fact]
    public void Anything_that_looks_like_a_secret_is_redacted_even_when_nested()
    {
        var json = AuditLogger.Serialize(new
        {
            Email = "ada@example.org",
            Password = "hunter2",
            NewPassword = "hunter3",
            PasswordHash = "AQAAAA...",
            RefreshToken = "abc",
            Nested = new { AccessToken = "def", ApiKey = "ghi", Fine = "visible" },
            Items = new[] { new { ClientSecret = "jkl", Name = "ok" } },
            SecurityStamp = "stamp",
            CardNumber = "4242424242424242",
            Cvv = "123",
        });

        json.Should().NotContain("hunter").And.NotContain("AQAAAA").And.NotContain("abc")
            .And.NotContain("def").And.NotContain("ghi").And.NotContain("jkl")
            .And.NotContain("4242").And.NotContain("stamp").And.NotContain("123");
        json.Should().Contain("ada@example.org").And.Contain("visible").And.Contain("\"name\":\"ok\"");
        json.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void Null_snapshots_serialise_to_null()
    {
        AuditLogger.Serialize(null).Should().BeNull();
    }

    // ---- cache -----------------------------------------------------------------------------------------

    private sealed record Sample(string Name, int[] Numbers);

    private static CacheService NewCache(IDistributedCache? inner = null) =>
        new(inner ?? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<CacheService>.Instance);

    [Fact]
    public async Task Cache_round_trips_objects_and_reports_existence()
    {
        var cache = NewCache();

        await cache.SetAsync("k", new Sample("ada", [1, 2]), TimeSpan.FromMinutes(1), CancellationToken.None);

        (await cache.GetAsync<Sample>("k", CancellationToken.None))!.Numbers.Should().Equal(1, 2);
        (await cache.ExistsAsync("k", CancellationToken.None)).Should().BeTrue();
        (await cache.ExistsAsync("missing", CancellationToken.None)).Should().BeFalse();

        await cache.RemoveAsync("k", CancellationToken.None);
        (await cache.GetAsync<Sample>("k", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task A_cache_outage_degrades_to_a_miss_instead_of_failing_the_request()
    {
        var broken = new Mock<IDistributedCache>();
        broken.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("redis down"));
        broken.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("redis down"));
        broken.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("redis down"));
        var cache = NewCache(broken.Object);

        (await cache.GetAsync<Sample>("k", CancellationToken.None)).Should().BeNull();
        (await cache.ExistsAsync("k", CancellationToken.None)).Should().BeFalse();
        await cache.Invoking(c => c.SetAsync("k", new Sample("a", []), TimeSpan.FromSeconds(1), CancellationToken.None)).Should().NotThrowAsync();
        await cache.Invoking(c => c.RemoveAsync("k", CancellationToken.None)).Should().NotThrowAsync();
    }

    // ---- email payload protection ----------------------------------------------------------------------

    [Fact]
    public void Email_payloads_are_encrypted_at_rest_and_round_trip()
    {
        var protector = new EmailPayloadProtector(new EphemeralDataProtectionProvider());
        var message = new EmailMessage("ada@example.org", "Ada", "Reset", "<a href=\"https://x/reset?token=SECRET-TOKEN\">go</a>", "https://x/reset?token=SECRET-TOKEN");

        var protectedPayload = protector.Protect(message);

        protectedPayload.Should().NotContain("SECRET-TOKEN").And.NotContain("ada@example.org");
        protector.Unprotect(protectedPayload).Should().Be(message);
    }

    [Fact]
    public void Tampered_email_payloads_are_rejected()
    {
        var protector = new EmailPayloadProtector(new EphemeralDataProtectionProvider());
        var payload = protector.Protect(new EmailMessage("a@b.org", null, "S", "<p/>", "t"));

        var act = () => protector.Unprotect(payload[..^4] + "AAAA");

        act.Should().Throw<Exception>();
    }

    // ---- role assignment guard -------------------------------------------------------------------------

    private static (RoleAssignmentGuard Guard, Mock<IUserAccessProvider> Access) NewGuard(Guid actorId, UserAccess actorAccess)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.UserId).Returns(actorId);
        var access = new Mock<IUserAccessProvider>();
        access.Setup(a => a.GetAsync(actorId, It.IsAny<CancellationToken>())).ReturnsAsync(actorAccess);
        return (new RoleAssignmentGuard(user.Object, access.Object, Mock.Of<IAppDbContext>()), access);
    }

    private static UserAccess Access(string[] roles, params string[] permissions) =>
        new(permissions.ToHashSet(), roles.ToHashSet());

    [Fact]
    public async Task Users_may_grant_permissions_they_hold()
    {
        var (guard, _) = NewGuard(Guid.NewGuid(), Access(["STAFF"], Permissions.User.Manage, Permissions.Donor.Read));

        await guard.Invoking(g => g.EnsureCanGrantAsync([Permissions.Donor.Read], CancellationToken.None)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Users_may_not_grant_permissions_they_lack()
    {
        var (guard, _) = NewGuard(Guid.NewGuid(), Access(["STAFF"], Permissions.User.Manage, Permissions.Donor.Read));

        var act = () => guard.EnsureCanGrantAsync([Permissions.Donor.Read, Permissions.Donation.Refund], CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<ForbiddenException>();
        thrown.Which.Code.Should().Be("privilege_escalation");
        thrown.Which.Message.Should().Contain("Donation.Refund").And.NotContain("Donor.Read");
    }

    [Fact]
    public async Task Administrators_may_grant_anything()
    {
        var (guard, _) = NewGuard(Guid.NewGuid(), Access([SystemRoles.OrganizationAdmin], Permissions.User.Manage));

        await guard.Invoking(g => g.EnsureCanGrantAsync([Permissions.Donation.Refund, Permissions.Audit.Read], CancellationToken.None)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Non_administrators_cannot_manage_administrators()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var (guard, access) = NewGuard(actor, Access(["STAFF"], Permissions.User.Manage));
        access.Setup(a => a.GetAsync(target, It.IsAny<CancellationToken>())).ReturnsAsync(Access([SystemRoles.OrganizationAdmin]));

        var act = () => guard.EnsureCanManageUserAsync(target, CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>()).Which.Code.Should().Be("privilege_escalation");
    }

    [Fact]
    public async Task Non_administrators_can_manage_ordinary_users_and_administrators_can_manage_anyone()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var (guard, access) = NewGuard(actor, Access(["STAFF"], Permissions.User.Manage));
        access.Setup(a => a.GetAsync(target, It.IsAny<CancellationToken>())).ReturnsAsync(Access(["VOLUNTEER"]));
        await guard.Invoking(g => g.EnsureCanManageUserAsync(target, CancellationToken.None)).Should().NotThrowAsync();

        var (adminGuard, adminAccess) = NewGuard(actor, Access([SystemRoles.OrganizationAdmin]));
        adminAccess.Setup(a => a.GetAsync(target, It.IsAny<CancellationToken>())).ReturnsAsync(Access([SystemRoles.OrganizationAdmin]));
        await adminGuard.Invoking(g => g.EnsureCanManageUserAsync(target, CancellationToken.None)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task An_unauthenticated_actor_cannot_grant_anything()
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.UserId).Returns((Guid?)null);
        var guard = new RoleAssignmentGuard(user.Object, Mock.Of<IUserAccessProvider>(), Mock.Of<IAppDbContext>());

        await guard.Invoking(g => g.EnsureCanGrantAsync([Permissions.Donor.Read], CancellationToken.None)).Should().ThrowAsync<UnauthorizedException>();
    }
}
