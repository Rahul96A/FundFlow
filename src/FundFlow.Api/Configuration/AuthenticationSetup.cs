using System.Security.Claims;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FundFlow.Api.Configuration;

public static class AuthenticationSetup
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                // Claims stay exactly as issued ("sub", "role", ...): no silent remapping to legacy URI claim types.
                bearer.MapInboundClaims = false;
                bearer.RequireHttpsMetadata = false; // tokens are self-contained; there is no metadata endpoint to protect
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(jwt.GetSigningKeyBytes()),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256], // never accept "none" or an attacker-chosen algorithm
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimNames.Name,
                    RoleClaimType = ClaimNames.Role,
                };

                bearer.Events = new JwtBearerEvents
                {
                    // A valid signature is not enough: logout, password change, deactivation and refresh-token theft
                    // all revoke a session, and its already-issued access token must stop working immediately.
                    OnTokenValidated = async context =>
                    {
                        var sessionClaim = context.Principal?.FindFirstValue(ClaimNames.SessionId);
                        if (!Guid.TryParse(sessionClaim, out var sessionId))
                        {
                            context.Fail("The token has no session.");
                            return;
                        }

                        var revocations = context.HttpContext.RequestServices.GetRequiredService<ISessionRevocations>();
                        if (await revocations.IsRevokedAsync(sessionId, context.HttpContext.RequestAborted))
                        {
                            context.Fail("The session has been revoked.");
                        }
                    },
                };
            });

        services.AddAuthorization(options =>
        {
            // Secure by default: an endpoint is private unless it explicitly opts out with [AllowAnonymous].
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });
        services.AddSingleton<IAuthorizationPolicyProvider, Security.PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, Security.PermissionAuthorizationHandler>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, Security.HttpCurrentUser>();

        return services;
    }
}
