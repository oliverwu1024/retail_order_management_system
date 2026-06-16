using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="OrderPriceBreakdown"/> (DATABASE_DESIGN §3.21).</summary>
public sealed class OrderPriceBreakdownConfiguration : IEntityTypeConfiguration<OrderPriceBreakdown>
{
    public void Configure(EntityTypeBuilder<OrderPriceBreakdown> builder)
    {
        builder.ToTable("OrderPriceBreakdown");
        builder.HasKey(b => b.Id);

        // Voucher/loyalty fields default 0 and stay 0 until Phase 7.
        builder.Property(b => b.VoucherDiscountCents).HasDefaultValue(0);
        builder.Property(b => b.LoyaltyRedeemDiscountCents).HasDefaultValue(0);
        builder.Property(b => b.ShippingCents).HasDefaultValue(0);
        builder.Property(b => b.TaxCents).HasDefaultValue(0);
        builder.Property(b => b.PipelineVersion).IsRequired().HasMaxLength(20).HasDefaultValue("v1");
        builder.Property(b => b.CreatedBy).HasMaxLength(64);
        builder.Property(b => b.UpdatedBy).HasMaxLength(64);

        // 1:1 with Order; deleting the order removes its breakdown.
        builder.HasOne(b => b.Order)
            .WithOne(o => o.PriceBreakdown)
            .HasForeignKey<OrderPriceBreakdown>(b => b.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => b.OrderId, "UX_OrderPriceBreakdown_OrderId").IsUnique();
    }
}
