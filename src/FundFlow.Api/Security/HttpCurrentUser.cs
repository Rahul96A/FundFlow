using System.Security.Claims;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Infrastructure.Security;

namespace FundFlow.Api.Security;

/// <summary>Adapts the validated access token and HTTP request to <see cref="ICurrentUser"/>.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => ParseGuid(ClaimNames.Subject);

    public Guid? TenantId => ParseGuid(ClaimNames.TenantId);

    public Guid? SessionId => ParseGuid(ClaimNames.SessionId);

    public string? Email => IsAuthenticated ? Principal!.FindFirstValue(ClaimNames.Email) : null;

    public bool IsPlatformUser => IsAuthenticated && Principal!.FindFirstValue(ClaimNames.Scope) == ClaimNames.PlatformScope;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

    public string? CorrelationId => accessor.HttpContext?.Items[CorrelationIdMiddleware.ItemKey] as string;

    private Guid? ParseGuid(string claimType) =>
        IsAuthenticated && Guid.TryParse(Principal!.FindFirstValue(claimType), out var value) ? value : null;
}
