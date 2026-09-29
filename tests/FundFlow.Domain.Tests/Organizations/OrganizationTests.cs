using FundFlow.Domain.Organizations;
using FundFlow.Domain.SharedKernel;

namespace FundFlow.Domain.Tests.Organizations;

public class OrganizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static Organization NewOrganization() =>
        Organization.Register("Hope Foundation", "hope-foundation", "hello@hope.org", "America/New_York", "usd");

    [Fact]
    public void Register_creates_an_active_tenant_with_default_settings()
    {
        var organization = NewOrganization();

        organization.Status.Should().Be(OrganizationStatus.Active);
        organization.Slug.Should().Be("hope-foundation");
        organization.Settings.TenantId.Should().Be(organization.Id, "the organization id is the tenant id");
        organization.Settings.CurrencyCode.Should().Be("USD");
        organization.Settings.TimeZoneId.Should().Be("America/New_York");
        organization.Settings.FiscalYearStartMonth.Should().Be(1);
        organization.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OrganizationRegisteredDomainEvent>();
    }

    [Theory]
    [InlineData("hope-foundation", true)]
    [InlineData("abc", true)]
    [InlineData("a1-b2-c3", true)]
    [InlineData("ab", false)]
    [InlineData("Hope-Foundation", false)]
    [InlineData("-hope", false)]
    [InlineData("hope-", false)]
    [InlineData("hope--foundation", false)]
    [InlineData("hope foundation", false)]
    [InlineData("hope_foundation", false)]
    [InlineData("admin", false)]
    [InlineData("api", false)]
    [InlineData("give", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Slug_validation(string? slug, bool valid)
    {
        Organization.IsValidSlug(slug).Should().Be(valid);
    }

    [Fact]
    public void Slug_length_is_capped()
    {
        Organization.IsValidSlug(new string('a', 50)).Should().BeTrue();
        Organization.IsValidSlug(new string('a', 51)).Should().BeFalse();
    }

    [Theory]
    [InlineData("Hope Foundation", "hope-foundation")]
    [InlineData("  St. Mary's  Food Bank!! ", "st-mary-s-food-bank")]
    [InlineData("Ünïcode Name", "n-code-name")]
    [InlineData("---", "")]
    public void Slugify_produces_url_safe_slugs(string name, string expected)
    {
        Organization.Slugify(name).Should().Be(expected);
    }

    [Fact]
    public void Slugify_truncates_without_leaving_a_trailing_hyphen()
    {
        var slug = Organization.Slugify("word " + string.Join(' ', Enumerable.Repeat("longer", 20)));

        slug.Length.Should().BeLessThanOrEqualTo(Organization.SlugMaxLength);
        slug.Should().NotEndWith("-");
    }

    [Fact]
    public void Register_rejects_reserved_or_invalid_slugs()
    {
        var reserved = () => Organization.Register("X", "support", "a@b.org", "UTC", "USD");
        var malformed = () => Organization.Register("X", "Not A Slug", "a@b.org", "UTC", "USD");

        reserved.Should().Throw<DomainException>().Which.Code.Should().Be("organization.slug_invalid");
        malformed.Should().Throw<DomainException>().Which.Code.Should().Be("organization.slug_invalid");
    }

    [Fact]
    public void Suspend_and_activate_toggle_the_lifecycle()
    {
        var organization = NewOrganization();
        organization.ClearDomainEvents();

        organization.Suspend("  Payment dispute ", Now);

        organization.Status.Should().Be(OrganizationStatus.Suspended);
        organization.IsActive.Should().BeFalse();
        organization.SuspendedAt.Should().Be(Now);
        organization.SuspensionReason.Should().Be("Payment dispute");
        organization.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OrganizationSuspendedDomainEvent>();

        organization.Activate();

        organization.IsActive.Should().BeTrue();
        organization.SuspendedAt.Should().BeNull();
        organization.SuspensionReason.Should().BeNull();
    }

    [Fact]
    public void Suspension_requires_a_reason_and_cannot_repeat()
    {
        var organization = NewOrganization();

        var noReason = () => organization.Suspend("  ", Now);
        noReason.Should().Throw<DomainException>().Which.Code.Should().Be("organization.suspension_reason_required");

        organization.Suspend("Abuse", Now);
        var twice = () => organization.Suspend("Abuse", Now);
        twice.Should().Throw<DomainException>().Which.Code.Should().Be("organization.already_suspended");

        var active = NewOrganization();
        var activateActive = active.Activate;
        activateActive.Should().Throw<DomainException>().Which.Code.Should().Be("organization.already_active");
    }

    [Fact]
    public void UpdateProfile_cleans_optional_fields()
    {
        var organization = NewOrganization();

        organization.UpdateProfile(" Hope ", "  ", " 12-3456789 ", null, " info@hope.org ", "", new Address("1 Main St", null, "Springfield", "IL", "62701", "US"));

        organization.Name.Should().Be("Hope");
        organization.LegalName.Should().BeNull();
        organization.TaxId.Should().Be("12-3456789");
        organization.ContactEmail.Should().Be("info@hope.org");
        organization.PhoneNumber.Should().BeNull();
        organization.Address.City.Should().Be("Springfield");
    }

    [Fact]
    public void Settings_validate_timezone_currency_month_and_colour()
    {
        var settings = NewOrganization().Settings;

        settings.Update("Europe/Berlin", "eur", "de-DE", 4, " https://cdn.example/logo.png ", "#1F5EFF");
        settings.CurrencyCode.Should().Be("EUR");
        settings.LogoUrl.Should().Be("https://cdn.example/logo.png");

        Action[] invalid =
        [
            () => settings.Update("Mars/Olympus_Mons", "USD", "en-US", 1, null, null),
            () => settings.Update("UTC", "US", "en-US", 1, null, null),
            () => settings.Update("UTC", "USDX", "en-US", 1, null, null),
            () => settings.Update("UTC", "USD", "en-US", 13, null, null),
            () => settings.Update("UTC", "USD", "en-US", 0, null, null),
            () => settings.Update("UTC", "USD", "en-US", 1, null, "blue"),
        ];

        foreach (var act in invalid)
        {
            act.Should().Throw<DomainException>();
        }
    }
}
