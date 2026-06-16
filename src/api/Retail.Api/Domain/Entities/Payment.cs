using Retail.Api.Common.Enums;
using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// A payment event against an <see cref="Order"/> (DATABASE_DESIGN §3.13).
/// </summary>
/// <remarks>
/// One order can have several payment rows over its life — typically a charge, and later a
/// refund. <see cref="AmountCents"/> is positive for a charge and negative for a refund, so
/// the rows sum to the net amount captured. The two Stripe id columns are filled at
/// different times (session id when the Checkout Session is created; payment-intent id once
/// the charge succeeds) and are indexed (filtered to NOT NULL) for webhook lookups.
/// </remarks>
public class Payment : IAuditableEntity
{
    /// <summary>Surrogate PK.</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the order this payment is for.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Navigation to the order.</summary>
    public Order? Order { get; set; }

    /// <summary>Payment provider. <c>"stripe"</c> for now; column exists so a second PSP wouldn't need a schema change.</summary>
    public string Provider { get; set; } = "stripe";

    /// <summary>Stripe Checkout Session id (<c>cs_...</c>); set when the session is created.</summary>
    public string? StripeSessionId { get; set; }

    /// <summary>Stripe PaymentIntent id (<c>pi_...</c>); set once the charge succeeds.</summary>
    public string? StripePaymentIntentId { get; set; }

    /// <summary>Amount in cents — positive for a charge, negative for a refund.</summary>
    public int AmountCents { get; set; }

    /// <summary>ISO-4217 currency code, <c>char(3)</c>. Defaults to AUD.</summary>
    public string Currency { get; set; } = "AUD";

    /// <summary>Lifecycle status. New rows start <see cref="PaymentStatus.Created"/>.</summary>
    public PaymentStatus Status { get; set; } = PaymentStatus.Created;

    /// <summary>Raw body of the last Stripe event for this payment, for audit/debug. JSON string, nullable.</summary>
    public string? RawPayloadJson { get; set; }

    // ── IAuditableEntity (stamped by AuditingInterceptor) ────────────────────
    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }
    /// <inheritdoc />
    public string? CreatedBy { get; set; }
    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }
    /// <inheritdoc />
    public string? UpdatedBy { get; set; }
}
