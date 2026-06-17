using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Common.Enums;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="InventoryReservation"/> (DATABASE_DESIGN §3.10).</summary>
public sealed class InventoryReservationConfiguration : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        // Exactly one owner: cart-bound (pre-payment) XOR order-bound (post-commit). The service
        // upholds this (insert sets CartId; commit re-homes to OrderId + nulls CartId); the CHECK
        // is the DB backstop, mirroring CK_Order_Identity.
        builder.ToTable("InventoryReservation", table => table.HasCheckConstraint(
            "CK_InventoryReservation_Owner",
            "([CartId] IS NOT NULL AND [OrderId] IS NULL) OR ([CartId] IS NULL AND [OrderId] IS NOT NULL)"));
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).HasColumnType("tinyint").HasDefaultValue(ReservationStatus.Active);
        builder.Property(r => r.CreatedBy).HasMaxLength(64);
        builder.Property(r => r.UpdatedBy).HasMaxLength(64);

        // The hold draws from a stock row; never delete stock that has holds against it.
        builder.HasOne(r => r.InventoryItem)
            .WithMany()
            .HasForeignKey(r => r.InventoryItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // CartId / OrderId are nullable FKs (exactly one set at a time). NoAction: reservations
        // are released/committed explicitly by the service, never cascade-deleted — and this
        // avoids a multiple-cascade-path error on a row reachable from several parents.
        builder.HasOne<Cart>()
            .WithMany()
            .HasForeignKey(r => r.CartId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(r => new { r.InventoryItemId, r.Status }, "IX_InventoryReservation_InventoryItemId_Status");
        // Sweeper scan ("active holds past expiry").
        builder.HasIndex(r => new { r.ExpiresAt, r.Status }, "IX_InventoryReservation_ExpiresAt_Status");
        builder.HasIndex(r => r.CartId, "IX_InventoryReservation_CartId");
        builder.HasIndex(r => r.OrderId, "IX_InventoryReservation_OrderId");
    }
}
