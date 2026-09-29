using System.Text.Json.Serialization;

namespace FundFlow.Contracts.Organizations;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrganizationStatus
{
    Active = 1,
    Suspended = 2,
}

public sealed record AddressDto(
    string? Line1,
    string? Line2,
    string? City,
    string? Region,
    string? PostalCode,
    string? Country);

public sealed record OrganizationSettingsResponse(
    string TimeZoneId,
    string CurrencyCode,
    string Locale,
    int FiscalYearStartMonth,
    string? LogoUrl,
    string? BrandColor);

public sealed record OrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    string? LegalName,
    string? TaxId,
    string? Website,
    string ContactEmail,
    string? PhoneNumber,
    AddressDto Address,
    OrganizationStatus Status,
    OrganizationSettingsResponse Settings,
    DateTimeOffset CreatedAt);

public sealed record UpdateOrganizationRequest(
    string Name,
    string? LegalName,
    string? TaxId,
    string? Website,
    string ContactEmail,
    string? PhoneNumber,
    AddressDto? Address);

public sealed record UpdateOrganizationSettingsRequest(
    string TimeZoneId,
    string CurrencyCode,
    string Locale,
    int FiscalYearStartMonth,
    string? LogoUrl,
    string? BrandColor);

/// <summary>What an anonymous visitor may learn about an organization (public giving pages).</summary>
public sealed record PublicOrganizationResponse(
    string Name,
    string Slug,
    string? LogoUrl,
    string? BrandColor,
    string CurrencyCode,
    string TimeZoneId);

public sealed record TeamOverviewResponse(int TotalUsers, int ActiveUsers, int PendingInvitations, int RoleCount);

public sealed record ActivityItemResponse(
    Guid Id,
    DateTimeOffset Timestamp,
    string Action,
    string EntityType,
    string? EntityId,
    string? UserEmail);

public sealed record ActivityPointResponse(DateOnly Date, int Count);

public sealed record ActivityOverviewResponse(
    IReadOnlyList<ActivityItemResponse> Recent,
    IReadOnlyList<ActivityPointResponse> ByDay);

/// <summary>Dashboard payload. Sections are null when the caller lacks the permission to see them.</summary>
public sealed record OrganizationOverviewResponse(
    bool ProfileCompleted,
    TeamOverviewResponse? Team,
    ActivityOverviewResponse? Activity);

public sealed record PlatformOrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    OrganizationStatus Status,
    string ContactEmail,
    int UserCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SuspendedAt,
    string? SuspensionReason);

public sealed record SuspendOrganizationRequest(string Reason);
