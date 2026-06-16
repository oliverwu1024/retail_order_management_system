using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// The price computation behind an <see cref="Order"/> (DATABASE_DESIGN §3.21) — 1:1 with
/// the order, persisted at placement so the math is reproducible forever.
/// </summary>
/// <remarks>
/// <para>
/// Kept in a SIDE table (not on <see cref="Order"/>) for two reasons: it keeps the Order row
/// — read on every orders-grid query — lean, and it lets the Phase 7 pricing pipeline add
/// voucher/loyalty math without altering the Order schema.
/// </para>
/// <para>
/// In Phase 2 only <see cref="SubtotalCents"/>/<see cref="TaxCents"/>/<see cref="ShippingCents"/>/
/// <see cref="TotalCents"/> are populated; the voucher and loyalty fields stay 0 until Phase 7
/// introduces those features. <see cref="PipelineVersion"/> records which pricing model
/// produced the numbers, so a future pipeline change is auditable per order.
/// </para>
/// </remarks>
public class OrderPriceBreakdown : IAuditableEntity
{
    /// <summary>Surrogate PK.</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the owning order (unique — 1:1).</summary>
    public Guid OrderId { get; set; }

    /// <summary>Navigation to the order.</summary>
    public Order? Order { get; set; }

    /// <summary>Sum of line totals, in cents.</summary>
    public int SubtotalCents { get; set; }

    /// <summary>Voucher discount in cents. Phase 7 — stays 0 in Phase 2.</summary>
    public int VoucherDiscountCents { get; set; }

    /// <summary>Loyalty-points redemption discount in cents. Phase 7 — stays 0 in Phase 2.</summary>
    public int LoyaltyRedeemDiscountCents { get; set; }

    /// <summary>Shipping in cents (0 in Phase 2).</summary>
    public int ShippingCents { get; set; }

    /// <summary>Tax in cents (flat 10% GST in MVP).</summary>
    public int TaxCents { get; set; }

    /// <summary>Grand total in cents.</summary>
    public int TotalCents { get; set; }

    /// <summary>Pricing-pipeline version that produced these numbers; <c>"v1"</c> for the Phase 2 flat model.</summary>
    public string PipelineVersion { get; set; } = "v1";

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
