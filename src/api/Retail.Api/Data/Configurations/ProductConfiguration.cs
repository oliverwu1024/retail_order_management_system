using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="Product"/> (DATABASE_DESIGN §3.5).</summary>
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Product");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Sku).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Slug).IsRequired().HasMaxLength(160);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        // Description left unbounded → nvarchar(max).
        builder.Property(p => p.SeoTitle).HasMaxLength(200);
        builder.Property(p => p.SeoDescription).HasMaxLength(400);
        builder.Property(p => p.BrandName).HasMaxLength(120);
        builder.Property(p => p.PrimaryImageBlobKey).HasMaxLength(260);
        builder.Property(p => p.IsPublished).HasDefaultValue(false);
        builder.Property(p => p.IsDeleted).HasDefaultValue(false);
        builder.Property(p => p.CreatedBy).HasMaxLength(64);
        builder.Property(p => p.UpdatedBy).HasMaxLength(64);

        // Restrict: a category can't be hard-deleted while products reference it.
        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.Sku)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Product_Sku");

        builder.HasIndex(p => p.Slug)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Product_Slug");

        // Composite index supporting the storefront listing (filter by category +
        // published), ordered category-first (the equality predicate).
        builder.HasIndex(p => new { p.CategoryId, p.IsPublished })
            .HasDatabaseName("IX_Product_CategoryId_IsPublished");
    }
}
