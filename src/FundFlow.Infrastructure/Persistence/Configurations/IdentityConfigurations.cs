using FundFlow.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundFlow.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", Schemas.Identity, t =>
        {
            t.HasCheckConstraint("CK_Users_AccessFailedCount", "[AccessFailedCount] >= 0");
        });

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).HasMaxLength(254).IsRequired();
        builder.Property(u => u.NormalizedEmail).HasMaxLength(254).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(500);
        builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.PhoneNumber).HasMaxLength(30);
        builder.Property(u => u.SecurityStamp).HasMaxLength(64).IsRequired();

        // Email addresses are unique platform-wide: one identity belongs to exactly one organization.
        builder.HasIndex(u => u.NormalizedEmail).IsUnique();
        builder.HasIndex(u => new { u.TenantId, u.IsActive });

        builder.HasMany(u => u.UserRoles)
            .WithOne()
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(u => u.UserRoles).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles", Schemas.Identity);
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.Property(r => r.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(500);

        // Names are unique per tenant; platform roles (TenantId NULL) share one namespace. EF would add
        // "WHERE TenantId IS NOT NULL" to a unique index over a nullable column, which would stop enforcing the
        // platform namespace; HasFilter(null) keeps NULL a comparable value.
        builder.HasIndex(r => new { r.TenantId, r.NormalizedName }).IsUnique().HasFilter(null);

        builder.HasMany(r => r.Permissions)
            .WithOne()
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions", Schemas.Identity);
        builder.HasKey(p => p.Name);
        builder.Property(p => p.Name).HasMaxLength(100);
        builder.Property(p => p.Module).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(300).IsRequired();
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions", Schemas.Identity);
        builder.HasKey(rp => new { rp.RoleId, rp.PermissionName });
        builder.Property(rp => rp.PermissionName).HasMaxLength(100);

        builder.HasOne<Permission>()
            .WithMany()
            .HasForeignKey(rp => rp.PermissionName)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles", Schemas.Identity);
        builder.HasKey(ur => new { ur.UserId, ur.RoleId });

        builder.HasOne(ur => ur.Role)
            .WithMany()
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(ur => ur.RoleId);
    }
}

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("UserSessions", Schemas.Identity);
        builder.HasKey(s => s.Id);

        builder.Property(s => s.RefreshTokenHash).HasMaxLength(100).IsRequired();
        builder.Property(s => s.PreviousRefreshTokenHash).HasMaxLength(100);
        builder.Property(s => s.IpAddress).HasMaxLength(64);
        builder.Property(s => s.UserAgent).HasMaxLength(512);
        builder.Property(s => s.RevokedReason).HasMaxLength(100);
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasIndex(s => s.RefreshTokenHash).IsUnique();
        builder.HasIndex(s => s.PreviousRefreshTokenHash).HasFilter("[PreviousRefreshTokenHash] IS NOT NULL");
        builder.HasIndex(s => new { s.UserId, s.RevokedAt, s.ExpiresAt });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<UserToken>
{
    public void Configure(EntityTypeBuilder<UserToken> builder)
    {
        builder.ToTable("UserTokens", Schemas.Identity);
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Purpose).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.Purpose });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
