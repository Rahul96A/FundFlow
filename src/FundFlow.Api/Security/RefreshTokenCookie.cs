namespace FundFlow.Api.Security;

/// <summary>
/// The refresh token lives only in an HttpOnly, SameSite=Strict cookie scoped to the auth endpoints, so script
/// (including an XSS payload) can never read it, and other endpoints never receive it.
/// </summary>
public static class RefreshTokenCookie
{
    public const string Name = "ff_refresh";
    public const string Path = "/api/v1/auth";

    public static void Append(HttpResponse response, string token, DateTimeOffset expires) =>
        response.Cookies.Append(Name, token, Options(response.HttpContext, expires));

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, Options(response.HttpContext, expires: null));

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static CookieOptions Options(HttpContext context, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        // Secure whenever the request (after forwarded-header processing) came in over HTTPS. Plain HTTP is only
        // reachable in local development, where browsers treat localhost as a secure context anyway.
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expires,
        IsEssential = true,
    };
}
