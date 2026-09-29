using FluentValidation;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Exceptions;
using FundFlow.Application.Common.Paging;
using FundFlow.Contracts.Common;
using FundFlow.Contracts.Organizations;
using FundFlow.Domain.Audit;
using FundFlow.Domain.Organizations;
using FundFlow.Domain.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ContractStatus = FundFlow.Contracts.Organizations.OrganizationStatus;
using DomainStatus = FundFlow.Domain.Organizations.OrganizationStatus;

namespace FundFlow.Application.Organizations;

// Platform (SUPER_ADMIN) tenant management. These handlers work in platform scope: they can see the registry of
// organizations, and nothing inside any organization.

public sealed record ListPlatformOrganizationsQuery(ContractStatus? Status) : PagedQuery, IRequest<PagedResponse<PlatformOrganizationResponse>>;

public sealed class ListPlatformOrganizationsHandler(IAppDbContext db, ITenantContext tenant)
    : IRequestHandler<ListPlatformOrganizationsQuery, PagedResponse<PlatformOrganizationResponse>>
{
    private static readonly SortMap<Organization> Sorts = new SortMap<Organization>()
        .Add("name", o => o.Name)
        .Add("slug", o => o.Slug)
        .Add("createdAt", o => o.CreatedAt)
        .Default("createdAt", descending: true);

    public async Task<PagedResponse<PlatformOrganizationResponse>> Handle(
        ListPlatformOrganizationsQuery request,
        CancellationToken cancellationToken)
    {
        if (!tenant.IsPlatformScope)
        {
            throw new ForbiddenException("This operation is restricted to platform operators.");
        }

        var query = db.Organizations.AsNoTracking();

        if (request.SearchTerm is { } term)
        {
            var pattern = $"%{PagingExtensions.EscapeLike(term)}%";
            query = query.Where(o =>
                EF.Functions.Like(o.Name, pattern, "\\")
                || EF.Functions.Like(o.Slug, pattern, "\\")
                || EF.Functions.Like(o.ContactEmail, pattern, "\\"));
        }

        if (request.Status is { } status)
        {
            var domainStatus = (DomainStatus)(int)status;
            query = query.Where(o => o.Status == domainStatus);
        }

        return await Sorts.Apply(query, request.SortBy, request.SortDirection)
            .ToPagedResponseAsync(
                o => new PlatformOrganizationResponse(
                    o.Id,
                    o.Name,
                    o.Slug,
                    o.Status == DomainStatus.Active ? ContractStatus.Active : ContractStatus.Suspended,
                    o.ContactEmail,
                    db.Users.IgnoreQueryFilters().Count(u => u.TenantId == o.Id),
                    o.CreatedAt,
                    o.SuspendedAt,
                    o.SuspensionReason),
                request,
                cancellationToken);
    }
}

public sealed record SuspendOrganizationCommand(Guid OrganizationId, string Reason) : IRequest;

public sealed class SuspendOrganizationValidator : AbstractValidator<SuspendOrganizationCommand>
{
    public SuspendOrganizationValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class SuspendOrganizationHandler(
    IAppDbContext db,
    ITenantContext tenant,
    ITenantDirectory directory,
    IAuditLogger audit,
    TimeProvider clock) : IRequestHandler<SuspendOrganizationCommand>
{
    public async Task Handle(SuspendOrganizationCommand command, CancellationToken cancellationToken)
    {
        if (!tenant.IsPlatformScope)
        {
            throw new ForbiddenException("This operation is restricted to platform operators.");
        }

        var organization = await db.Organizations.FirstOrDefaultAsync(o => o.Id == command.OrganizationId, cancellationToken)
                           ?? throw new NotFoundException(nameof(Organization), command.OrganizationId);

        try
        {
            organization.Suspend(command.Reason, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            throw new ConflictException(ex.Message, ex.Code);
        }

        audit.Record(new AuditEntry(
            AuditActions.OrganizationSuspended, nameof(Organization), organization.Id.ToString(),
            NewValues: new { organization.Slug, Reason = command.Reason },
            TenantId: organization.Id));
        await db.SaveChangesAsync(cancellationToken);
        await directory.InvalidateAsync(organization.Id, organization.Slug, cancellationToken);
    }
}

public sealed record ActivateOrganizationCommand(Guid OrganizationId) : IRequest;

public sealed class ActivateOrganizationHandler(
    IAppDbContext db,
    ITenantContext tenant,
    ITenantDirectory directory,
    IAuditLogger audit) : IRequestHandler<ActivateOrganizationCommand>
{
    public async Task Handle(ActivateOrganizationCommand command, CancellationToken cancellationToken)
    {
        if (!tenant.IsPlatformScope)
        {
            throw new ForbiddenException("This operation is restricted to platform operators.");
        }

        var organization = await db.Organizations.FirstOrDefaultAsync(o => o.Id == command.OrganizationId, cancellationToken)
                           ?? throw new NotFoundException(nameof(Organization), command.OrganizationId);

        try
        {
            organization.Activate();
        }
        catch (DomainException ex)
        {
            throw new ConflictException(ex.Message, ex.Code);
        }

        audit.Record(new AuditEntry(
            AuditActions.OrganizationActivated, nameof(Organization), organization.Id.ToString(),
            NewValues: new { organization.Slug },
            TenantId: organization.Id));
        await db.SaveChangesAsync(cancellationToken);
        await directory.InvalidateAsync(organization.Id, organization.Slug, cancellationToken);
    }
}
