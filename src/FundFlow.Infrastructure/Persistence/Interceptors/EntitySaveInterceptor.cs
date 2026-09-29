using FundFlow.Application.Common.Abstractions;
using FundFlow.Domain.Audit;
using FundFlow.Domain.Organizations;
using FundFlow.Domain.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FundFlow.Infrastructure.Persistence.Interceptors;

/// <summary>Thrown when a write would cross a tenant boundary. Always a bug (or an attack), never user error.</summary>
public sealed class TenantViolationException(string message) : InvalidOperationException(message);

/// <summary>
/// Runs on every SaveChanges:
/// <list type="bullet">
/// <item>stamps CreatedAt/CreatedBy/UpdatedAt/UpdatedBy (values come from the server, never from clients);</item>
/// <item>enforces tenant boundaries on <em>writes</em> as a second line of defence behind the read-side query filters;</item>
/// <item>keeps the audit log append-only.</item>
/// </list>
/// </summary>
public sealed class EntitySaveInterceptor(
    ITenantContext tenant,
    ICurrentUser currentUser,
    TimeProvider clock) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var actor = currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity is AuditLog && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Audit log entries are append-only and cannot be modified or deleted.");
            }

            EnforceTenantBoundary(entry);
            StampAuditFields(entry, now, actor);
        }
    }

    private static void StampAuditFields(EntityEntry entry, DateTimeOffset now, Guid? actor)
    {
        if (entry.Entity is not AuditableEntity)
        {
            return;
        }

        if (entry.State == EntityState.Added)
        {
            entry.Property(nameof(AuditableEntity.CreatedAt)).CurrentValue = now;
            entry.Property(nameof(AuditableEntity.CreatedBy)).CurrentValue = actor;
        }
        else if (entry.State == EntityState.Modified)
        {
            // Creation stamps are immutable even if a caller tried to overwrite them.
            entry.Property(nameof(AuditableEntity.CreatedAt)).IsModified = false;
            entry.Property(nameof(AuditableEntity.CreatedBy)).IsModified = false;
            entry.Property(nameof(AuditableEntity.UpdatedAt)).CurrentValue = now;
            entry.Property(nameof(AuditableEntity.UpdatedBy)).CurrentValue = actor;
        }
    }

    private void EnforceTenantBoundary(EntityEntry entry)
    {
        var scopeTenant = tenant.TenantId;

        switch (entry.Entity)
        {
            case ITenantEntity strict:
                if (scopeTenant is null)
                {
                    throw new TenantViolationException(
                        $"Cannot write {entry.Metadata.ClrType.Name} without a tenant scope.");
                }

                if (entry.State == EntityState.Added && strict.TenantId == Guid.Empty)
                {
                    entry.Property("TenantId").CurrentValue = scopeTenant.Value;
                }
                else if (strict.TenantId != scopeTenant)
                {
                    throw new TenantViolationException(
                        $"Cross-tenant write blocked: {entry.Metadata.ClrType.Name} belongs to a different tenant than the current scope.");
                }

                break;

            case IOptionalTenantEntity optional when scopeTenant is not null:
                // Inside a tenant scope a row must carry exactly that tenant. Platform scope and pre-authentication
                // flows (login, reset) are allowed to write tenant-tagged rows for the user they just identified.
                if (optional.TenantId != scopeTenant)
                {
                    throw new TenantViolationException(
                        $"Cross-tenant write blocked: {entry.Metadata.ClrType.Name} does not belong to the current tenant.");
                }

                break;

            case Organization organization when scopeTenant is not null:
                if (organization.Id != scopeTenant)
                {
                    throw new TenantViolationException("Cross-tenant write blocked: a tenant may only modify its own organization.");
                }

                break;
        }
    }
}
