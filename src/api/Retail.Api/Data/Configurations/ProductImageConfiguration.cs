using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="ProductImage"/> (PRODUCT_IMAGES_SCOPE).</summary>
public sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("ProductImage");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.BlobKey).IsRequired().HasMaxLength(260);
        builder.Property(i => i.AltText).HasMaxLength(200);
        builder.Property(i => i.SortOrder).HasDefaultValue(0);
        builder.Property(i => i.IsPrimary).HasDefaultValue(false);
        builder.Property(i => i.CreatedBy).HasMaxLength(64);
        builder.Property(i => i.UpdatedBy).HasMaxLength(64);

        // Owning product: a product's gallery is deleted with it (products are soft-deleted in
        // practice, so this cascade rarely fires).
        builder.HasOne(i => i.Product)
            .WithMany(p => p.Images)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Variant association is optional. NoAction (not Cascade) to avoid the multiple-cascade-path
        // error — Product already cascades, and Product → Variant → Image would be a second path.
        // The catalog service detaches a variant's images when a variant is removed.
        builder.HasOne(i => i.ProductVariant)
            .WithMany()
            .HasForeignKey(i => i.ProductVariantId)
            .OnDelete(DeleteBehavior.NoAction);

        // At most one primary image per product (DB-enforced).
        builder.HasIndex(i => i.ProductId)
            .IsUnique()
            .HasFilter("[IsPrimary] = 1")
            .HasDatabaseName("UX_ProductImage_Primary");

        // Loading a product's gallery in display order.
        builder.HasIndex(i => new { i.ProductId, i.SortOrder })
            .HasDatabaseName("IX_ProductImage_ProductId_SortOrder");

        // Filtering to a variant's images.
        builder.HasIndex(i => i.ProductVariantId)
            .HasFilter("[ProductVariantId] IS NOT NULL")
            .HasDatabaseName("IX_ProductImage_ProductVariantId");

        // Stay consistent with Product's soft-delete filter so a deleted product's images never
        // surface (also silences EF's filtered-principal navigation warning).
        builder.HasQueryFilter(i => !i.Product!.IsDeleted);
    }
}
