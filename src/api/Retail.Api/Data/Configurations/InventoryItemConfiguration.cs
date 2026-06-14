using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="InventoryItem"/> (DATABASE_DESIGN §3.7).</summary>
public sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("InventoryItem");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.OnHand).HasDefaultValue(0);
        builder.Property(i => i.Reserved).HasDefaultValue(0);
        builder.Property(i => i.CreatedBy).HasMaxLength(64);
        builder.Property(i => i.UpdatedBy).HasMaxLength(64);

        // SQL Server rowversion → optimistic concurrency token.
        builder.Property(i => i.RowVersion).IsRowVersion();

        // Available = OnHand − Reserved is computed in C#, never persisted.
        builder.Ignore(i => i.Available);

        // 1:1 with the variant; deleting the variant cascades to its stock row.
        builder.HasOne(i => i.Variant)
            .WithOne(v => v.Inventory)
            .HasForeignKey<InventoryItem>(i => i.ProductVariantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.ProductVariantId)
            .IsUnique()
            .HasDatabaseName("UX_InventoryItem_ProductVariantId");
    }
}
