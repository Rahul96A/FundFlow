using System.Text.RegularExpressions;
using FundFlow.Domain.SharedKernel;

namespace FundFlow.Domain.Organizations;

/// <summary>Per-tenant configuration: timezone, currency, locale and branding.</summary>
public sealed partial class OrganizationSettings : AuditableEntity, ITenantEntity
{
    private OrganizationSettings()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>IANA timezone id, e.g. "America/New_York". Timestamps are stored in UTC and converted for display.</summary>
    public string TimeZoneId { get; private set; } = default!;

    /// <summary>ISO 4217 code, e.g. "USD". All monetary amounts for the tenant are denominated in this currency.</summary>
    public string CurrencyCode { get; private set; } = default!;

    public string Locale { get; private set; } = "en-US";

    /// <summary>1-12; the month the organization's fiscal year begins.</summary>
    public int FiscalYearStartMonth { get; private set; } = 1;

    public string? LogoUrl { get; private set; }

    /// <summary>Hex colour (#RRGGBB) used on public giving pages.</summary>
    public string? BrandColor { get; private set; }

    internal static OrganizationSettings CreateDefault(Guid tenantId, string timeZoneId, string currencyCode)
    {
        var settings = new OrganizationSettings { TenantId = tenantId };
        settings.Update(timeZoneId, currencyCode, "en-US", 1, null, null);
        return settings;
    }

    public void Update(
        string timeZoneId,
        string currencyCode,
        string locale,
        int fiscalYearStartMonth,
        string? logoUrl,
        string? brandColor)
    {
        if (!IsKnownTimeZone(timeZoneId))
        {
            throw new DomainException("settings.timezone_invalid", "The timezone is not recognised.");
        }

        if (!CurrencyPattern().IsMatch(currencyCode ?? string.Empty))
        {
            throw new DomainException("settings.currency_invalid", "The currency must be a three-letter ISO 4217 code.");
        }

        if (fiscalYearStartMonth is < 1 or > 12)
        {
            throw new DomainException("settings.fiscal_month_invalid", "The fiscal year start month must be between 1 and 12.");
        }

        if (!string.IsNullOrWhiteSpace(brandColor) && !ColorPattern().IsMatch(brandColor))
        {
            throw new DomainException("settings.color_invalid", "The brand colour must be a hex value like #1F6FEB.");
        }

        TimeZoneId = timeZoneId;
        CurrencyCode = currencyCode!.ToUpperInvariant();
        Locale = string.IsNullOrWhiteSpace(locale) ? "en-US" : locale.Trim();
        FiscalYearStartMonth = fiscalYearStartMonth;
        LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
        BrandColor = string.IsNullOrWhiteSpace(brandColor) ? null : brandColor.Trim();
    }

    private static bool IsKnownTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[A-Za-z]{3}$")]
    private static partial Regex CurrencyPattern();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();
}
