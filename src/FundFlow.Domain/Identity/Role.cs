using FundFlow.Domain.SharedKernel;

namespace FundFlow.Domain.Identity;

/// <summary>A permission in the global catalogue. Reference data; identified by its stable name.</summary>
public sealed class Permission
{
    private Permission()
    {
    }

    public Permission(string name, string module, string description)
    {
        Name = name;
        Module = module;
        Description = description;
    }

    public string Name { get; private set; } = default!;
    public string Module { get; private set; } = default!;
    public string Description { get; private set; } = default!;

    public void Update(string module, string description)
    {
        Module = module;
        Description = description;
    }
}

public sealed class RolePermission : IOptionalTenantEntity
{
    private RolePermission()
    {
    }

    internal RolePermission(Guid roleId, string permissionName, Guid? tenantId)
    {
        RoleId = roleId;
        PermissionName = permissionName;
        TenantId = tenantId;
    }

    public Guid RoleId { get; private set; }
    public string PermissionName { get; private set; } = default!;
    public Guid? TenantId { get; private set; }
}

public sealed class UserRole : IOptionalTenantEntity
{
    private UserRole()
    {
    }

    internal UserRole(Guid userId, Guid roleId, Guid? tenantId, Guid? assignedBy, DateTimeOffset assignedAt)
    {
        UserId = userId;
        RoleId = roleId;
        TenantId = tenantId;
        AssignedBy = assignedBy;
        AssignedAt = assignedAt;
    }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid? TenantId { get; private set; }
    public Guid? AssignedBy { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }

    public Role Role { get; private set; } = default!;
}

public sealed record RoleCreatedDomainEvent(Guid RoleId, Guid? TenantId, string Name) : DomainEvent;

/// <summary>
/// A named bundle of permissions. System roles are created from <see cref="RoleTemplates"/> and are managed by
/// code; custom roles belong to the organization and are edited through the API.
/// </summary>
public sealed class Role : AuditableEntity, IOptionalTenantEntity
{
    private readonly List<RolePermission> _permissions = [];

    private Role()
    {
    }

    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public string NormalizedName { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    public static string NormalizeName(string name) => name.Trim().ToUpperInvariant();

    public static Role CreateSystem(Guid? tenantId, RoleTemplate template)
    {
        var role = new Role
        {
            TenantId = tenantId,
            Name = template.Name,
            NormalizedName = NormalizeName(template.Name),
            Description = template.Description,
            IsSystem = true,
        };
        role.ReplacePermissions(template.Permissions);
        return role;
    }

    public static Role CreateCustom(Guid tenantId, string name, string? description, IEnumerable<string> permissions)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("role.name_required", "A role name is required.");
        }

        var role = new Role
        {
            TenantId = tenantId,
            Name = name.Trim(),
            NormalizedName = NormalizeName(name),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            IsSystem = false,
        };
        role.ReplacePermissions(permissions);
        role.Raise(new RoleCreatedDomainEvent(role.Id, tenantId, role.Name));
        return role;
    }

    public void Update(string name, string? description, IEnumerable<string> permissions)
    {
        EnsureEditable();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("role.name_required", "A role name is required.");
        }

        Name = name.Trim();
        NormalizedName = NormalizeName(name);
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ReplacePermissions(permissions);
    }

    public void EnsureEditable()
    {
        if (IsSystem)
        {
            throw new DomainException(
                "role.system_role_immutable",
                "System roles cannot be changed. Create a custom role instead.");
        }
    }

    /// <summary>Makes the permission set exactly <paramref name="permissionNames"/> and returns what changed.</summary>
    public (IReadOnlyList<string> Added, IReadOnlyList<string> Removed) ReplacePermissions(IEnumerable<string> permissionNames)
    {
        var desired = permissionNames.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);

        var unknown = desired.Where(p => !IsKnownPermission(p)).ToArray();
        if (unknown.Length > 0)
        {
            throw new DomainException("role.unknown_permission", $"Unknown permission(s): {string.Join(", ", unknown)}.");
        }

        if (TenantId is not null && desired.Contains(Identity.Permissions.Platform.Manage))
        {
            throw new DomainException(
                "role.platform_permission_forbidden",
                "Platform permissions cannot be granted to organization roles.");
        }

        var removed = _permissions.Where(p => !desired.Contains(p.PermissionName)).ToList();
        foreach (var permission in removed)
        {
            _permissions.Remove(permission);
        }

        var existing = _permissions.Select(p => p.PermissionName).ToHashSet(StringComparer.Ordinal);
        var added = desired.Where(p => !existing.Contains(p)).Order().ToList();
        foreach (var name in added)
        {
            _permissions.Add(new RolePermission(Id, name, TenantId));
        }

        return (added, removed.Select(p => p.PermissionName).Order().ToArray());
    }

    private static bool IsKnownPermission(string name) => Identity.Permissions.IsKnown(name);
}
