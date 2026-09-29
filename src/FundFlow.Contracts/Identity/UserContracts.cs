using System.Text.Json.Serialization;

namespace FundFlow.Contracts.Identity;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserStatus
{
    PendingVerification = 1,
    Invited = 2,
    Active = 3,
    Locked = 4,
    Deactivated = 5,
}

public sealed record UserSummaryResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    UserStatus Status,
    IReadOnlyList<string> Roles,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt);

public sealed record UserRoleResponse(Guid Id, string Name);

public sealed record UserDetailResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    string? PhoneNumber,
    UserStatus Status,
    bool EmailConfirmed,
    bool IsActive,
    DateTimeOffset? LockoutEnd,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<UserRoleResponse> Roles);

public sealed record InviteUserRequest(
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    IReadOnlyList<Guid> RoleIds);

public sealed record UpdateUserRequest(string FirstName, string LastName, string? PhoneNumber);

public sealed record SetUserRolesRequest(IReadOnlyList<Guid> RoleIds);
