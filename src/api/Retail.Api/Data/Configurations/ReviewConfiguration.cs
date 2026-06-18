using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="Review"/> (DATABASE_DESIGN §3.15) — Phase 4.</summary>
public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        // CHECK constraint declared via the table builder (EF Core 10) so the
        // 1..5 rating bound lives in the schema, not just FluentValidation.
        builder.ToTable("Review", t =>
            t.HasCheckConstraint("CK_Review_Rating", "[Rating] BETWEEN 1 AND 5"));
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Rating).HasColumnType("tinyint");
        builder.Property(r => r.Body).IsRequired().HasMaxLength(4000);

        // decimal(4,3) → range −9.999..9.999; we only ever store −1..1. Nullable: unscored until processed.
        builder.Property(r => r.SentimentScore).HasPrecision(4, 3);
        builder.Property(r => r.SentimentLabel).HasColumnType("tinyint");

        builder.Property(r => r.IsDeleted).HasDefaultValue(false);
        builder.Property(r => r.CreatedBy).HasMaxLength(64);
        builder.Property(r => r.UpdatedBy).HasMaxLength(64);

        // Cascade: a review is a child of its product. Products are soft-deleted in
        // practice, so this rarely fires — it just keeps the dependency direction honest.
        builder.HasOne(r => r.Product)
            .WithMany(p => p.Reviews)
            .HasForeignKey(r => r.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: a customer profile can't be hard-deleted while it has reviews, and it
        // keeps Review to a single cascade path (avoids SQL Server's multiple-cascade-paths error).
        builder.HasOne(r => r.CustomerProfile)
            .WithMany()
            .HasForeignKey(r => r.CustomerProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        // Storefront read: newest reviews for a product first (CreatedAt from IAuditableEntity).
        builder.HasIndex(r => new { r.ProductId, r.CreatedAt }, "IX_Review_ProductId_CreatedAt");

        // One LIVE review per customer per product — filtered to non-deleted so a soft-deleted
        // review doesn't block re-reviewing.
        builder.HasIndex(r => new { r.ProductId, r.CustomerProfileId }, "UX_Review_ProductId_CustomerProfileId")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}
