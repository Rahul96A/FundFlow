using System.Reflection;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Domain.Audit;
using FundFlow.Domain.Identity;
using FundFlow.Domain.Organizations;
using FundFlow.Domain.SharedKernel;
using MassTransit;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FundFlow.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the modular monolith (each module owns its own SQL schema, see <see cref="Schemas"/>).
/// <para>
/// <b>Tenant isolation lives here.</b> Every tenant-owned entity type gets a global query filter derived from
/// <see cref="ITenantContext"/>; the filters are applied by convention so a new entity cannot forget one.
/// With no tenant scope established, tenant-owned tables return nothing (fail closed).
/// </para>
/// </summary>
public sealed class AppDbContext : DbContext, IAppDbContext, IDataProtectionKeyContext
{
    private readonly ITenantContext _tenant;
    private readonly IDomainEventDispatcher? _domainEvents;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ITenantContext tenant,
        IDomainEventDispatcher? domainEvents = null)
        : base(options)
    {
        _tenant = tenant;
        _domainEvents = domainEvents;
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationSettings> OrganizationSettings => Set<OrganizationSettings>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // Read by the query filters below. EF turns member accesses on the context into per-query parameters,
    // so the cached model is shared by all instances while each query sees its own tenant.
    private Guid? CurrentTenantId => _tenant.TenantId;

    private bool IsPlatformScope => _tenant.IsPlatformScope;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys", Schemas.Security);

        // MassTransit transactional outbox / inbox tables.
        modelBuilder.AddInboxStateEntity(b => b.ToTable("InboxState", Schemas.Messaging));
        modelBuilder.AddOutboxMessageEntity(b => b.ToTable("OutboxMessage", Schemas.Messaging));
        modelBuilder.AddOutboxStateEntity(b => b.ToTable("OutboxState", Schemas.Messaging));

        ApplyConventions(modelBuilder);
    }

    private void ApplyConventions(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            var clrType = entityType.ClrType;

            if (typeof(Entity).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType).Ignore(nameof(Entity.DomainEvents));
                modelBuilder.Entity(clrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
            }

            if (typeof(AuditableEntity).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType).Property(nameof(AuditableEntity.RowVersion)).IsRowVersion();
            }

            if (typeof(ITenantEntity).IsAssignableFrom(clrType))
            {
                InvokeGeneric(nameof(ApplyStrictTenantConvention), clrType, modelBuilder);
            }
            else if (typeof(IOptionalTenantEntity).IsAssignableFrom(clrType))
            {
                InvokeGeneric(nameof(ApplyOptionalTenantConvention), clrType, modelBuilder);
            }
        }

        // The organization registry itself: a tenant sees only its own row; platform operators see the registry.
        modelBuilder.Entity<Organization>().HasQueryFilter(o => IsPlatformScope || o.Id == CurrentTenantId);
    }

    private void InvokeGeneric(string methodName, Type entityType, ModelBuilder modelBuilder) =>
        GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(entityType)
            .Invoke(this, [modelBuilder]);

    /// <summary>Tenant-owned rows (donors, donations, ...): visible only inside their own tenant.</summary>
    private void ApplyStrictTenantConvention<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantEntity
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        EnsureTenantIndexAndKey<TEntity>(modelBuilder);
    }

    /// <summary>
    /// Rows that belong to a tenant but may also exist at platform level (TenantId == null): a tenant scope sees its
    /// own rows, platform scope sees only the platform rows, and an unscoped context sees nothing.
    /// </summary>
    private void ApplyOptionalTenantConvention<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IOptionalTenantEntity
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            (IsPlatformScope && e.TenantId == null)
            || (CurrentTenantId != null && e.TenantId == CurrentTenantId));
        EnsureTenantIndexAndKey<TEntity>(modelBuilder);
    }

    private static void EnsureTenantIndexAndKey<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class
    {
        var entity = modelBuilder.Entity<TEntity>();
        var entityType = entity.Metadata;
        var tenantProperty = entityType.FindProperty("TenantId");
        if (tenantProperty is null)
        {
            return;
        }

        // Every tenant-owned table gets a TenantId foreign key to Organizations (audit rows are the exception:
        // the log must outlive and never block anything else).
        var alreadyHasTenantKey = entityType.GetForeignKeys().Any(fk => fk.Properties.Contains(tenantProperty));
        if (!alreadyHasTenantKey && typeof(TEntity) != typeof(AuditLog))
        {
            entity.HasOne(typeof(Organization)).WithMany().HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict);
        }

        if (!entityType.GetIndexes().Any(i => i.Properties.FirstOrDefault() == tenantProperty)
            && !entityType.FindPrimaryKey()!.Properties.Contains(tenantProperty))
        {
            entity.HasIndex("TenantId");
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await DispatchDomainEventsAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Publishes domain events raised by tracked entities <em>before</em> the commit, repeating until handlers stop
    /// raising new events. Handlers may stage further rows and outbox messages that then commit atomically with the
    /// change that caused them.
    /// </summary>
    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        if (_domainEvents is null)
        {
            return;
        }

        const int maxRounds = 10;
        for (var round = 0; round < maxRounds; round++)
        {
            var entitiesWithEvents = ChangeTracker.Entries<Entity>()
                .Select(e => e.Entity)
                .Where(e => e.DomainEvents.Count > 0)
                .ToList();

            if (entitiesWithEvents.Count == 0)
            {
                return;
            }

            var events = entitiesWithEvents.SelectMany(e => e.DomainEvents).ToList();
            foreach (var entity in entitiesWithEvents)
            {
                entity.ClearDomainEvents();
            }

            foreach (var domainEvent in events)
            {
                await _domainEvents.DispatchAsync(domainEvent, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Domain events were still being raised after {maxRounds} rounds; a handler is probably raising events in a loop.");
    }
}
