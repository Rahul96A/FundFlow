namespace FundFlow.Application.Common.Abstractions;

/// <summary>The authenticated caller and request metadata. Populated from the validated access token.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? TenantId { get; }

    /// <summary>The session (<c>sid</c> claim) the access token was issued for.</summary>
    Guid? SessionId { get; }

    string? Email { get; }

    /// <summary>True for platform operators (SUPER_ADMIN), who belong to no tenant.</summary>
    bool IsPlatformUser { get; }

    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }
}
