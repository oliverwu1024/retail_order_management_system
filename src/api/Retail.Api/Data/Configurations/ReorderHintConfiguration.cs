using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="ReorderHint"/> (DATABASE_DESIGN §3.18) — Phase 5B.</summary>
public sealed class ReorderHintConfiguration : IEntityTypeConfiguration<ReorderHint>
{
    public void Configure(EntityTypeBuilder<ReorderHint> builder)
    {
        builder.ToTable("ReorderHint");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.Reasoning).IsRequired().HasMaxLength(400);
        builder.Property(h => h.Dismissed).HasDefaultValue(false);
        builder.Property(h => h.CreatedBy).HasMaxLength(64);
        builder.Property(h => h.UpdatedBy).HasMaxLength(64);

        // Cascade: a reorder hint is a disposable derived child of its variant (same as DemandForecast).
        // One-directional FK; one upserted row per variant (PHASE_5B_FORECAST_SCOPE §3.5).
        builder.HasOne(h => h.ProductVariant)
            .WithMany()
            .HasForeignKey(h => h.ProductVariantId)
            .OnDelete(DeleteBehavior.Cascade);

        // Ranked "top reorder" list: active hints (Dismissed = 0) by quantity, per variant.
        builder.HasIndex(h => new { h.ProductVariantId, h.Dismissed, h.RecommendedOrderQty },
            "IX_ReorderHint_ProductVariantId_Dismissed_RecommendedOrderQty");
    }
}
