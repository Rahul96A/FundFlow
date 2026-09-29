namespace FundFlow.Application.Common.Options;

/// <summary>Application-wide settings (section "App").</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Base URL of the web app; used to build the links in emails. No trailing slash.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:3000";

    public string ProductName { get; set; } = "FundFlow";

    public string SupportEmail { get; set; } = "support@fundflow.example";

    public string BuildLink(string path, params (string Key, string Value)[] query)
    {
        var baseUrl = PublicBaseUrl.TrimEnd('/');
        var queryString = query.Length == 0
            ? string.Empty
            : "?" + string.Join('&', query.Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value)}"));
        return $"{baseUrl}/{path.TrimStart('/')}{queryString}";
    }
}

/// <summary>Authentication and session policy (section "Auth").</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public int AccessTokenLifetimeMinutes { get; set; } = 15;

    /// <summary>Sliding window: an idle session dies after this long.</summary>
    public int RefreshTokenSlidingLifetimeDays { get; set; } = 14;

    /// <summary>Hard cap: even an always-active session must re-authenticate after this long.</summary>
    public int RefreshTokenAbsoluteLifetimeDays { get; set; } = 30;

    /// <summary>Window in which replaying the previous refresh token (two tabs racing) is tolerated, not treated as theft.</summary>
    public int RefreshReuseGraceSeconds { get; set; } = 10;

    public int MaxFailedAccessAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    public bool RequireConfirmedEmail { get; set; } = true;

    public int EmailVerificationTokenHours { get; set; } = 48;

    public int PasswordResetTokenMinutes { get; set; } = 60;

    public int InvitationTokenDays { get; set; } = 7;

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(AccessTokenLifetimeMinutes);

    public TimeSpan RefreshSlidingLifetime => TimeSpan.FromDays(RefreshTokenSlidingLifetimeDays);

    public TimeSpan RefreshAbsoluteLifetime => TimeSpan.FromDays(RefreshTokenAbsoluteLifetimeDays);

    public TimeSpan RefreshReuseGrace => TimeSpan.FromSeconds(RefreshReuseGraceSeconds);
}
