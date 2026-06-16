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

    /// <summary>Stages a new order (with its lines, breakdown, and payment graph) for insert.</summary>
    void AddOrder(Order order);

    Task SaveChangesAsync(CancellationToken ct);
}
