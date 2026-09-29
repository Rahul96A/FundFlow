using System.Security.Claims;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Options;
using FundFlow.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FundFlow.Application.Tests.Infrastructure;

public class SecurityServicesTests
{
    private static PasswordService Passwords(int iterations = 1_000) =>
        new(Options.Create(new SecurityOptions { PasswordHashIterations = iterations }));

    [Fact]
    public void Passwords_are_salted_so_equal_passwords_hash_differently()
    {
        var service = Passwords();

        var first = service.Hash("Correct-Horse-Battery9");
        var second = service.Hash("Correct-Horse-Battery9");

        first.Should().NotBe(second);
        first.Should().NotContain("Correct-Horse");
    }

    [Fact]
    public void Verification_accepts_the_right_password_and_rejects_others()
    {
        var service = Passwords();
        var hash = service.Hash("Correct-Horse-Battery9");

        service.Verify(hash, "Correct-Horse-Battery9").Should().Be(PasswordVerification.Success);
        service.Verify(hash, "correct-horse-battery9").Should().Be(PasswordVerification.Failed);
        service.Verify(hash, "").Should().Be(PasswordVerification.Failed);
    }

    [Fact]
    public void Hashes_made_with_a_weaker_work_factor_are_flagged_for_a_transparent_upgrade()
    {
        var weak = Passwords(iterations: 1_000).Hash("Correct-Horse-Battery9");

        Passwords(iterations: 20_000).Verify(weak, "Correct-Horse-Battery9")
            .Should().Be(PasswordVerification.SuccessRehashNeeded);
        Passwords(iterations: 20_000).Verify(weak, "wrong").Should().Be(PasswordVerification.Failed);
    }

    [Fact]
    public void Simulated_verification_never_throws_and_needs_no_account()
    {
        var act = () => Passwords().SimulateVerification("anything at all");

        act.Should().NotThrow();
    }

    [Fact]
    public void Secret_tokens_are_long_unique_and_url_safe()
    {
        var tokens = new SecretTokens();

        var generated = Enumerable.Range(0, 1000).Select(_ => tokens.Generate()).ToList();

        generated.Should().OnlyHaveUniqueItems();
        generated.Should().OnlyContain(t => t.Length == 43 && t.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
    }

    [Fact]
    public void Token_hashes_are_deterministic_and_do_not_reveal_the_token()
    {
        var tokens = new SecretTokens();
        var token = tokens.Generate();

        tokens.Hash(token).Should().Be(tokens.Hash(token));
        tokens.Hash(token).Should().NotContain(token);
        tokens.Hash(token).Should().NotBe(tokens.Hash(tokens.Generate()));
    }

    private static (JwtTokenService Service, JwtOptions Jwt, FixedTimeProvider Clock) NewJwt()
    {
        var jwt = new JwtOptions { Issuer = "https://api.test", Audience = "web", SigningKey = new string('k', 48) };
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new JwtTokenService(Options.Create(jwt), Options.Create(new AuthOptions { AccessTokenLifetimeMinutes = 15 }), clock);
        return (service, jwt, clock);
    }

    private static TokenValidationParameters Validation(JwtOptions jwt, DateTimeOffset now) => new()
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(jwt.GetSigningKeyBytes()),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = TimeSpan.Zero,
        LifetimeValidator = (notBefore, expires, _, _) => notBefore <= now.UtcDateTime && now.UtcDateTime < expires,
        NameClaimType = "name",
        RoleClaimType = "role",
    };

    [Fact]
    public async Task Access_tokens_carry_identity_tenant_session_and_roles_but_no_permissions()
    {
        var (service, jwt, clock) = NewJwt();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var token = service.CreateAccessToken(new AccessTokenRequest(userId, tenantId, sessionId, "ada@example.org", "Ada Lovelace", ["STAFF", "FINANCE_MANAGER"]));
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, Validation(jwt, clock.GetUtcNow()));

