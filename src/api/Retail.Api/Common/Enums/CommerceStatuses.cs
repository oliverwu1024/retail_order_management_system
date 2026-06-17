namespace Retail.Api.Common.Enums;

// ─────────────────────────────────────────────────────────────────────────────
//  Commerce lifecycle statuses (Phase 2 — Cart & Orders).
//
//  These are the FIRST persisted enums in the project, so this file also
//  establishes the convention every future status enum follows:
//
//  WHY `: byte` (not the default `int`)?
//  -------------------------------------
//  A status has a handful of values that will never exceed 255. Backing the
//  enum with `byte` makes EF Core map it to a SQL Server `tinyint` (1 byte)
//  instead of `int` (4 bytes) — smaller rows, smaller indexes on the hot
//  Order/Cart status columns. DATABASE_DESIGN specifies every status column as
//  `tinyint`, so the C# type and the SQL type line up with no surprise.
//
//  WHY EXPLICIT, 1-BASED VALUES?
//  -----------------------------
//  * Explicit numbers are part of the contract — they are stored in the DB and
//    leak into API responses, so reordering or inserting a member must NEVER
//    renumber the others. Pinning the value to each name makes that safe.
//  * Starting at 1 (not 0) means the C# default(enum) == 0 is an UNUSED, clearly
//    invalid sentinel. A row that somehow has status 0 is detectably wrong,
//    rather than silently masquerading as the first real state. EF configs set
//    `HasDefaultValue(...=1)` so new rows get a real starting status at the DB.
//  * The numbers match DATABASE_DESIGN §3.8–§3.14 exactly.
//
//  The EF mapping (HasColumnType("tinyint") + HasDefaultValue) lives in each
//  entity's configuration, not here — this file is just the vocabulary.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Lifecycle of a shopping <c>Cart</c>. A cart starts <see cref="Open"/>, and ends
/// either <see cref="Converted"/> (checkout succeeded) or <see cref="Abandoned"/>
/// (expired and swept). Carts are never deleted — the status is the tombstone.
/// </summary>
public enum CartStatus : byte
{
    /// <summary>Active cart the shopper is still filling.</summary>
    Open = 1,

    /// <summary>Expired past its TTL and released by the sweeper; no longer usable.</summary>
    Abandoned = 2,

    /// <summary>Checkout completed — this cart became an order.</summary>
    Converted = 3,
}

/// <summary>
/// Lifecycle of an <c>InventoryReservation</c> — the soft hold placed on stock
/// between "started checkout" and "paid". Active holds inflate
/// <c>InventoryItem.Reserved</c>; committing or releasing them unwinds that.
/// </summary>
public enum ReservationStatus : byte
{
    /// <summary>Stock is held (counts against Reserved) and the hold has not yet expired.</summary>
    Active = 1,

    /// <summary>Payment succeeded — the hold was converted into a real stock decrement.</summary>
    Committed = 2,

    /// <summary>Hold cancelled (cart edited/abandoned/expired) — Reserved was given back.</summary>
    Released = 3,
}

/// <summary>
/// Lifecycle of an <c>Order</c>. Phase 2 drives Pending → Paid (webhook) and the
/// customer-cancel path Pending → Cancelled / Paid → <see cref="Refunding"/> → Refunded.
/// Fulfilled is set by the Phase 3 staff "Mark Shipped" flow.
/// </summary>
public enum OrderStatus : byte
{
    /// <summary>Created, awaiting payment confirmation from Stripe.</summary>
    Pending = 1,

    /// <summary>Payment confirmed via <c>checkout.session.completed</c>.</summary>
    Paid = 2,

    /// <summary>Shipped/fulfilled (Phase 3).</summary>
    Fulfilled = 3,

    /// <summary>Cancelled before payment (no charge to reverse).</summary>
    Cancelled = 4,

    /// <summary>Paid then refunded — inventory rolled back.</summary>
    Refunded = 5,

    /// <summary>
    /// Transient "refund in progress" claim. The customer-cancel flow atomically flips
    /// Paid → Refunding BEFORE calling Stripe, so exactly one writer reaches the refund API
    /// (a concurrent cancel loses the claim and gets a 409). On a successful Stripe refund the
    /// state advances to <see cref="Refunded"/>; if the Stripe call fails it is rolled back to
    /// <see cref="Paid"/> so the order stays cancellable. Appended as 6 — never renumber.
    /// </summary>
    Refunding = 6,
}

/// <summary>
/// Lifecycle of a <c>Payment</c> row. An order can have several payments over time
/// (e.g. a charge then a refund), so this is per-payment, not per-order.
/// </summary>
public enum PaymentStatus : byte
{
    /// <summary>Stripe Checkout Session created; outcome not yet known.</summary>
    Created = 1,

    /// <summary>Charge succeeded.</summary>
    Succeeded = 2,

    /// <summary>Charge failed/declined.</summary>
    Failed = 3,

    /// <summary>This payment row represents a refund (negative amount).</summary>
    Refunded = 4,
}
