using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="ProductVariant"/> (DATABASE_DESIGN §3.6).</summary>
public sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("ProductVariant");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Sku).IsRequired().HasMaxLength(64);
        builder.Property(v => v.PriceCents).IsRequired();
        builder.Property(v => v.IsActive).HasDefaultValue(true);
        builder.Property(v => v.CreatedBy).HasMaxLength(64);
        builder.Property(v => v.UpdatedBy).HasMaxLength(64);

        // Options <-> OptionsJson: store the dictionary as a JSON string. EF needs a
        // ValueComparer for a mutable reference-type property, or it can't detect
        // in-place mutations (and would warn). We compare by serialized form.
        var converter = new ValueConverter<Dictionary<string, string>, string>(
            options => JsonSerializer.Serialize(options, (JsonSerializerOptions?)null),
            json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, (JsonSerializerOptions?)null)
                    ?? new Dictionary<string, string>());

        var comparer = new ValueComparer<Dictionary<string, string>>(
            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
                      == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(StringComparison.Ordinal),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(
                     JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)
                 ?? new Dictionary<string, string>());

        builder.Property(v => v.Options)
            .HasColumnName("OptionsJson")
            .HasColumnType("nvarchar(max)")
            .IsRequired()
            .HasConversion(converter, comparer);

        // Deleting a product cascades to its variants.
        builder.HasOne(v => v.Product)
            .WithMany(p => p.Variants)
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(v => v.Sku).IsUnique().HasDatabaseName("UX_ProductVariant_Sku");
        builder.HasIndex(v => v.ProductId).HasDatabaseName("IX_ProductVariant_ProductId");
    }
}
