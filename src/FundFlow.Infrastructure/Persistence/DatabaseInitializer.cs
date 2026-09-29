using FundFlow.Application.Common.Abstractions;
using FundFlow.Domain.Audit;
using FundFlow.Domain.Identity;
using FundFlow.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FundFlow.Infrastructure.Persistence;

/// <summary>Deployment-time database behaviour (section "Database").</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply pending migrations at startup. Development convenience only; production uses an explicit step.</summary>
    public bool AutoMigrate { get; set; }

    /// <summary>Create the demo organizations and users (Development / demo environments only).</summary>
    public bool SeedDemoData { get; set; }

    public string? DemoPassword { get; set; }

    /// <summary>Optional bootstrap of the first platform operator, applied by the explicit migration step.</summary>
    public string? SuperAdminEmail { get; set; }

    public string? SuperAdminPassword { get; set; }
}

/// <summary>
/// Everything that prepares a database for use, split into steps so deployments can run exactly what they need:
/// <c>dotnet FundFlow.Api.dll --migrate</c> runs <see cref="MigrateAsync"/> and <see cref="SyncReferenceDataAsync"/>;
/// Development additionally seeds demo data. Every step is idempotent and additive: none of them deletes tenant data.
/// </summary>
public sealed class DatabaseInitializer(
    IServiceScopeFactory scopes,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseInitializer> logger)
{
    private const int TenantBatchSize = 200;

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is up to date");
            return;
        }

        logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// Brings reference data in line with the code: the permission catalogue, the platform role, and every
    /// organization's system roles. Custom roles are never touched.
    /// </summary>
    public async Task SyncReferenceDataAsync(CancellationToken cancellationToken)
    {
        await SyncPermissionsAsync(cancellationToken);
        await SyncPlatformRoleAsync(cancellationToken);
        await SyncTenantSystemRolesAsync(cancellationToken);
    }

    private async Task SyncPermissionsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.Permissions.ToDictionaryAsync(p => p.Name, cancellationToken);
        foreach (var definition in Permissions.All)
        {
            if (existing.TryGetValue(definition.Name, out var permission))
            {
                permission.Update(definition.Module, definition.Description);
            }
            else
            {
                db.Permissions.Add(new Permission(definition.Name, definition.Module, definition.Description));
            }
        }

        var obsolete = existing.Keys.Where(name => !Permissions.IsKnown(name)).ToList();
        if (obsolete.Count > 0)
        {
            await db.RolePermissions.IgnoreQueryFilters().Where(rp => obsolete.Contains(rp.PermissionName)).ExecuteDeleteAsync(cancellationToken);
            foreach (var name in obsolete)
            {
                db.Permissions.Remove(existing[name]);
            }

            logger.LogWarning("Removed obsolete permissions: {Permissions}", string.Join(", ", obsolete));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SyncPlatformRoleAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var role = await db.Roles.IgnoreQueryFilters()
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.TenantId == null && r.NormalizedName == Role.NormalizeName(SystemRoles.SuperAdmin), cancellationToken);

        if (role is null)
        {
            db.Roles.Add(Role.CreateSystem(null, RoleTemplates.SuperAdmin));
        }
        else
        {
            role.ReplacePermissions(RoleTemplates.SuperAdmin.Permissions);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SyncTenantSystemRolesAsync(CancellationToken cancellationToken)
    {
        Guid? lastId = null;
        while (true)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var tenantIds = await db.Organizations.IgnoreQueryFilters().AsNoTracking()
                .Where(o => lastId == null || o.Id.CompareTo(lastId.Value) > 0)
                .OrderBy(o => o.Id)
                .Select(o => o.Id)
                .Take(TenantBatchSize)
                .ToListAsync(cancellationToken);

            if (tenantIds.Count == 0)
            {
                return;
            }

            var roles = await db.Roles.IgnoreQueryFilters()
                .Include(r => r.Permissions)
                .Where(r => r.IsSystem && r.TenantId != null && tenantIds.Contains(r.TenantId.Value))
                .ToListAsync(cancellationToken);
            var byTenant = roles.ToLookup(r => r.TenantId!.Value);

            foreach (var tenantId in tenantIds)
            {
                var tenantRoles = byTenant[tenantId].ToDictionary(r => r.Name, StringComparer.Ordinal);
                foreach (var template in RoleTemplates.Tenant)
                {
                    if (tenantRoles.TryGetValue(template.Name, out var role))
                    {
                        role.ReplacePermissions(template.Permissions);
                    }
                    else
                    {
                        db.Roles.Add(Role.CreateSystem(tenantId, template));
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            lastId = tenantIds[^1];
        }
    }

    /// <summary>Creates the first platform operator if none exists. Never modifies an existing account.</summary>
    public async Task EnsureSuperAdminAsync(string email, string password, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        sp.GetRequiredService<ITenantScope>().UsePlatform();

        var normalized = User.NormalizeEmail(email);
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken))
        {
            return;
        }

        var role = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.TenantId == null && r.NormalizedName == Role.NormalizeName(SystemRoles.SuperAdmin), cancellationToken);

        var user = User.Provision(null, email, "Platform", "Administrator", sp.GetRequiredService<IPasswordService>().Hash(password));
        user.SetRoles([role], null, sp.GetRequiredService<TimeProvider>().GetUtcNow());
        db.Users.Add(user);

        sp.GetRequiredService<IAuditLogger>().Record(new AuditEntry(
            AuditActions.UserCreated, nameof(User), user.Id.ToString(),
            NewValues: new { user.Email, Role = SystemRoles.SuperAdmin },
            UserId: user.Id, UserEmail: user.Email));

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Created platform administrator {Email}", email);
    }

    /// <summary>Realistic demo tenants for local development and sales demos. Safe to run repeatedly.</summary>
    public async Task SeedDemoDataAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.DemoPassword))
        {
            logger.LogWarning("Database:DemoPassword is not configured; skipping demo data");
            return;
        }

        var password = settings.DemoPassword;

        await EnsureSuperAdminAsync("superadmin@fundflow.test", password, cancellationToken);

        await SeedOrganizationAsync(
            "Hope Foundation",
            "hope-foundation",
            "America/New_York",
            [
                ("admin@hopefoundation.test", "Amelia", "Reyes", SystemRoles.OrganizationAdmin),
                ("fundraising@hopefoundation.test", "Marcus", "Chen", SystemRoles.FundraisingManager),
                ("finance@hopefoundation.test", "Priya", "Nair", SystemRoles.FinanceManager),
            ],
            password,
            cancellationToken);

        // A second tenant makes cross-tenant behaviour easy to demonstrate by hand.
        await SeedOrganizationAsync(
            "Riverside Animal Rescue",
            "riverside-rescue",
            "America/Chicago",
            [("admin@riverside.test", "Jordan", "Blake", SystemRoles.OrganizationAdmin)],
            password,
            cancellationToken);
    }

    private async Task SeedOrganizationAsync(
        string name,
        string slug,
        string timeZoneId,
        (string Email, string First, string Last, string Role)[] users,
        string password,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();

        if (await db.Organizations.IgnoreQueryFilters().AnyAsync(o => o.Slug == slug, cancellationToken))
        {
            return;
        }

        var organization = Organization.Register(name, slug, users[0].Email, timeZoneId, "USD");
        sp.GetRequiredService<ITenantScope>().UseTenant(organization.Id);

        var roles = RoleTemplates.Tenant.Select(t => Role.CreateSystem(organization.Id, t)).ToDictionary(r => r.Name);
        var hash = sp.GetRequiredService<IPasswordService>().Hash(password);
        var now = sp.GetRequiredService<TimeProvider>().GetUtcNow();

        db.Organizations.Add(organization);
        db.Roles.AddRange(roles.Values);

        foreach (var (email, first, last, roleName) in users)
        {
            var user = User.Provision(organization.Id, email, first, last, hash);
            user.SetRoles([roles[roleName]], null, now);
            db.Users.Add(user);
        }

        sp.GetRequiredService<IAuditLogger>().Record(new AuditEntry(
            AuditActions.OrganizationRegistered, nameof(Organization), organization.Id.ToString(),
            NewValues: new { organization.Name, organization.Slug, Seeded = true },
            TenantId: organization.Id));

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded demo organization {Organization} with {Users} users", name, users.Length);
    }
}
