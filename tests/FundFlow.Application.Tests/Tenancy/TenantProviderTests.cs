using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Tenancy;
using FundFlow.Infrastructure.Tenancy;
using Moq;

namespace FundFlow.Application.Tests.Tenancy;

public class TenantProviderTests
{
    private static readonly TenantInfo Hope = new(Guid.NewGuid(), "hope-foundation", "Hope Foundation", IsActive: true);
    private static readonly TenantInfo Riverside = new(Guid.NewGuid(), "riverside-rescue", "Riverside", IsActive: true);
    private static readonly TenantInfo Frozen = new(Guid.NewGuid(), "frozen-org", "Frozen", IsActive: false);

    private static TenantProvider Provider()
    {
        var directory = new Mock<ITenantDirectory>();
        foreach (var tenant in new[] { Hope, Riverside, Frozen })
        {
            directory.Setup(d => d.FindByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
            directory.Setup(d => d.FindBySlugAsync(tenant.Slug, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        }

        return new TenantProvider(directory.Object);
    }

    private static Task<TenantResolution> Resolve(bool authenticated, bool platform, Guid? claim, string? slug) =>
        Provider().ResolveAsync(new TenantRequestInfo(authenticated, platform, claim, slug), CancellationToken.None);

    [Fact]
    public async Task An_authenticated_user_is_scoped_to_the_tenant_in_their_token()
    {
        var resolution = await Resolve(true, false, Hope.Id, null);

        resolution.Status.Should().Be(TenantResolutionStatus.Resolved);
        resolution.Tenant.Should().Be(Hope);
    }

    [Fact]
    public async Task The_token_wins_when_the_url_names_the_same_tenant()
    {
        (await Resolve(true, false, Hope.Id, "HOPE-FOUNDATION")).Status.Should().Be(TenantResolutionStatus.Resolved);
    }

    [Fact]
    public async Task A_url_naming_another_tenant_is_a_cross_tenant_attempt()
    {
        var resolution = await Resolve(true, false, Hope.Id, Riverside.Slug);

        resolution.Status.Should().Be(TenantResolutionStatus.Mismatch);
    }

    [Fact]
    public async Task An_authenticated_tenant_user_without_a_tenant_claim_is_rejected_not_defaulted()
    {
        (await Resolve(true, false, null, Hope.Slug)).Status.Should().Be(TenantResolutionStatus.Mismatch);
    }

    [Fact]
    public async Task Platform_operators_get_platform_scope_and_ignore_url_hints()
    {
        (await Resolve(true, true, null, Hope.Slug)).Status.Should().Be(TenantResolutionStatus.Platform);
    }

    [Fact]
    public async Task A_suspended_tenant_is_reported_as_suspended()
    {
        (await Resolve(true, false, Frozen.Id, null)).Status.Should().Be(TenantResolutionStatus.Suspended);
        (await Resolve(false, false, null, Frozen.Slug)).Status.Should().Be(TenantResolutionStatus.Suspended);
    }

    [Fact]
    public async Task A_token_for_a_tenant_that_no_longer_exists_is_not_found()
    {
        (await Resolve(true, false, Guid.NewGuid(), null)).Status.Should().Be(TenantResolutionStatus.NotFound);
    }

    [Fact]
    public async Task Anonymous_requests_resolve_from_the_public_slug()
    {
        var resolution = await Resolve(false, false, null, Hope.Slug);

        resolution.Status.Should().Be(TenantResolutionStatus.Resolved);
        resolution.Tenant!.Id.Should().Be(Hope.Id);
    }

    [Fact]
    public async Task Anonymous_requests_for_unknown_slugs_are_not_found()
    {
        (await Resolve(false, false, null, "does-not-exist")).Status.Should().Be(TenantResolutionStatus.NotFound);
    }

    [Fact]
    public async Task Anonymous_requests_without_a_slug_stay_unscoped()
    {
        (await Resolve(false, false, null, null)).Status.Should().Be(TenantResolutionStatus.Anonymous);
    }

    [Fact]
    public void The_tenant_scope_can_be_chosen_once_and_never_widened_or_switched()
    {
        var tenantA = Guid.NewGuid();
        var scope = new TenantContext();

        scope.UseTenant(tenantA);
        scope.UseTenant(tenantA); // idempotent

        scope.TenantId.Should().Be(tenantA);
        scope.IsPlatformScope.Should().BeFalse();
        var switchTenant = () => scope.UseTenant(Guid.NewGuid());
        switchTenant.Should().Throw<InvalidOperationException>();
        scope.UsePlatformAction().Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Platform_scope_cannot_enter_a_tenant()
    {
        var scope = new TenantContext();
        scope.UsePlatform();

        var enter = () => scope.UseTenant(Guid.NewGuid());

        enter.Should().Throw<InvalidOperationException>();
        scope.IsPlatformScope.Should().BeTrue();
        scope.TenantId.Should().BeNull();
    }

    [Fact]
    public void UseScopeOf_routes_null_to_platform_and_ids_to_tenants()
    {
        var tenantId = Guid.NewGuid();
        var tenantScope = new TenantContext();
        var platformScope = new TenantContext();

        tenantScope.UseScopeOf(tenantId);
        platformScope.UseScopeOf(null);

        tenantScope.TenantId.Should().Be(tenantId);
        platformScope.IsPlatformScope.Should().BeTrue();
    }

    [Fact]
    public void An_unscoped_context_exposes_nothing()
    {
        var scope = new TenantContext();

        scope.TenantId.Should().BeNull();
        scope.IsPlatformScope.Should().BeFalse();
        new NoTenantContext().TenantId.Should().BeNull();
    }
}

internal static class TenantContextTestExtensions
{
    public static Action UsePlatformAction(this TenantContext scope) => scope.UsePlatform;
}
