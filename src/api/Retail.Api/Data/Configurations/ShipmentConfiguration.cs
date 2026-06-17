using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Common.Enums;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="Shipment"/> (DATABASE_DESIGN §3.14).</summary>
public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipment");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Carrier).HasMaxLength(60);
        builder.Property(s => s.TrackingNumber).HasMaxLength(120);
        builder.Property(s => s.Status).HasColumnType("tinyint").HasDefaultValue(ShipmentStatus.Pending);
        builder.Property(s => s.CreatedBy).HasMaxLength(64);
        builder.Property(s => s.UpdatedBy).HasMaxLength(64);

        // 1:0..1 — an order has at most one shipment. Cascade keeps the dependency direction
        // honest (a shipment is a child of its order); orders aren't hard-deleted in practice,
        // so this rarely fires.
        builder.HasOne(s => s.Order)
            .WithOne(o => o.Shipment)
            .HasForeignKey<Shipment>(s => s.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Names the unique index the 1:1 relationship implies (one shipment per order). Dropping
        // this index is the clean migration path to multi-shipment later.
        builder.HasIndex(s => s.OrderId, "UX_Shipment_OrderId").IsUnique();

        // Filtered to the shipped rows (those with a tracking number) — supports "find by
        // tracking number" without indexing the many null rows.
        builder.HasIndex(s => s.TrackingNumber, "IX_Shipment_TrackingNumber")
            .HasFilter("[TrackingNumber] IS NOT NULL");
    }
}
