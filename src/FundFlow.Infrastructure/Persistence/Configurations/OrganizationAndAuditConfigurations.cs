using FundFlow.Domain.Audit;
using FundFlow.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundFlow.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations", Schemas.Organizations);
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name).HasMaxLength(200).IsRequired();
        builder.Property(o => o.Slug).HasMaxLength(Organization.SlugMaxLength).IsRequired();
        builder.Property(o => o.LegalName).HasMaxLength(200);
        builder.Property(o => o.TaxId).HasMaxLength(50);
        builder.Property(o => o.Website).HasMaxLength(300);
        builder.Property(o => o.ContactEmail).HasMaxLength(254).IsRequired();
        builder.Property(o => o.PhoneNumber).HasMaxLength(30);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.SuspensionReason).HasMaxLength(500);

        builder.ComplexProperty(o => o.Address, address =>
        {
            address.Property(a => a.Line1).HasColumnName("AddressLine1").HasMaxLength(200);
            address.Property(a => a.Line2).HasColumnName("AddressLine2").HasMaxLength(200);
            address.Property(a => a.City).HasColumnName("AddressCity").HasMaxLength(100);
            address.Property(a => a.Region).HasColumnName("AddressRegion").HasMaxLength(100);
            address.Property(a => a.PostalCode).HasColumnName("AddressPostalCode").HasMaxLength(20);
            address.Property(a => a.Country).HasColumnName("AddressCountry").HasMaxLength(100);
        });

        builder.HasIndex(o => o.Slug).IsUnique();
        builder.HasIndex(o => o.Status);

        builder.HasOne(o => o.Settings)
            .WithOne()
            .HasForeignKey<OrganizationSettings>(s => s.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Settings).IsRequired();
    }
}

internal sealed class OrganizationSettingsConfiguration : IEntityTypeConfiguration<OrganizationSettings>
{
    public void Configure(EntityTypeBuilder<OrganizationSettings> builder)
    {
        builder.ToTable("OrganizationSettings", Schemas.Organizations, t =>
        {
            t.HasCheckConstraint("CK_OrganizationSettings_FiscalYearStartMonth", "[FiscalYearStartMonth] BETWEEN 1 AND 12");
        });
        builder.HasKey(s => s.Id);

        builder.Property(s => s.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(s => s.CurrencyCode).HasMaxLength(3).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(s => s.Locale).HasMaxLength(20).IsRequired();
        builder.Property(s => s.LogoUrl).HasMaxLength(500);
        builder.Property(s => s.BrandColor).HasMaxLength(7);

        builder.HasIndex(s => s.TenantId).IsUnique();
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs", Schemas.Audit);
        builder.HasKey(a => a.Id);

        builder.Property(a => a.UserEmail).HasMaxLength(320);
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(100);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.UserAgent).HasMaxLength(512);
        builder.Property(a => a.CorrelationId).HasMaxLength(100);
        builder.Property(a => a.OldValues).HasColumnType("nvarchar(max)");
        builder.Property(a => a.NewValues).HasColumnType("nvarchar(max)");

        builder.HasIndex(a => new { a.TenantId, a.Timestamp }).IsDescending(false, true);
        builder.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId });
        builder.HasIndex(a => new { a.TenantId, a.UserId, a.Timestamp });
    }
}
