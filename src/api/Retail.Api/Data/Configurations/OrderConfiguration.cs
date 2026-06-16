using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Retail.Api.Common.Enums;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="Order"/> (DATABASE_DESIGN §3.11, amended for guest checkout).</summary>
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Order"); // EF brackets the reserved word → [Order].
        builder.HasKey(o => o.Id);

        // OrderNumber is assigned by the DB from Seq_OrderNumber (created in
        // RetailDbContext.OnModelCreating). HasDefaultValueSql makes EF treat it as
        // store-generated: it omits the column on INSERT and reads the value back.
        builder.Property(o => o.OrderNumber)
            .HasDefaultValueSql("NEXT VALUE FOR Seq_OrderNumber");

        builder.Property(o => o.GuestEmail).HasMaxLength(256);
        builder.Property(o => o.Status).HasColumnType("tinyint").HasDefaultValue(OrderStatus.Pending);
        builder.Property(o => o.TaxCents).HasDefaultValue(0);
        builder.Property(o => o.ShippingCents).HasDefaultValue(0);
        builder.Property(o => o.CreatedBy).HasMaxLength(64);
        builder.Property(o => o.UpdatedBy).HasMaxLength(64);

        // Optimistic concurrency token for status transitions (refund webhook vs customer cancel).
        builder.Property(o => o.RowVersion).IsRowVersion();

        // Address value objects <-> JSON columns (same converter+comparer pattern as
        // ProductVariant.Options — the comparer lets EF detect in-place mutations).
        ConfigureAddressSnapshot(builder, o => o.ShippingAddress, "ShippingAddressJson");
        ConfigureAddressSnapshot(builder, o => o.BillingAddress, "BillingAddressJson");

        // Member orders link to a profile; guest orders leave it null. Restrict so a profile
        // can never be deleted out from under its order history.
        builder.HasOne(o => o.CustomerProfile)
            .WithMany()
            .HasForeignKey(o => o.CustomerProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => o.OrderNumber, "UX_Order_OrderNumber").IsUnique();
        // Customer order history (newest first) and the staff status queue.
        builder.HasIndex(o => new { o.CustomerProfileId, o.PlacedAt }, "IX_Order_CustomerProfileId_PlacedAt");
        builder.HasIndex(o => new { o.Status, o.PlacedAt }, "IX_Order_Status_PlacedAt");
    }

    /// <summary>
    /// Maps an <see cref="OrderAddressSnapshot"/> property to a single <c>nvarchar(max)</c>
    /// JSON column. Factored out because Order has two of them (shipping + billing).
    /// </summary>
    private static void ConfigureAddressSnapshot(
        EntityTypeBuilder<Order> builder,
        Expression<Func<Order, OrderAddressSnapshot>> property,
        string columnName)
    {
        var converter = new ValueConverter<OrderAddressSnapshot, string>(
            snapshot => JsonSerializer.Serialize(snapshot, (JsonSerializerOptions?)null),
            json => JsonSerializer.Deserialize<OrderAddressSnapshot>(json, (JsonSerializerOptions?)null)
                    ?? new OrderAddressSnapshot());

        var comparer = new ValueComparer<OrderAddressSnapshot>(
            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
                      == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(StringComparison.Ordinal),
            v => JsonSerializer.Deserialize<OrderAddressSnapshot>(
                     JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)
                 ?? new OrderAddressSnapshot());

        builder.Property(property)
            .HasColumnName(columnName)
            .HasColumnType("nvarchar(max)")
            .IsRequired()
            .HasConversion(converter, comparer);
    }
}
