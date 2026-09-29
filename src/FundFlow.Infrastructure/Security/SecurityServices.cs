using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FundFlow.Infrastructure.Security;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>PBKDF2 work factor. OWASP's 2023 guidance for PBKDF2-HMAC-SHA512 is 210,000.</summary>
    public int PasswordHashIterations { get; set; } = 210_000;
}

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "https://api.fundflow.local";

    public string Audience { get; set; } = "fundflow-web";

    /// <summary>HMAC signing key. Must come from a secret store or environment variable, never source control.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public byte[] GetSigningKeyBytes() => Encoding.UTF8.GetBytes(SigningKey);
}

public static class ClaimNames
{
    public const string Subject = "sub";
    public const string SessionId = "sid";
    public const string TenantId = "tenant_id";
    public const string Scope = "scope";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";

    public const string TenantScope = "tenant";
    public const string PlatformScope = "platform";
}

/// <summary>Password hashing with ASP.NET Core Identity's PBKDF2 hasher (versioned format, transparent rehash).</summary>
public sealed class PasswordService : IPasswordService
{
    private static readonly object HasherUser = new();
    private readonly PasswordHasher<object> _hasher;
    private readonly Lazy<string> _dummyHash;

    public PasswordService(IOptions<SecurityOptions> options)
    {
        _hasher = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions
        {
            IterationCount = options.Value.PasswordHashIterations,
        }));
        _dummyHash = new Lazy<string>(() => _hasher.HashPassword(HasherUser, Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))));
    }

    public string Hash(string password) => _hasher.HashPassword(HasherUser, password);

    public PasswordVerification Verify(string hash, string password) =>
        _hasher.VerifyHashedPassword(HasherUser, hash, password) switch
        {
            PasswordVerificationResult.Success => PasswordVerification.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerification.SuccessRehashNeeded,
            _ => PasswordVerification.Failed,
        };

    public void SimulateVerification(string password) => _ = _hasher.VerifyHashedPassword(HasherUser, _dummyHash.Value, password);
}

public sealed class SecretTokens : ISecretTokens
{
    /// <summary>256 bits of randomness, URL-safe so it can sit in a link or cookie unescaped.</summary>
    public string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>SHA-256 is appropriate here: the inputs are high-entropy random secrets, not passwords.</summary>
    public string Hash(string token) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed class JwtTokenService(IOptions<JwtOptions> jwt, IOptions<AuthOptions> auth, TimeProvider clock) : ITokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken CreateAccessToken(AccessTokenRequest request)
    {
        var now = clock.GetUtcNow();
        var expires = now.Add(auth.Value.AccessTokenLifetime);

        var claims = new List<Claim>
        {
            new(ClaimNames.Subject, request.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimNames.SessionId, request.SessionId.ToString()),
            new(ClaimNames.Email, request.Email),
            new(ClaimNames.Name, request.FullName),
            new(ClaimNames.Scope, request.TenantId is null ? ClaimNames.PlatformScope : ClaimNames.TenantScope),
        };

        if (request.TenantId is { } tenantId)
        {
            claims.Add(new Claim(ClaimNames.TenantId, tenantId.ToString()));
        }

        claims.AddRange(request.Roles.Select(role => new Claim(ClaimNames.Role, role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = jwt.Value.Issuer,
            Audience = jwt.Value.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(jwt.Value.GetSigningKeyBytes()),
                SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }
}
