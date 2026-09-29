namespace FundFlow.Contracts.Identity;

public sealed record RoleSummaryResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    int PermissionCount,
    int UserCount);

public sealed record RoleDetailResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions,
    int UserCount);

public sealed record CreateRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record UpdateRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record PermissionResponse(string Name, string Module, string Description);

public sealed record PermissionGroupResponse(string Module, IReadOnlyList<PermissionResponse> Permissions);
