using Retail.Api.Common.Enums;
using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// A soft hold on stock (DATABASE_DESIGN §3.10), placed when checkout starts and resolved
/// when payment succeeds (committed) or the cart is abandoned/edited/expires (released).
/// </summary>
/// <remarks>
/// <para>
/// While <see cref="ReservationStatus.Active"/>, the hold inflates
/// <see cref="InventoryItem.Reserved"/> so two shoppers can't both buy the last unit. A
/// hold is tied to EITHER a cart (pre-order) via <see cref="CartId"/> OR an order
/// (post-order) via <see cref="OrderId"/> — exactly one is set at a time.
/// </para>
/// <para>
/// 15-minute TTL (<see cref="ExpiresAt"/>): the <c>CartExpirySweeper</c> releases holds
/// past their expiry so abandoned checkouts return stock to the pool. NOTE this is a
/// shorter, distinct TTL from the cart's own 30-minute expiry.
/// </para>
/// </remarks>
public class InventoryReservation : IAuditableEntity
{
    /// <summary>Surrogate PK.</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the stock row this hold draws from.</summary>
    public Guid InventoryItemId { get; set; }

    /// <summary>Navigation to the stock row.</summary>
    public InventoryItem? InventoryItem { get; set; }

    /// <summary>Set while the hold belongs to a cart (pre-payment); <c>null</c> once tied to an order.</summary>
    public Guid? CartId { get; set; }

    /// <summary>Set once the hold is attached to a placed order; <c>null</c> while still cart-bound.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Units held (must be &gt; 0).</summary>
    public int Quantity { get; set; }

    /// <summary>When the hold lapses (15 min). Past this the sweeper releases it.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Lifecycle status. New holds start <see cref="ReservationStatus.Active"/>.</summary>
    public ReservationStatus Status { get; set; } = ReservationStatus.Active;

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
