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

        // At most ONE open cart per owner — promotes the app invariant (CartService fetch-or-create)
        // to a DB guarantee, so a racing double "first add" can't leak a second open cart. The SQL
        // filter can't reference the enum name, so Open is spelled as its tinyint value 1.
        //
        // Member: one Open cart per profile (guest carts have a null profile id → filtered out).
        // This filtered-unique index also serves the "open cart for this profile" lookup.
        builder.HasIndex(c => c.CustomerProfileId, "UX_Cart_OpenPerProfile")
            .IsUnique()
            .HasFilter("[Status] = 1 AND [CustomerProfileId] IS NOT NULL");
        // Guest: one Open cart per cookie key (member carts have a null key → filtered out).
        builder.HasIndex(c => c.AnonymousKey, "UX_Cart_OpenPerAnonymousKey")
            .IsUnique()
            .HasFilter("[Status] = 1 AND [AnonymousKey] IS NOT NULL");
        // Sweeper scan ("open carts past their expiry").
        builder.HasIndex(c => new { c.ExpiresAt, c.Status }, "IX_Cart_ExpiresAt_Status");
    }
}
