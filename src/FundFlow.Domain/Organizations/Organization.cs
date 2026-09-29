using System.Text.RegularExpressions;
using FundFlow.Domain.SharedKernel;

namespace FundFlow.Domain.Organizations;

public enum OrganizationStatus
{
    Active = 1,
    Suspended = 2,
}

public sealed record Address(
    string? Line1,
    string? Line2,
    string? City,
    string? Region,
    string? PostalCode,
    string? Country)
{
    public static Address Empty { get; } = new(null, null, null, null, null, null);
}

public sealed record OrganizationRegisteredDomainEvent(Guid OrganizationId, string Slug, string Name) : DomainEvent;

public sealed record OrganizationSuspendedDomainEvent(Guid OrganizationId, string Reason) : DomainEvent;

public sealed record OrganizationActivatedDomainEvent(Guid OrganizationId) : DomainEvent;

/// <summary>
/// A nonprofit customer of the platform. An organization <em>is</em> a tenant: its <see cref="Entity.Id"/> is the
/// TenantId stamped on every tenant-owned row.
/// </summary>
public sealed partial class Organization : AuditableEntity
{
    public const int SlugMinLength = 3;
    public const int SlugMaxLength = 50;

    /// <summary>Slugs that would collide with application routes or look like platform-owned names.</summary>
    private static readonly HashSet<string> ReservedSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "api", "app", "assets", "auth", "billing", "campaigns", "dashboard", "docs", "donate",
        "donations", "donors", "events", "fundflow", "give", "health", "help", "login", "logout", "platform",
        "register", "root", "settings", "static", "status", "support", "swagger", "system", "www",
    };

    private Organization()
    {
    }

    public string Name { get; private set; } = default!;
    public string Slug { get; private set; } = default!;
    public string? LegalName { get; private set; }
    public string? TaxId { get; private set; }
    public string? Website { get; private set; }
    public string ContactEmail { get; private set; } = default!;
    public string? PhoneNumber { get; private set; }
    public Address Address { get; private set; } = Address.Empty;
    public OrganizationStatus Status { get; private set; } = OrganizationStatus.Active;
    public DateTimeOffset? SuspendedAt { get; private set; }
    public string? SuspensionReason { get; private set; }

    public OrganizationSettings Settings { get; private set; } = default!;

    public bool IsActive => Status == OrganizationStatus.Active;

    public static Organization Register(
        string name,
        string slug,
        string contactEmail,
        string timeZoneId,
        string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("organization.name_required", "An organization name is required.");
        }

        EnsureSlugIsValid(slug);

        var organization = new Organization
        {
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            ContactEmail = contactEmail.Trim(),
        };
        organization.Settings = OrganizationSettings.CreateDefault(organization.Id, timeZoneId, currencyCode);
        organization.Raise(new OrganizationRegisteredDomainEvent(organization.Id, organization.Slug, organization.Name));
        return organization;
    }

    public void UpdateProfile(
        string name,
        string? legalName,
        string? taxId,
        string? website,
        string contactEmail,
        string? phoneNumber,
        Address address)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("organization.name_required", "An organization name is required.");
        }

        Name = name.Trim();
        LegalName = Clean(legalName);
        TaxId = Clean(taxId);
        Website = Clean(website);
        ContactEmail = contactEmail.Trim();
        PhoneNumber = Clean(phoneNumber);
        Address = address;
    }

    public void Suspend(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("organization.suspension_reason_required", "A reason is required to suspend an organization.");
        }

        if (Status == OrganizationStatus.Suspended)
        {
            throw new DomainException("organization.already_suspended", "This organization is already suspended.");
        }

        Status = OrganizationStatus.Suspended;
        SuspendedAt = now;
        SuspensionReason = reason.Trim();
        Raise(new OrganizationSuspendedDomainEvent(Id, SuspensionReason));
    }

    public void Activate()
    {
        if (Status == OrganizationStatus.Active)
        {
            throw new DomainException("organization.already_active", "This organization is already active.");
        }

        Status = OrganizationStatus.Active;
        SuspendedAt = null;
        SuspensionReason = null;
        Raise(new OrganizationActivatedDomainEvent(Id));
    }

    public static bool IsValidSlug(string? slug) =>
        !string.IsNullOrWhiteSpace(slug)
        && slug.Length is >= SlugMinLength and <= SlugMaxLength
        && SlugPattern().IsMatch(slug)
        && !ReservedSlugs.Contains(slug);

    /// <summary>Derives a URL-safe slug from a display name ("Hope Foundation" -> "hope-foundation").</summary>
    public static string Slugify(string name)
    {
        var lower = name.Trim().ToLowerInvariant();
        var hyphenated = NonSlugCharacters().Replace(lower, "-").Trim('-');
        hyphenated = RepeatedHyphens().Replace(hyphenated, "-");
        if (hyphenated.Length > SlugMaxLength)
        {
            hyphenated = hyphenated[..SlugMaxLength].TrimEnd('-');
        }

        return hyphenated;
    }

    private static void EnsureSlugIsValid(string slug)
    {
        if (!IsValidSlug(slug))
        {
            throw new DomainException(
                "organization.slug_invalid",
                $"The organization address must be {SlugMinLength}-{SlugMaxLength} characters of lowercase letters, digits or hyphens, " +
                "must not start or end with a hyphen, and must not be a reserved word.");
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugCharacters();

    [GeneratedRegex("-{2,}")]
    private static partial Regex RepeatedHyphens();
}
