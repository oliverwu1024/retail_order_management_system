using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Common.Enums;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="Cart"/> (DATABASE_DESIGN §3.8).</summary>
public sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Cart");
        builder.HasKey(c => c.Id);

        // char(36): a GUID in string form. Only guest carts carry it (null for member carts).
        builder.Property(c => c.AnonymousKey).HasMaxLength(36).IsFixedLength();
        // byte-backed enum → tinyint; default 1 (Open) at the DB.
        builder.Property(c => c.Status).HasColumnType("tinyint").HasDefaultValue(CartStatus.Open);
        builder.Property(c => c.CreatedBy).HasMaxLength(64);
        builder.Property(c => c.UpdatedBy).HasMaxLength(64);

        // Carts are ephemeral: deleting a profile takes its carts with it.
        builder.HasOne(c => c.CustomerProfile)
            .WithMany()
            .HasForeignKey(c => c.CustomerProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        // Member lookup ("the open cart for this profile").
        builder.HasIndex(c => new { c.CustomerProfileId, c.Status }, "IX_Cart_CustomerProfileId_Status");
        // Guest lookup by cookie key — filtered to the rows that actually have a key.
        builder.HasIndex(c => new { c.AnonymousKey, c.Status }, "IX_Cart_AnonymousKey_Status")
            .HasFilter("[AnonymousKey] IS NOT NULL");
        // Sweeper scan ("open carts past their expiry").
        builder.HasIndex(c => new { c.ExpiresAt, c.Status }, "IX_Cart_ExpiresAt_Status");
    }
}
