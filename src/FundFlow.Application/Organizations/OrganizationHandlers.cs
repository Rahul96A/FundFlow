using FluentValidation;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Exceptions;
using FundFlow.Application.Common.Validation;
using FundFlow.Contracts.Organizations;
using FundFlow.Domain.Audit;
using FundFlow.Domain.Organizations;
using FundFlow.Domain.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ContractStatus = FundFlow.Contracts.Organizations.OrganizationStatus;

namespace FundFlow.Application.Organizations;

public sealed record GetOrganizationQuery : IRequest<OrganizationResponse>;

public sealed class GetOrganizationHandler(IAppDbContext db, ITenantContext tenant)
    : IRequestHandler<GetOrganizationQuery, OrganizationResponse>
{
    public async Task<OrganizationResponse> Handle(GetOrganizationQuery request, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("This operation requires an organization.");

        var row = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == tenantId)
            .Select(o => new
            {
                o.Id, o.Name, o.Slug, o.LegalName, o.TaxId, o.Website, o.ContactEmail, o.PhoneNumber, o.Address, o.Status, o.CreatedAt,
                o.Settings.TimeZoneId, o.Settings.CurrencyCode, o.Settings.Locale, o.Settings.FiscalYearStartMonth,
                o.Settings.LogoUrl, o.Settings.BrandColor,
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Organization), tenantId);

        return new OrganizationResponse(
            row.Id, row.Name, row.Slug, row.LegalName, row.TaxId, row.Website, row.ContactEmail, row.PhoneNumber,
            new AddressDto(row.Address.Line1, row.Address.Line2, row.Address.City, row.Address.Region, row.Address.PostalCode, row.Address.Country),
            (ContractStatus)(int)row.Status,
            new OrganizationSettingsResponse(row.TimeZoneId, row.CurrencyCode, row.Locale, row.FiscalYearStartMonth, row.LogoUrl, row.BrandColor),
            row.CreatedAt);
    }
}

public sealed record UpdateOrganizationCommand(
    string Name,
    string? LegalName,
    string? TaxId,
    string? Website,
    string ContactEmail,
    string? PhoneNumber,
    AddressDto? Address) : IRequest<OrganizationResponse>;

public sealed class UpdateOrganizationValidator : AbstractValidator<UpdateOrganizationCommand>
{
    public UpdateOrganizationValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.LegalName).MaximumLength(200);
        RuleFor(x => x.TaxId).MaximumLength(50);
        RuleFor(x => x.Website)
            .MaximumLength(300)
            .Must(BeHttpUrl).When(x => !string.IsNullOrWhiteSpace(x.Website))
            .WithMessage("Enter a full web address starting with http:// or https://.");
        RuleFor(x => x.ContactEmail).MustBeEmail();
        RuleFor(x => x.PhoneNumber).MustBeOptionalPhone();
        RuleFor(x => x.Address!.Line1).MaximumLength(200).When(x => x.Address is not null);
        RuleFor(x => x.Address!.Line2).MaximumLength(200).When(x => x.Address is not null);
        RuleFor(x => x.Address!.City).MaximumLength(100).When(x => x.Address is not null);
        RuleFor(x => x.Address!.Region).MaximumLength(100).When(x => x.Address is not null);
        RuleFor(x => x.Address!.PostalCode).MaximumLength(20).When(x => x.Address is not null);
        RuleFor(x => x.Address!.Country).MaximumLength(100).When(x => x.Address is not null);
    }

    internal static bool BeHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}

public sealed class UpdateOrganizationHandler(
    IAppDbContext db,
    ITenantContext tenant,
    IAuditLogger audit,
    ISender sender) : IRequestHandler<UpdateOrganizationCommand, OrganizationResponse>
{
    public async Task<OrganizationResponse> Handle(UpdateOrganizationCommand command, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("This operation requires an organization.");
        var organization = await db.Organizations.FirstOrDefaultAsync(o => o.Id == tenantId, cancellationToken)
                           ?? throw new NotFoundException(nameof(Organization), tenantId);

        var before = new { organization.Name, organization.LegalName, organization.TaxId, organization.Website, organization.ContactEmail, organization.PhoneNumber, organization.Address };
        var address = command.Address is null
            ? Address.Empty
            : new Address(command.Address.Line1, command.Address.Line2, command.Address.City, command.Address.Region, command.Address.PostalCode, command.Address.Country);

        organization.UpdateProfile(command.Name, command.LegalName, command.TaxId, command.Website, command.ContactEmail, command.PhoneNumber, address);

        audit.Record(new AuditEntry(
            AuditActions.OrganizationUpdated, nameof(Organization), organization.Id.ToString(),
            OldValues: before,
            NewValues: new { organization.Name, organization.LegalName, organization.TaxId, organization.Website, organization.ContactEmail, organization.PhoneNumber, organization.Address }));
        await db.SaveChangesAsync(cancellationToken);

        return await sender.Send(new GetOrganizationQuery(), cancellationToken);
    }
}

