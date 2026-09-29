using FundFlow.Api.Security;
using FundFlow.Application.Audit;
using FundFlow.Application.Organizations;
using FundFlow.Contracts.Audit;
using FundFlow.Contracts.Common;
using FundFlow.Contracts.Organizations;
using FundFlow.Domain.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FundFlow.Api.Controllers;

/// <summary>The caller's own organization: profile, settings and dashboard overview.</summary>
[Route("api/v{version:apiVersion}/organization")]
public sealed class OrganizationController(ISender sender) : ApiControllerBase
{
    /// <summary>The caller's organization. Any signed-in member may read it.</summary>
    [HttpGet]
    public async Task<ActionResult<OrganizationResponse>> Get(CancellationToken cancellationToken) =>
        await sender.Send(new GetOrganizationQuery(), cancellationToken);

    [HttpPut]
    [HasPermission(Permissions.Organization.Update)]
    public async Task<ActionResult<OrganizationResponse>> Update(UpdateOrganizationRequest request, CancellationToken cancellationToken) =>
        await sender.Send(
            new UpdateOrganizationCommand(
                request.Name, request.LegalName, request.TaxId, request.Website, request.ContactEmail, request.PhoneNumber, request.Address),
            cancellationToken);

    [HttpPut("settings")]
    [HasPermission(Permissions.Organization.Update)]
    public async Task<ActionResult<OrganizationResponse>> UpdateSettings(UpdateOrganizationSettingsRequest request, CancellationToken cancellationToken) =>
        await sender.Send(
            new UpdateOrganizationSettingsCommand(
                request.TimeZoneId, request.CurrencyCode, request.Locale, request.FiscalYearStartMonth, request.LogoUrl, request.BrandColor),
            cancellationToken);

    /// <summary>Dashboard data. Sections the caller may not see are omitted.</summary>
    [HttpGet("overview")]
    public async Task<ActionResult<OrganizationOverviewResponse>> Overview(CancellationToken cancellationToken) =>
        await sender.Send(new GetOrganizationOverviewQuery(), cancellationToken);
}

/// <summary>The append-only audit trail of the caller's organization.</summary>
[Route("api/v{version:apiVersion}/audit-logs")]
public sealed class AuditLogsController(ISender sender) : ApiControllerBase
{
    /// <summary>Searches audit entries. `action` may be exact (`Auth.Login`) or a module prefix ending in a dot (`Auth.`).</summary>
    /// <remarks>`sortBy`: timestamp, action, entityType, userEmail.</remarks>
    [HttpGet]
    [HasPermission(Permissions.Audit.Read)]
    public async Task<ActionResult<PagedResponse<AuditLogResponse>>> List([FromQuery] ListAuditLogsQuery query, CancellationToken cancellationToken) =>
        await sender.Send(query, cancellationToken);
}

/// <summary>Tenant management for platform operators (SUPER_ADMIN). Sees the organization registry, never tenant data.</summary>
[Route("api/v{version:apiVersion}/platform/organizations")]
public sealed class PlatformOrganizationsController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Platform.Manage)]
    public async Task<ActionResult<PagedResponse<PlatformOrganizationResponse>>> List(
        [FromQuery] ListPlatformOrganizationsQuery query,
        CancellationToken cancellationToken) =>
        await sender.Send(query, cancellationToken);

    /// <summary>Suspends an organization: its users are signed out and cannot sign in until it is reactivated.</summary>
    [HttpPost("{id:guid}/suspend")]
    [HasPermission(Permissions.Platform.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Suspend(Guid id, SuspendOrganizationRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new SuspendOrganizationCommand(id, request.Reason), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.Platform.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new ActivateOrganizationCommand(id), cancellationToken);
        return NoContent();
    }
}

/// <summary>Anonymous, public-safe organization information for giving and event pages.</summary>
[Route("api/v{version:apiVersion}/public/organizations")]
public sealed class PublicOrganizationsController(ISender sender) : ApiControllerBase
{
    /// <summary>Branding and currency of an organization, looked up by its public slug.</summary>
    [HttpGet("{tenantSlug}")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicOrganizationResponse>> Get(string tenantSlug, CancellationToken cancellationToken) =>
        await sender.Send(new GetPublicOrganizationQuery(tenantSlug), cancellationToken);
}
