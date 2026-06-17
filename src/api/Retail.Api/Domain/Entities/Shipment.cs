using Retail.Api.Common.Enums;
using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// The fulfilment record for an <see cref="Order"/> (DATABASE_DESIGN §3.14, deferred from
/// Phase 2 to Phase 3 — see docs/PHASE_3_SCOPE.md §3.3).
/// </summary>
/// <remarks>
/// <para>
/// CARDINALITY: an order has AT MOST ONE shipment (1:0..1) in the MVP — single-shipment
/// orders. The <c>UX_Shipment_OrderId</c> unique index enforces it; multi-shipment is a
/// future extension (drop the unique index) and is deliberately out of scope.
/// </para>
/// <para>
/// STATUS SPLIT: the order's coarse lifecycle stays on <see cref="OrderStatus"/> — "Mark
/// Shipped" flips the order to <see cref="OrderStatus.Fulfilled"/> once — while the finer
/// logistics progression (<see cref="ShipmentStatus.Pending"/> → Shipped → Delivered) lives
/// here. We never add Shipped/Delivered to <c>OrderStatus</c> (it is a stored, serialized
/// contract we don't renumber).
/// </para>
/// </remarks>
public class Shipment : IAuditableEntity
{
    /// <summary>Surrogate PK.</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the order this shipment fulfils. UNIQUE — one shipment per order (1:0..1).</summary>
    public Guid OrderId { get; set; }

    /// <summary>Navigation to the parent order.</summary>
    public Order Order { get; set; } = null!;

    /// <summary>Carrier name (e.g. "AusPost"). Null until shipped.</summary>
    public string? Carrier { get; set; }

    /// <summary>Carrier tracking number. Null until shipped.</summary>
    public string? TrackingNumber { get; set; }

    /// <summary>Logistics status. Starts <see cref="ShipmentStatus.Pending"/>.</summary>
    public ShipmentStatus Status { get; set; } = ShipmentStatus.Pending;

    /// <summary>When the parcel was dispatched (UTC). Set on "Mark Shipped".</summary>
    public DateTimeOffset? ShippedAt { get; set; }

    /// <summary>When delivery was confirmed (UTC). Set on "Mark Delivered".</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

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
