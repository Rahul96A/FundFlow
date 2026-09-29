using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Domain.Audit;
using FundFlow.Infrastructure.Persistence;

namespace FundFlow.Infrastructure.Auditing;

public sealed class AuditLogger(AppDbContext db, ICurrentUser currentUser, ITenantContext tenant, TimeProvider clock) : IAuditLogger
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public void Record(AuditEntry entry)
    {
        db.AuditLogs.Add(AuditLog.Create(
            clock.GetUtcNow(),
            entry.TenantId ?? tenant.TenantId ?? currentUser.TenantId,
            entry.UserId ?? currentUser.UserId,
            entry.UserEmail ?? currentUser.Email,
            entry.Action,
            entry.EntityType,
            entry.EntityId,
            currentUser.IpAddress,
            currentUser.UserAgent,
            Serialize(entry.OldValues),
            Serialize(entry.NewValues),
            currentUser.CorrelationId));
    }

    /// <summary>
    /// Serialises a snapshot and masks anything that looks like a secret. Callers are told to pass explicit
    /// snapshots, but this is the backstop: credentials must never reach the audit table.
    /// </summary>
    internal static string? Serialize(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var node = JsonSerializer.SerializeToNode(value, Json);
        Redact(node);
        return node?.ToJsonString(Json);
    }

    private static void Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (IsSensitive(key))
                    {
                        obj[key] = "[REDACTED]";
                    }
                    else
                    {
                        Redact(obj[key]);
                    }
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    Redact(item);
                }

                break;
        }
    }

    private static bool IsSensitive(string propertyName)
    {
        string[] markers = ["password", "secret", "token", "hash", "authorization", "apikey", "cardnumber", "cvv", "cvc", "securitystamp"];
        return markers.Any(m => propertyName.Contains(m, StringComparison.OrdinalIgnoreCase));
    }
}
