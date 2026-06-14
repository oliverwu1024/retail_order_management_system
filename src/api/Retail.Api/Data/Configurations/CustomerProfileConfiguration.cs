using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="CustomerProfile"/> (DATABASE_DESIGN §3.2).</summary>
public sealed class CustomerProfileConfiguration : IEntityTypeConfiguration<CustomerProfile>
{
    public void Configure(EntityTypeBuilder<CustomerProfile> builder)
    {
        builder.ToTable("CustomerProfile");
        builder.HasKey(p => p.Id);

        // AppUserId mirrors Identity's Id column type: nvarchar(450).
        builder.Property(p => p.AppUserId).IsRequired().HasMaxLength(450);
        builder.Property(p => p.DisplayName).IsRequired().HasMaxLength(120);
        builder.Property(p => p.Phone).HasMaxLength(32);
        builder.Property(p => p.CreatedBy).HasMaxLength(64);
        builder.Property(p => p.UpdatedBy).HasMaxLength(64);

        // 1:1 with the Identity user. Cascade: deleting the user removes their profile
        // (and, transitively, their addresses) rather than orphaning it.
        builder.HasOne(p => p.User)
            .WithOne()
            .HasForeignKey<CustomerProfile>(p => p.AppUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique → enforces the 1:1 (one profile per user) at the DB level.
        builder.HasIndex(p => p.AppUserId)
            .IsUnique()
            .HasDatabaseName("UX_CustomerProfile_AppUserId");
    }
}
