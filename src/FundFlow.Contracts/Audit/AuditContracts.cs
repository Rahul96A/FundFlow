using System.Text.Json;

namespace FundFlow.Contracts.Audit;

public sealed record AuditLogResponse(
    Guid Id,
    DateTimeOffset Timestamp,
    Guid? UserId,
    string? UserEmail,
    string Action,
    string EntityType,
    string? EntityId,
    string? IpAddress,
    string? UserAgent,
    JsonElement? OldValues,
    JsonElement? NewValues,
    string? CorrelationId);
