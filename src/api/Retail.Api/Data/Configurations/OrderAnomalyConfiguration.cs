using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="OrderAnomaly"/> (DATABASE_DESIGN §3.19) — Phase 5B.</summary>
public sealed class OrderAnomalyConfiguration : IEntityTypeConfiguration<OrderAnomaly>
{
    public void Configure(EntityTypeBuilder<OrderAnomaly> builder)
    {
        builder.ToTable("OrderAnomaly");
        builder.HasKey(a => a.Id);

        // decimal(8,3): a unit-less Z-score (or 0). Wide enough for any plausible |Z|.
        builder.Property(a => a.Score).HasPrecision(8, 3);
        builder.Property(a => a.Reason).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Acknowledged).HasDefaultValue(false);
        builder.Property(a => a.CreatedBy).HasMaxLength(64);
        builder.Property(a => a.UpdatedBy).HasMaxLength(64);

        // Cascade: an anomaly is a child of its order, matching every other single-FK Order
        // child (OrderLine/Payment/Shipment/OrderPriceBreakdown). One FK → no multiple-cascade-paths
        // collision. One-directional (no Order.Anomalies back-collection) — the ship-block check and
        // the scan idempotency run direct OrderAnomalies queries, not a loaded nav.
        builder.HasOne(a => a.Order)
            .WithMany()
            .HasForeignKey(a => a.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Lookup by order (idempotency check + ship-block guard).
        builder.HasIndex(a => a.OrderId, "IX_OrderAnomaly_OrderId");

        // Risk-queue read: unacknowledged first, newest first.
        builder.HasIndex(a => new { a.Acknowledged, a.DetectedAt }, "IX_OrderAnomaly_Acknowledged_DetectedAt");
    }
}
