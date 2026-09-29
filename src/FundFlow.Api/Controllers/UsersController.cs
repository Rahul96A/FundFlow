using FundFlow.Api.Security;
using FundFlow.Application.Identity.Users;
using FundFlow.Contracts.Common;
using FundFlow.Contracts.Identity;
using FundFlow.Domain.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FundFlow.Api.Controllers;

/// <summary>Users of the caller's organization.</summary>
[Route("api/v{version:apiVersion}/users")]
public sealed class UsersController(ISender sender) : ApiControllerBase
{
    /// <summary>Lists users with search, status/role filters, sorting and paging.</summary>
    /// <remarks>`sortBy`: name, email, lastLoginAt, createdAt.</remarks>
    [HttpGet]
    [HasPermission(Permissions.User.Read)]
    public async Task<ActionResult<PagedResponse<UserSummaryResponse>>> List([FromQuery] ListUsersQuery query, CancellationToken cancellationToken) =>
        await sender.Send(query, cancellationToken);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.User.Read)]
    public async Task<ActionResult<UserDetailResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await sender.Send(new GetUserQuery(id), cancellationToken);

    /// <summary>Invites a user by email; they choose their own password from the invitation link.</summary>
    [HttpPost]
    [HasPermission(Permissions.User.Manage)]
    [ProducesResponseType<UserDetailResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Invite(InviteUserRequest request, CancellationToken cancellationToken)
    {
        var user = await sender.Send(
            new InviteUserCommand(request.Email, request.FirstName, request.LastName, request.PhoneNumber, request.RoleIds),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.User.Manage)]
    public async Task<ActionResult<UserDetailResponse>> Update(Guid id, UpdateUserRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new UpdateUserCommand(id, request.FirstName, request.LastName, request.PhoneNumber), cancellationToken);

    /// <summary>Replaces the user's roles. Callers cannot grant permissions they do not hold themselves (unless administrators).</summary>
    [HttpPut("{id:guid}/roles")]
    [HasPermission(Permissions.User.Manage)]
    public async Task<ActionResult<UserDetailResponse>> SetRoles(Guid id, SetUserRolesRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SetUserRolesCommand(id, request.RoleIds), cancellationToken);

    /// <summary>Deactivates the user and signs them out everywhere.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Permissions.User.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeactivateUserCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    [HasPermission(Permissions.User.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new ReactivateUserCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Clears a lockout caused by repeated failed sign-ins.</summary>
    [HttpPost("{id:guid}/unlock")]
    [HasPermission(Permissions.User.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new UnlockUserCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/resend-invitation")]
    [HasPermission(Permissions.User.Manage)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ResendInvitation(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new ResendInvitationCommand(id), cancellationToken);
        return Accepted();
    }
}
