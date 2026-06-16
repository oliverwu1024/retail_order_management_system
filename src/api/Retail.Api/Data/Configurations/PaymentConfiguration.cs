using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Common.Enums;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="Payment"/> (DATABASE_DESIGN §3.13).</summary>
public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payment");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Provider).IsRequired().HasMaxLength(40).HasDefaultValue("stripe");
        builder.Property(p => p.StripeSessionId).HasMaxLength(120);
        builder.Property(p => p.StripePaymentIntentId).HasMaxLength(120);
        // char(3) ISO-4217. IsFixedLength → char, not nvarchar.
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3).IsFixedLength().HasDefaultValue("AUD");
        builder.Property(p => p.Status).HasColumnType("tinyint").HasDefaultValue(PaymentStatus.Created);
        builder.Property(p => p.RawPayloadJson).HasColumnType("nvarchar(max)");
        builder.Property(p => p.CreatedBy).HasMaxLength(64);
        builder.Property(p => p.UpdatedBy).HasMaxLength(64);

        builder.HasOne(p => p.Order)
            .WithMany(o => o.Payments)
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.OrderId, "IX_Payment_OrderId");
        // UNIQUE per Stripe session (filtered to the rows that have one): this is the
        // database-level idempotency guard for order creation — a concurrent webhook
        // redelivery that tries to create a second order/payment for the same session hits
        // this index, and OrderCreationService treats the violation as "already processed".
        builder.HasIndex(p => p.StripeSessionId, "UX_Payment_StripeSessionId")
            .IsUnique()
            .HasFilter("[StripeSessionId] IS NOT NULL");
        builder.HasIndex(p => p.StripePaymentIntentId, "IX_Payment_StripePaymentIntentId")
            .HasFilter("[StripePaymentIntentId] IS NOT NULL");
    }
}
