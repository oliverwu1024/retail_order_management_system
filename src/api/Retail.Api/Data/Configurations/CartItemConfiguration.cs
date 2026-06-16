using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="CartItem"/> (DATABASE_DESIGN §3.9).</summary>
public sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("CartItem");
        builder.HasKey(ci => ci.Id);

        builder.Property(ci => ci.CreatedBy).HasMaxLength(64);
        builder.Property(ci => ci.UpdatedBy).HasMaxLength(64);

        // Deleting a cart cascades to its items.
        builder.HasOne(ci => ci.Cart)
            .WithMany(c => c.Items)
            .HasForeignKey(ci => ci.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        // Don't let a variant be hard-deleted while it sits in carts (variants deactivate, not delete).
        builder.HasOne(ci => ci.ProductVariant)
            .WithMany()
            .HasForeignKey(ci => ci.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(ci => ci.CartId, "IX_CartItem_CartId");
        // One line per (cart, variant): adding the same variant bumps quantity instead of duplicating.
        builder.HasIndex(ci => new { ci.CartId, ci.ProductVariantId }, "UX_CartItem_CartId_ProductVariantId")
            .IsUnique();
    }
}
