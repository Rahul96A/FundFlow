using FundFlow.Api.Security;
using FundFlow.Application.Identity.Roles;
using FundFlow.Contracts.Identity;
using FundFlow.Domain.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FundFlow.Api.Controllers;

/// <summary>Roles (bundles of permissions) and the permission catalogue.</summary>
[Route("api/v{version:apiVersion}")]
public sealed class RolesController(ISender sender) : ApiControllerBase
{
    [HttpGet("roles")]
    [HasPermission(Permissions.Role.Read)]
    public async Task<ActionResult<IReadOnlyList<RoleSummaryResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListRolesQuery(), cancellationToken));

    [HttpGet("roles/{id:guid}")]
    [HasPermission(Permissions.Role.Read)]
    public async Task<ActionResult<RoleDetailResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await sender.Send(new GetRoleQuery(id), cancellationToken);

    /// <summary>Creates a custom role. System roles are managed by the platform and cannot be created or edited.</summary>
    [HttpPost("roles")]
    [HasPermission(Permissions.Role.Manage)]
    [ProducesResponseType<RoleDetailResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await sender.Send(new CreateRoleCommand(request.Name, request.Description, request.Permissions), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = role.Id }, role);
    }

    [HttpPut("roles/{id:guid}")]
    [HasPermission(Permissions.Role.Manage)]
    public async Task<ActionResult<RoleDetailResponse>> Update(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new UpdateRoleCommand(id, request.Name, request.Description, request.Permissions), cancellationToken);

    /// <summary>Deletes a custom role that is not assigned to anyone.</summary>
    [HttpDelete("roles/{id:guid}")]
    [HasPermission(Permissions.Role.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteRoleCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>The permission catalogue, grouped by module.</summary>
    [HttpGet("permissions")]
    [HasPermission(Permissions.Role.Read)]
    public async Task<ActionResult<IReadOnlyList<PermissionGroupResponse>>> ListPermissions(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListPermissionsQuery(), cancellationToken));
}
