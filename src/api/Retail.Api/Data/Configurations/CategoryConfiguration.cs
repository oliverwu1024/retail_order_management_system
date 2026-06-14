using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="Category"/> (DATABASE_DESIGN §3.4).</summary>
public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Category");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Slug).IsRequired().HasMaxLength(140);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(140);
        builder.Property(c => c.IsDeleted).HasDefaultValue(false);
        builder.Property(c => c.CreatedBy).HasMaxLength(64);
        builder.Property(c => c.UpdatedBy).HasMaxLength(64);

        // Self-referencing tree. Restrict delete so a category with children can't
        // be hard-deleted out from under them (we soft-delete anyway).
        builder.HasOne(c => c.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique slug among NON-deleted rows (filtered index) — lets a deleted
        // category's slug be reused.
        builder.HasIndex(c => c.Slug)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Category_Slug");

        builder.HasIndex(c => c.ParentId).HasDatabaseName("IX_Category_ParentId");
    }
}
