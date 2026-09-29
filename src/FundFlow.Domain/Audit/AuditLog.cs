using FundFlow.Domain.SharedKernel;

namespace FundFlow.Domain.Audit;

/// <summary>Stable action identifiers written to the audit log ("Module.Action").</summary>
public static class AuditActions
{
    public const string Login = "Auth.Login";
    public const string LoginFailed = "Auth.LoginFailed";
    public const string AccountLocked = "Auth.AccountLocked";
    public const string Logout = "Auth.Logout";
    public const string TokenReuseDetected = "Auth.TokenReuseDetected";
    public const string SessionRevoked = "Auth.SessionRevoked";
    public const string PasswordChanged = "Auth.PasswordChanged";
    public const string PasswordResetRequested = "Auth.PasswordResetRequested";
    public const string PasswordReset = "Auth.PasswordReset";
    public const string EmailVerified = "Auth.EmailVerified";

    public const string UserCreated = "User.Created";
    public const string UserInvited = "User.Invited";
    public const string UserInvitationAccepted = "User.InvitationAccepted";
    public const string UserUpdated = "User.Updated";
    public const string UserDeactivated = "User.Deactivated";
    public const string UserReactivated = "User.Reactivated";
    public const string UserUnlocked = "User.Unlocked";
    public const string UserRolesChanged = "User.RolesChanged";

    public const string RoleCreated = "Role.Created";
    public const string RoleUpdated = "Role.Updated";
    public const string RoleDeleted = "Role.Deleted";

    public const string OrganizationRegistered = "Organization.Registered";
    public const string OrganizationUpdated = "Organization.Updated";
    public const string OrganizationSettingsUpdated = "Organization.SettingsUpdated";
    public const string OrganizationSuspended = "Organization.Suspended";
    public const string OrganizationActivated = "Organization.Activated";
}

/// <summary>
/// Append-only record of a sensitive operation. Rows are never updated or deleted by the application; the
/// persistence layer rejects modifications. OldValues/NewValues hold JSON snapshots that must never contain secrets.
/// </summary>
public sealed class AuditLog : Entity, IOptionalTenantEntity
{
    private AuditLog()
    {
    }

    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }

    /// <summary>Denormalised so the log stays readable after the user is renamed or removed.</summary>
    public string? UserEmail { get; private set; }

    public string Action { get; private set; } = default!;
    public string EntityType { get; private set; } = default!;
    public string? EntityId { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? OldValues { get; private set; }
    public string? NewValues { get; private set; }
    public string? CorrelationId { get; private set; }

    public static AuditLog Create(
        DateTimeOffset timestamp,
        Guid? tenantId,
        Guid? userId,
        string? userEmail,
        string action,
        string entityType,
        string? entityId,
        string? ipAddress,
        string? userAgent,
        string? oldValues,
        string? newValues,
        string? correlationId) =>
        new()
        {
            Timestamp = timestamp,
            TenantId = tenantId,
            UserId = userId,
            UserEmail = Truncate(userEmail, 320),
            Action = Truncate(action, 100)!,
            EntityType = Truncate(entityType, 100)!,
            EntityId = Truncate(entityId, 100),
            IpAddress = Truncate(ipAddress, 64),
            UserAgent = Truncate(userAgent, 512),
            OldValues = oldValues,
            NewValues = newValues,
            CorrelationId = Truncate(correlationId, 100),
        };

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..max];
}
