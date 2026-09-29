namespace FundFlow.Application.Common.Abstractions;

/// <summary>
/// One audit record to write. Unspecified actor/tenant fields default to the current request's values.
/// <see cref="OldValues"/>/<see cref="NewValues"/> are serialised to JSON, so pass explicit snapshots
/// (anonymous objects or DTOs) that never contain secrets rather than entities.
/// </summary>
public sealed record AuditEntry(
    string Action,
    string EntityType,
    string? EntityId = null,
    object? OldValues = null,
    object? NewValues = null,
    Guid? TenantId = null,
    Guid? UserId = null,
    string? UserEmail = null);

public interface IAuditLogger
{
    /// <summary>
    /// Stages an audit record on the current unit of work. It is persisted by the same <c>SaveChanges</c> as the
    /// operation it describes, so the business change and its audit trail commit or roll back together.
    /// </summary>
    void Record(AuditEntry entry);
}
