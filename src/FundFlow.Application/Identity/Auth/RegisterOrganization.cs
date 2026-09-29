using System.Security.Cryptography;
using FluentValidation;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Exceptions;
using FundFlow.Application.Common.Validation;
using FundFlow.Contracts.Identity;
using FundFlow.Domain.Audit;
using FundFlow.Domain.Identity;
using FundFlow.Domain.Organizations;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FundFlow.Application.Identity.Auth;

public sealed record RegisterOrganizationCommand(
    string OrganizationName,
    string? OrganizationSlug,
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? TimeZoneId,
    string? CurrencyCode) : IRequest<RegisterOrganizationResponse>;

public sealed class RegisterOrganizationValidator : AbstractValidator<RegisterOrganizationCommand>
{
    public RegisterOrganizationValidator()
    {
        RuleFor(x => x.OrganizationName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OrganizationSlug)
            .Must(s => Organization.IsValidSlug(s))
            .When(x => !string.IsNullOrWhiteSpace(x.OrganizationSlug))
            .WithMessage("Use 3-50 lowercase letters, numbers or hyphens, and avoid reserved words.");
        RuleFor(x => x.FirstName).MustBePersonName("First name");
        RuleFor(x => x.LastName).MustBePersonName("Last name");
        RuleFor(x => x.Email).MustBeEmail();
        RuleFor(x => x.Password).MustBeStrongPassword();
        RuleFor(x => x.CurrencyCode)
            .Length(3).When(x => !string.IsNullOrWhiteSpace(x.CurrencyCode))
            .WithMessage("Use a three-letter currency code such as USD.");
    }
}

/// <summary>
/// Self-service sign-up: creates the tenant, its default settings and system roles, and the first administrator,
/// in one transaction. The administrator must verify their email before signing in.
/// </summary>
public sealed class RegisterOrganizationHandler(
    IAppDbContext db,
    ITenantScope tenantScope,
    IPasswordService passwords,
    IAuditLogger audit,
    TimeProvider clock) : IRequestHandler<RegisterOrganizationCommand, RegisterOrganizationResponse>
{
    private const string DefaultTimeZone = "UTC";
    private const string DefaultCurrency = "USD";

    public async Task<RegisterOrganizationResponse> Handle(RegisterOrganizationCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim();
        var normalizedEmail = User.NormalizeEmail(email);

        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            throw ConflictException.ForField("email", "An account with this email address already exists.", "email_taken");
        }

        var slug = await ResolveSlugAsync(command, cancellationToken);

        var organization = Organization.Register(
            command.OrganizationName,
            slug,
            email,
            string.IsNullOrWhiteSpace(command.TimeZoneId) ? DefaultTimeZone : command.TimeZoneId,
            string.IsNullOrWhiteSpace(command.CurrencyCode) ? DefaultCurrency : command.CurrencyCode);

        // From here on every tenant-owned row must belong to the new organization; the DbContext enforces it.
        tenantScope.UseTenant(organization.Id);

        var roles = RoleTemplates.Tenant.Select(t => Role.CreateSystem(organization.Id, t)).ToList();
        var administratorRole = roles.Single(r => r.Name == SystemRoles.OrganizationAdmin);

        var owner = User.Register(
            organization.Id,
            email,
            command.FirstName,
            command.LastName,
            passwords.Hash(command.Password));
        owner.SetRoles([administratorRole], null, clock.GetUtcNow());

        db.Organizations.Add(organization);
        db.Roles.AddRange(roles);
        db.Users.Add(owner);

        audit.Record(new AuditEntry(
            AuditActions.OrganizationRegistered,
            nameof(Organization),
            organization.Id.ToString(),
            NewValues: new { organization.Name, organization.Slug, OwnerEmail = owner.Email },
            TenantId: organization.Id,
            UserId: owner.Id,
            UserEmail: owner.Email));

        await db.SaveChangesAsync(cancellationToken);

        return new RegisterOrganizationResponse(organization.Id, organization.Slug, owner.Email, RequiresEmailVerification: true);
    }

    private async Task<string> ResolveSlugAsync(RegisterOrganizationCommand command, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.OrganizationSlug))
        {
            var requested = command.OrganizationSlug.Trim().ToLowerInvariant();
            if (await SlugExistsAsync(requested, cancellationToken))
            {
                throw ConflictException.ForField("organizationSlug", "This organization address is already taken.", "slug_taken");
            }

            return requested;
        }

        var baseSlug = Organization.Slugify(command.OrganizationName);
        if (baseSlug.Length == 0)
        {
            baseSlug = "organization";
        }

        if (!Organization.IsValidSlug(baseSlug))
        {
            // Too short or reserved ("Al", "Support"): make it distinguishable rather than rejecting the sign-up.
            baseSlug = Organization.Slugify($"{baseSlug}-org");
        }

        var candidate = baseSlug;
        for (var attempt = 2; attempt <= 25; attempt++)
        {
            if (!await SlugExistsAsync(candidate, cancellationToken))
            {
                return candidate;
            }

            candidate = Truncate($"{baseSlug}-{attempt}");
        }

        return Truncate($"{baseSlug}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant()}");

        static string Truncate(string value) =>
            value.Length <= Organization.SlugMaxLength ? value : value[..Organization.SlugMaxLength].TrimEnd('-');
    }

    private Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        db.Organizations.IgnoreQueryFilters().AnyAsync(o => o.Slug == slug, cancellationToken);
}
