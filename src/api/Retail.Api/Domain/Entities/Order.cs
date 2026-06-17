using Retail.Api.Common.Enums;
using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// A placed order (DATABASE_DESIGN §3.11, amended for guest checkout — see
/// docs/PHASE_2_SCOPE.md §3.2). Created off the back of the Stripe
/// <c>checkout.session.completed</c> webhook.
/// </summary>
/// <remarks>
/// <para>
/// IDENTITY: an order belongs to EITHER a member (<see cref="CustomerProfileId"/>) OR a
/// guest (<see cref="GuestEmail"/>) — exactly one is set, enforced in the service layer.
/// This is the deliberate deviation from DATABASE_DESIGN §3.11 (which made the profile FK
/// NOT NULL) that enables checkout without an account.
/// </para>
/// <para>
/// SNAPSHOTS: the address and line item details are copied onto the order at placement
/// (<see cref="ShippingAddress"/>/<see cref="BillingAddress"/> as JSON, plus
/// <see cref="OrderLine"/> SKU/name/price) so the order renders faithfully even after the
/// underlying product or address changes.
/// </para>
/// <para>
/// CONCURRENCY: <see cref="RowVersion"/> guards status transitions so two writers (e.g. the
/// refund webhook and a customer cancel) can't clobber each other — a stale update affects
/// 0 rows and surfaces as a 409.
/// </para>
/// </remarks>
public class Order : IAuditableEntity
{
    /// <summary>Surrogate PK.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Human-facing order number from the <c>Seq_OrderNumber</c> sequence (starts at 10001).
    /// The GUID <see cref="Id"/> remains the surrogate key; this is the friendly reference a
    /// customer quotes in support.
    /// </summary>
    public int OrderNumber { get; set; }

    /// <summary>FK to the buyer's profile; <c>null</c> for a guest order (then <see cref="GuestEmail"/> is set).</summary>
    public Guid? CustomerProfileId { get; set; }

    /// <summary>Navigation to the buyer's profile (null for guest orders).</summary>
    public CustomerProfile? CustomerProfile { get; set; }

    /// <summary>
    /// Buyer's email for a guest order; <c>null</c> for a member order (whose email lives on
    /// the Identity user). Exactly one of <see cref="CustomerProfileId"/> / <see cref="GuestEmail"/> is set.
    /// </summary>
    public string? GuestEmail { get; set; }

    /// <summary>Lifecycle status. New orders start <see cref="OrderStatus.Pending"/>.</summary>
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>Sum of line totals, in cents.</summary>
    public int SubtotalCents { get; set; }

    /// <summary>Tax in cents (flat 10% GST in MVP).</summary>
    public int TaxCents { get; set; }

    /// <summary>Shipping in cents (free / 0 in Phase 2).</summary>
    public int ShippingCents { get; set; }

    /// <summary>Grand total in cents = subtotal + tax + shipping.</summary>
    public int TotalCents { get; set; }

    /// <summary>Shipping address snapshot at placement (persisted as a JSON column).</summary>
    public OrderAddressSnapshot ShippingAddress { get; set; } = new();

    /// <summary>Billing address snapshot at placement (persisted as a JSON column).</summary>
    public OrderAddressSnapshot BillingAddress { get; set; } = new();

    /// <summary>When the order was placed (payment confirmed), UTC.</summary>
    public DateTimeOffset PlacedAt { get; set; }

    /// <summary>SQL Server <c>rowversion</c> — optimistic concurrency on status transitions.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    /// <summary>The order's line items.</summary>
    public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();

    /// <summary>The 1:1 price breakdown (subtotal/tax/shipping now; voucher/loyalty from Phase 7).</summary>
    public OrderPriceBreakdown? PriceBreakdown { get; set; }

    /// <summary>Payment events against this order (a charge, later possibly a refund).</summary>
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();

    /// <summary>The 1:0..1 fulfilment shipment — null until staff "Mark Shipped" (Phase 3).</summary>
    public Shipment? Shipment { get; set; }

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
