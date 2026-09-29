using FluentValidation;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Exceptions;
using FundFlow.Application.Identity.Services;
using FundFlow.Contracts.Identity;
using FundFlow.Domain.Audit;
using FundFlow.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FundFlow.Application.Identity.Roles;

public sealed record ListRolesQuery : IRequest<IReadOnlyList<RoleSummaryResponse>>;

public sealed class ListRolesHandler(IAppDbContext db) : IRequestHandler<ListRolesQuery, IReadOnlyList<RoleSummaryResponse>>
{
    public async Task<IReadOnlyList<RoleSummaryResponse>> Handle(ListRolesQuery request, CancellationToken cancellationToken) =>
        await db.Roles.AsNoTracking()
            .OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name)
            .Select(r => new RoleSummaryResponse(
                r.Id,
                r.Name,
                r.Description,
                r.IsSystem,
                r.Permissions.Count,
                db.UserRoles.Count(ur => ur.RoleId == r.Id)))
            .Take(200)
            .ToListAsync(cancellationToken);
}

public sealed record GetRoleQuery(Guid RoleId) : IRequest<RoleDetailResponse>;

public sealed class GetRoleHandler(IAppDbContext db) : IRequestHandler<GetRoleQuery, RoleDetailResponse>
{
    public async Task<RoleDetailResponse> Handle(GetRoleQuery request, CancellationToken cancellationToken)
    {
        var role = await db.Roles.AsNoTracking()
            .Where(r => r.Id == request.RoleId)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Description,
                r.IsSystem,
                Permissions = r.Permissions.Select(p => p.PermissionName).ToList(),
                UserCount = db.UserRoles.Count(ur => ur.RoleId == r.Id),
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Role), request.RoleId);

        return new RoleDetailResponse(role.Id, role.Name, role.Description, role.IsSystem, role.Permissions.Order().ToList(), role.UserCount);
    }
}

public sealed record ListPermissionsQuery : IRequest<IReadOnlyList<PermissionGroupResponse>>;

public sealed class ListPermissionsHandler(ICurrentUser currentUser)
    : IRequestHandler<ListPermissionsQuery, IReadOnlyList<PermissionGroupResponse>>
{
    public Task<IReadOnlyList<PermissionGroupResponse>> Handle(ListPermissionsQuery request, CancellationToken cancellationToken)
    {
        // Tenant users never see (or can assign) platform-operator permissions.
        IEnumerable<PermissionDefinition> source = currentUser.IsPlatformUser ? Permissions.All : Permissions.TenantScoped;

        IReadOnlyList<PermissionGroupResponse> groups = source
            .GroupBy(p => p.Module)
            .Select(g => new PermissionGroupResponse(
                g.Key,
                g.Select(p => new PermissionResponse(p.Name, p.Module, p.Description)).ToList()))
            .ToList();
        return Task.FromResult(groups);
    }
}

public sealed record CreateRoleCommand(string Name, string? Description, IReadOnlyList<string> Permissions) : IRequest<RoleDetailResponse>;

public sealed class CreateRoleValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Permissions).NotNull().Must(p => p.Count <= 200).WithMessage("Too many permissions.");
    }
}

public sealed class CreateRoleHandler(
    IAppDbContext db,
    ITenantContext tenant,
    IRoleAssignmentGuard guard,
    IAuditLogger audit,
    ISender sender) : IRequestHandler<CreateRoleCommand, RoleDetailResponse>
{
    public async Task<RoleDetailResponse> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("Roles can only be created inside an organization.");
        var normalized = Role.NormalizeName(command.Name);

        if (await db.Roles.AnyAsync(r => r.NormalizedName == normalized, cancellationToken))
        {
            throw ConflictException.ForField("name", "A role with this name already exists.", "role_name_taken");
        }

        await guard.EnsureCanGrantAsync(command.Permissions, cancellationToken);

        var role = Role.CreateCustom(tenantId, command.Name, command.Description, command.Permissions);
        db.Roles.Add(role);

        audit.Record(new AuditEntry(
            AuditActions.RoleCreated, nameof(Role), role.Id.ToString(),
            NewValues: new { role.Name, role.Description, Permissions = command.Permissions.Order() }));
        await db.SaveChangesAsync(cancellationToken);

        return await sender.Send(new GetRoleQuery(role.Id), cancellationToken);
    }
}

