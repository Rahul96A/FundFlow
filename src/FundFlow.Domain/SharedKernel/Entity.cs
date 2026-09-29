namespace FundFlow.Domain.SharedKernel;

/// <summary>Marker for facts that happened inside the domain. Handled inside the unit of work (before commit).</summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Ids.New();
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>Base class for every persisted domain object. Identity is assigned by the domain, not the database.</summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; protected set; } = Ids.New();

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>
/// Adds audit columns and optimistic concurrency. The persistence layer stamps these values; domain code never
/// sets them, which keeps "who/when" trustworthy.
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    /// <summary>SQL Server rowversion; a stale write raises <c>DbUpdateConcurrencyException</c> (mapped to HTTP 409).</summary>
    public byte[] RowVersion { get; private set; } = [];
}

/// <summary>An entity owned by exactly one tenant. The persistence layer enforces isolation on read and write.</summary>
public interface ITenantEntity
{
    Guid TenantId { get; }
}

/// <summary>
/// An entity that is usually owned by a tenant but may also exist at platform level (TenantId == null),
/// e.g. the SUPER_ADMIN user and its role.
/// </summary>
public interface IOptionalTenantEntity
{
    Guid? TenantId { get; }
}
