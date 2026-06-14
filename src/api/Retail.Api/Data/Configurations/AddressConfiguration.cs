using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="Address"/> (DATABASE_DESIGN §3.3).</summary>
public sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("Address");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Line1).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Line2).HasMaxLength(200);
        builder.Property(a => a.City).IsRequired().HasMaxLength(120);
        builder.Property(a => a.Region).HasMaxLength(120);
        builder.Property(a => a.PostalCode).IsRequired().HasMaxLength(20);
        // char(2): ISO-3166 alpha-2. IsFixedLength → char, not nvarchar.
        builder.Property(a => a.Country).IsRequired().HasMaxLength(2).IsFixedLength();
        builder.Property(a => a.IsDefaultShipping).HasDefaultValue(false);
        builder.Property(a => a.IsDefaultBilling).HasDefaultValue(false);
        builder.Property(a => a.CreatedBy).HasMaxLength(64);
        builder.Property(a => a.UpdatedBy).HasMaxLength(64);

        // Owned by the profile. Cascade: deleting a profile removes its addresses.
        builder.HasOne(a => a.CustomerProfile)
            .WithMany(p => p.Addresses)
            .HasForeignKey(a => a.CustomerProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        // Three indexes share the CustomerProfileId column, so each needs the NAMED
        // HasIndex overload — without distinct names EF collapses them into one index
        // (the last definition wins) instead of creating three.

        // Plain FK index for "list a profile's addresses".
        builder.HasIndex(a => a.CustomerProfileId, "IX_Address_CustomerProfileId");

        // Filtered unique indexes promote "at most one default per axis per profile"
        // from an app convention to a DB guarantee — a second default-shipping (or
        // default-billing) row for the same profile is rejected by SQL Server itself.
        // The service clears the prior default before setting a new one so this never
        // trips on a legitimate change.
        builder.HasIndex(a => a.CustomerProfileId, "UX_Address_DefaultShipping")
            .IsUnique()
            .HasFilter("[IsDefaultShipping] = 1");

        builder.HasIndex(a => a.CustomerProfileId, "UX_Address_DefaultBilling")
            .IsUnique()
            .HasFilter("[IsDefaultBilling] = 1");
    }
}