public sealed record UpdateOrganizationSettingsCommand(
    string TimeZoneId,
    string CurrencyCode,
    string Locale,
    int FiscalYearStartMonth,
    string? LogoUrl,
    string? BrandColor) : IRequest<OrganizationResponse>;

public sealed class UpdateOrganizationSettingsValidator : AbstractValidator<UpdateOrganizationSettingsCommand>
{
    public UpdateOrganizationSettingsValidator()
    {
        RuleFor(x => x.TimeZoneId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3).WithMessage("Use a three-letter currency code such as USD.");
        RuleFor(x => x.Locale).NotEmpty().MaximumLength(20);
        RuleFor(x => x.FiscalYearStartMonth).InclusiveBetween(1, 12);
        RuleFor(x => x.LogoUrl)
            .MaximumLength(500)
            .Must(UpdateOrganizationValidator.BeHttpUrl).When(x => !string.IsNullOrWhiteSpace(x.LogoUrl))
            .WithMessage("Enter a full web address starting with http:// or https://.");
        RuleFor(x => x.BrandColor)
            .Matches("^#[0-9A-Fa-f]{6}$").When(x => !string.IsNullOrWhiteSpace(x.BrandColor))
            .WithMessage("Use a hex colour like #1F5EFF.");
    }
}

public sealed class UpdateOrganizationSettingsHandler(
    IAppDbContext db,
    ITenantContext tenant,
    IAuditLogger audit,
    ISender sender) : IRequestHandler<UpdateOrganizationSettingsCommand, OrganizationResponse>
{
    public async Task<OrganizationResponse> Handle(UpdateOrganizationSettingsCommand command, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("This operation requires an organization.");
        var settings = await db.OrganizationSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken)
                       ?? throw new NotFoundException(nameof(OrganizationSettings), tenantId);

        var before = new { settings.TimeZoneId, settings.CurrencyCode, settings.Locale, settings.FiscalYearStartMonth, settings.LogoUrl, settings.BrandColor };
        try
        {
            settings.Update(command.TimeZoneId, command.CurrencyCode, command.Locale, command.FiscalYearStartMonth, command.LogoUrl, command.BrandColor);
        }
        catch (DomainException ex)
        {
            throw Failures.Field(FieldFor(ex.Code), ex.Message);
        }

        audit.Record(new AuditEntry(
            AuditActions.OrganizationSettingsUpdated, nameof(OrganizationSettings), settings.Id.ToString(),
            OldValues: before,
            NewValues: new { settings.TimeZoneId, settings.CurrencyCode, settings.Locale, settings.FiscalYearStartMonth, settings.LogoUrl, settings.BrandColor }));
        await db.SaveChangesAsync(cancellationToken);

        return await sender.Send(new GetOrganizationQuery(), cancellationToken);
    }

    private static string FieldFor(string code) => code switch
    {
        "settings.timezone_invalid" => "timeZoneId",
        "settings.currency_invalid" => "currencyCode",
        "settings.fiscal_month_invalid" => "fiscalYearStartMonth",
        "settings.color_invalid" => "brandColor",
        _ => "settings",
    };
}

public sealed record GetPublicOrganizationQuery(string TenantSlug) : IRequest<PublicOrganizationResponse>;

/// <summary>Anonymous-safe: tenant scope comes from the URL slug, and only public fields are projected.</summary>
public sealed class GetPublicOrganizationHandler(IAppDbContext db, ITenantContext tenant)
    : IRequestHandler<GetPublicOrganizationQuery, PublicOrganizationResponse>
{
    public async Task<PublicOrganizationResponse> Handle(GetPublicOrganizationQuery request, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId ?? throw new NotFoundException(nameof(Organization), request.TenantSlug);

        return await db.Organizations.AsNoTracking()
                   .Where(o => o.Id == tenantId && o.Status == FundFlow.Domain.Organizations.OrganizationStatus.Active)
                   .Select(o => new PublicOrganizationResponse(
                       o.Name, o.Slug, o.Settings.LogoUrl, o.Settings.BrandColor, o.Settings.CurrencyCode, o.Settings.TimeZoneId))
                   .FirstOrDefaultAsync(cancellationToken)
               ?? throw new NotFoundException(nameof(Organization), request.TenantSlug);
    }
}
