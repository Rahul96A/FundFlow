using System.Security.Claims;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace FundFlow.Api.Security;

/// <summary>
/// Requires the caller to hold a permission, e.g. <c>[HasPermission(Permissions.Donor.Read)]</c>.
/// Roles are only bundles of permissions; endpoints never check role names.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute(PermissionPolicy.Prefix + permission);

public static class PermissionPolicy
{
    public const string Prefix = "perm:";
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Builds a policy on demand for any <c>perm:*</c> policy name, so permissions need no manual registration.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[PermissionPolicy.Prefix.Length..]))
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}

public sealed class PermissionAuthorizationHandler(IUserAccessProvider accessProvider)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimNames.Subject), out var userId))
        {
            return;
        }

        var access = await accessProvider.GetAsync(userId, CancellationToken.None);
        if (access.Has(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