public sealed record UpdateRoleCommand(Guid RoleId, string Name, string? Description, IReadOnlyList<string> Permissions)
    : IRequest<RoleDetailResponse>;

public sealed class UpdateRoleValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Permissions).NotNull().Must(p => p.Count <= 200).WithMessage("Too many permissions.");
    }
}

public sealed class UpdateRoleHandler(
    IAppDbContext db,
    IRoleAssignmentGuard guard,
    IUserAccessProvider accessProvider,
    IAuditLogger audit,
    ISender sender) : IRequestHandler<UpdateRoleCommand, RoleDetailResponse>
{
    public async Task<RoleDetailResponse> Handle(UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == command.RoleId, cancellationToken)
                   ?? throw new NotFoundException(nameof(Role), command.RoleId);

        try
        {
            role.EnsureEditable();
        }
        catch (FundFlow.Domain.SharedKernel.DomainException ex)
        {
            throw new ConflictException(ex.Message, ex.Code);
        }

        var normalized = Role.NormalizeName(command.Name);
        if (await db.Roles.AnyAsync(r => r.Id != role.Id && r.NormalizedName == normalized, cancellationToken))
        {
            throw ConflictException.ForField("name", "A role with this name already exists.", "role_name_taken");
        }

        var existing = role.Permissions.Select(p => p.PermissionName).ToHashSet();
        await guard.EnsureCanGrantAsync(command.Permissions.Where(p => !existing.Contains(p)), cancellationToken);

        var before = new { role.Name, role.Description, Permissions = existing.Order().ToList() };
        role.Update(command.Name, command.Description, command.Permissions);

        audit.Record(new AuditEntry(
            AuditActions.RoleUpdated, nameof(Role), role.Id.ToString(),
            OldValues: before,
            NewValues: new { role.Name, role.Description, Permissions = command.Permissions.Distinct().Order().ToList() }));
        await db.SaveChangesAsync(cancellationToken);

        // Everyone holding this role just gained or lost permissions; drop their cached access.
        var affectedUsers = await db.UserRoles.Where(ur => ur.RoleId == role.Id).Select(ur => ur.UserId).ToListAsync(cancellationToken);
        await accessProvider.InvalidateAsync(affectedUsers, cancellationToken);

        return await sender.Send(new GetRoleQuery(role.Id), cancellationToken);
    }
}

public sealed record DeleteRoleCommand(Guid RoleId) : IRequest;

public sealed class DeleteRoleHandler(IAppDbContext db, IAuditLogger audit) : IRequestHandler<DeleteRoleCommand>
{
    public async Task Handle(DeleteRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == command.RoleId, cancellationToken)
                   ?? throw new NotFoundException(nameof(Role), command.RoleId);

        try
        {
            role.EnsureEditable();
        }
        catch (FundFlow.Domain.SharedKernel.DomainException ex)
        {
            throw new ConflictException(ex.Message, ex.Code);
        }

        if (await db.UserRoles.AnyAsync(ur => ur.RoleId == role.Id, cancellationToken))
        {
            throw new ConflictException("This role is assigned to users. Reassign them before deleting it.", "role_in_use");
        }

        db.Roles.Remove(role);
        audit.Record(new AuditEntry(
            AuditActions.RoleDeleted, nameof(Role), role.Id.ToString(),
            OldValues: new { role.Name, role.Description, Permissions = role.Permissions.Select(p => p.PermissionName).Order().ToList() }));
        await db.SaveChangesAsync(cancellationToken);
    }
}
