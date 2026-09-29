namespace FundFlow.Contracts.Identity;

public sealed record RegisterOrganizationRequest(
    string OrganizationName,
    string? OrganizationSlug,
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? TimeZoneId,
    string? CurrencyCode);

public sealed record RegisterOrganizationResponse(
    Guid OrganizationId,
    string Slug,
    string Email,
    bool RequiresEmailVerification);

public sealed record LoginRequest(string Email, string Password);

/// <summary>
/// Returned by login/refresh. The refresh token is delivered separately in an HttpOnly cookie and is never
/// exposed to JavaScript.
/// </summary>
public sealed record AuthTokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresInSeconds,
    DateTimeOffset ExpiresAt);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record VerifyEmailRequest(string Token);

public sealed record ResendVerificationRequest(string Email);

public sealed record AcceptInvitationRequest(string Token, string Password, string? FirstName, string? LastName);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record UpdateProfileRequest(string FirstName, string LastName, string? PhoneNumber);

public sealed record CurrentOrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    string TimeZoneId,
    string CurrencyCode,
    string Locale,
    string? LogoUrl,
    string? BrandColor);

public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    string? PhoneNumber,
    bool EmailConfirmed,
    bool IsPlatformUser,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    CurrentOrganizationResponse? Organization);

public sealed record SessionResponse(
    Guid Id,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    bool IsCurrent);
