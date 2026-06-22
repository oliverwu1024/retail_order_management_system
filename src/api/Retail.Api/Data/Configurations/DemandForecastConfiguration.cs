using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="DemandForecast"/> (DATABASE_DESIGN §3.17) — Phase 5B.</summary>
public sealed class DemandForecastConfiguration : IEntityTypeConfiguration<DemandForecast>
{
    public void Configure(EntityTypeBuilder<DemandForecast> builder)
    {
        builder.ToTable("DemandForecast");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Horizon).HasDefaultValue((short)14);
        builder.Property(f => f.ForecastedQty).HasPrecision(10, 2);
        builder.Property(f => f.LowerBound).HasPrecision(10, 2);
        builder.Property(f => f.UpperBound).HasPrecision(10, 2);
        builder.Property(f => f.Confidence).HasPrecision(4, 3);
        builder.Property(f => f.ModelVersion).IsRequired().HasMaxLength(40);
        builder.Property(f => f.CreatedBy).HasMaxLength(64);
        builder.Property(f => f.UpdatedBy).HasMaxLength(64);

        // Cascade: a forecast is a disposable derived child of its variant (matches InventoryItem →
        // ProductVariant). Single FK → no multiple-cascade-paths collision. One-directional (no
        // ProductVariant.Forecasts back-collection) — reads are direct queries.
        builder.HasOne(f => f.ProductVariant)
            .WithMany()
            .HasForeignKey(f => f.ProductVariantId)
            .OnDelete(DeleteBehavior.Cascade);

        // "Latest forecast per variant" read.
        builder.HasIndex(f => new { f.ProductVariantId, f.GeneratedAt }, "IX_DemandForecast_ProductVariantId_GeneratedAt");
    }
}