        result.IsValid.Should().BeTrue();
        var claims = result.ClaimsIdentity;
        claims.FindFirst(ClaimNames.Subject)!.Value.Should().Be(userId.ToString());
        claims.FindFirst(ClaimNames.TenantId)!.Value.Should().Be(tenantId.ToString());
        claims.FindFirst(ClaimNames.SessionId)!.Value.Should().Be(sessionId.ToString());
        claims.FindFirst(ClaimNames.Scope)!.Value.Should().Be(ClaimNames.TenantScope);
        claims.FindAll(ClaimNames.Role).Select(c => c.Value).Should().BeEquivalentTo("STAFF", "FINANCE_MANAGER");
        claims.Claims.Should().NotContain(c => c.Type == "permission" || c.Type == "permissions");
        token.ExpiresAt.Should().Be(clock.GetUtcNow().AddMinutes(15));
    }

    [Fact]
    public void Platform_users_get_platform_scope_and_no_tenant_claim()
    {
        var (service, jwt, clock) = NewJwt();

        var token = service.CreateAccessToken(new AccessTokenRequest(Guid.NewGuid(), null, Guid.NewGuid(), "root@fundflow.test", "Root", ["SUPER_ADMIN"]));
        var parsed = new JsonWebTokenHandler().ReadJsonWebToken(token.Value);

        parsed.Claims.Should().NotContain(c => c.Type == ClaimNames.TenantId);
        parsed.Claims.Single(c => c.Type == ClaimNames.Scope).Value.Should().Be(ClaimNames.PlatformScope);
        _ = jwt;
        _ = clock;
    }

    [Fact]
    public async Task Tokens_expire_after_the_configured_lifetime()
    {
        var (service, jwt, clock) = NewJwt();
        var token = service.CreateAccessToken(new AccessTokenRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "a@b.org", "A B", []));

        var justBefore = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, Validation(jwt, clock.GetUtcNow().AddMinutes(14)));
        var justAfter = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, Validation(jwt, clock.GetUtcNow().AddMinutes(15).AddSeconds(1)));

        justBefore.IsValid.Should().BeTrue();
        justAfter.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Tokens_signed_with_a_different_key_or_for_a_different_audience_are_rejected()
    {
        var (service, jwt, clock) = NewJwt();
        var token = service.CreateAccessToken(new AccessTokenRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "a@b.org", "A B", []));
        var handler = new JsonWebTokenHandler();

        var wrongKey = Validation(jwt, clock.GetUtcNow());
        wrongKey.IssuerSigningKey = new SymmetricSecurityKey(new string('z', 48).Select(c => (byte)c).ToArray());
        var wrongAudience = Validation(jwt, clock.GetUtcNow());
        wrongAudience.ValidAudience = "someone-else";

        (await handler.ValidateTokenAsync(token.Value, wrongKey)).IsValid.Should().BeFalse();
        (await handler.ValidateTokenAsync(token.Value, wrongAudience)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task A_token_with_a_tampered_payload_fails_validation()
    {
        var (service, jwt, clock) = NewJwt();
        var token = service.CreateAccessToken(new AccessTokenRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "a@b.org", "A B", ["STAFF"])).Value;
        var parts = token.Split('.');
        var payload = System.Text.Encoding.UTF8.GetString(Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(parts[1]))
            .Replace("STAFF", "ADMIN", StringComparison.Ordinal); // same length: only the signature can catch it
        var forged = $"{parts[0]}.{Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(payload)}.{parts[2]}";

        (await new JsonWebTokenHandler().ValidateTokenAsync(forged, Validation(jwt, clock.GetUtcNow()))).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task An_unsigned_token_is_never_accepted()
    {
        var (_, jwt, clock) = NewJwt();
        var unsigned = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", "SUPER_ADMIN")]),
            Expires = clock.GetUtcNow().AddMinutes(5).UtcDateTime,
        });

        (await new JsonWebTokenHandler().ValidateTokenAsync(unsigned, Validation(jwt, clock.GetUtcNow()))).IsValid.Should().BeFalse();
    }
}
