using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="OrderLine"/> (DATABASE_DESIGN §3.12).</summary>
public sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("OrderLine");
        builder.HasKey(ol => ol.Id);

        builder.Property(ol => ol.SkuSnapshot).IsRequired().HasMaxLength(64);
        builder.Property(ol => ol.NameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(ol => ol.CreatedBy).HasMaxLength(64);
        builder.Property(ol => ol.UpdatedBy).HasMaxLength(64);

        // Deleting an order cascades to its lines.
        builder.HasOne(ol => ol.Order)
            .WithMany(o => o.Lines)
            .HasForeignKey(ol => ol.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Keep the variant FK for traceability, but Restrict so order history can't be orphaned.
        builder.HasOne(ol => ol.ProductVariant)
            .WithMany()
            .HasForeignKey(ol => ol.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(ol => ol.OrderId, "IX_OrderLine_OrderId");
        builder.HasIndex(ol => ol.ProductVariantId, "IX_OrderLine_ProductVariantId");
    }
}
