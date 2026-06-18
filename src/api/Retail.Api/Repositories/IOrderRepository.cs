using Retail.Api.Domain.Entities;

namespace Retail.Api.Repositories;

/// <summary>
/// Persistence for orders (Story 2.2). Pure data access — order assembly + the reservation
/// commit live in <c>OrderCreationService</c>.
/// </summary>
public interface IOrderRepository
{
    /// <summary>
    /// The order linked to a Stripe Checkout Session (via its Payment), or null if no order has
    /// been created for that session yet. The idempotency check for webhook redelivery.
    /// </summary>
    Task<Order?> GetByStripeSessionIdAsync(string stripeSessionId, CancellationToken ct);

    /// <summary>
    /// The order linked to a Stripe PaymentIntent (via any of its Payments), tracked WITH its
    /// lines — for the refund handler. Null if no order matches.
    /// </summary>
    Task<Order?> GetByPaymentIntentIdAsync(string paymentIntentId, CancellationToken ct);

    /// <summary>A page of a customer's orders (newest first), each with its lines, read-only.</summary>
    Task<(IReadOnlyList<Order> Items, int Total)> GetPagedByProfileAsync(Guid customerProfileId, int page, int pageSize, CancellationToken ct);

    /// <summary>An order by id scoped to its owner, with lines, read-only. Null if missing or not the caller's.</summary>
    Task<Order?> GetOwnedByIdAsync(Guid orderId, Guid customerProfileId, CancellationToken ct);

    /// <summary>
    /// Whether the customer has a completed purchase (an order in <c>Paid</c>/<c>Fulfilled</c>) with a
    /// line for any variant of the product — the "verified purchase" gate for reviews (Story 4.1).
    /// </summary>
    Task<bool> HasPurchasedProductAsync(Guid customerProfileId, Guid productId, CancellationToken ct);

    /// <summary>An order (with lines) by Stripe session id — the guest bearer lookup. Read-only. Null if none.</summary>
    Task<Order?> GetDetailByStripeSessionIdAsync(string stripeSessionId, CancellationToken ct);

    /// <summary>Admin workbench list (NOT owner-scoped): all orders, optional status/date/email filters, paged.</summary>
    Task<(IReadOnlyList<Order> Items, int Total)> GetPagedForAdminAsync(
        Common.Enums.OrderStatus? status, DateTimeOffset? from, DateTimeOffset? to, string? customerEmail,
        int page, int pageSize, CancellationToken ct);

    /// <summary>Admin order detail (NOT owner-scoped): lines + payments + shipment + the buyer's profile/user.</summary>
    Task<Order?> GetDetailForAdminAsync(Guid orderId, CancellationToken ct);

    /// <summary>Tracked order (with its shipment) for fulfilment writes (mark shipped / delivered).</summary>
    Task<Order?> GetTrackedWithShipmentAsync(Guid orderId, CancellationToken ct);

    /// <summary>Admin refund claim: atomically flip {Paid, Fulfilled} → Refunding (NOT owner-scoped). True if won.</summary>
    Task<bool> TryClaimForRefundByIdAsync(Guid orderId, DateTimeOffset now, string actor, CancellationToken ct);

    /// <summary>Revert a refund claim (Refunding → <paramref name="toStatus"/>) — rolls back a failed Stripe refund.</summary>
    Task ReleaseRefundClaimToAsync(
        Guid orderId, Common.Enums.OrderStatus toStatus, DateTimeOffset now, string actor, CancellationToken ct);

    /// <summary>The PaymentIntent id of an order's charge (positive payment), for issuing a refund. Null if none.</summary>
    Task<string?> GetChargePaymentIntentIdAsync(Guid orderId, CancellationToken ct);

    /// <summary>
    /// Atomically claims an owned <c>Paid</c> order for refund by flipping it to
    /// <c>Refunding</c>. Returns <c>true</c> only for the single caller whose set-based UPDATE
    /// matched the still-<c>Paid</c> row; a concurrent cancel sees a non-Paid status and gets
    /// <c>false</c> (0 rows). This is the TOCTOU guard that ensures exactly one writer reaches
    /// Stripe (cf. <c>InventoryReservationRepository.TryReserveAsync</c>).
    /// </summary>
    Task<bool> TryClaimForRefundAsync(Guid orderId, Guid customerProfileId, DateTimeOffset now, string actor, CancellationToken ct);

    /// <summary>
    /// Best-effort rollback of a refund claim (<c>Refunding</c> → <c>Paid</c>) when the Stripe
    /// refund call fails, so the order stays cancellable. Scoped to the still-<c>Refunding</c> row.
    /// </summary>
    Task ReleaseRefundClaimAsync(Guid orderId, DateTimeOffset now, string actor, CancellationToken ct);

    /// <summary>Stages a new order (with its lines, breakdown, and payment graph) for insert.</summary>
    void AddOrder(Order order);

    Task SaveChangesAsync(CancellationToken ct);
}
