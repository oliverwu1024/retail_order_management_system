using Retail.Api.Domain.Entities;

namespace Retail.Api.Services;

/// <summary>
/// Turns a paid Stripe checkout into a real order (Story 2.2). Called by the webhook on
/// <c>checkout.session.completed</c>.
/// </summary>
public interface IOrderCreationService
{
    /// <summary>
    /// Finalises a paid checkout, atomically: commits the cart's reservations (stock leaves the
    /// warehouse), creates the order + lines + price breakdown + payment, and converts the cart.
    /// Idempotent — if an order already exists for the session, it's returned unchanged.
    /// </summary>
    Task<Order> CreateOrderFromCheckoutAsync(CheckoutCompletion completion, CancellationToken ct);
}

/// <summary>
/// The provider-agnostic facts the webhook extracts from a completed Stripe Checkout Session.
/// Identity is a member (<paramref name="CustomerProfileId"/>) XOR a guest
/// (<paramref name="CustomerEmail"/>).
/// </summary>
public sealed record CheckoutCompletion(
    string StripeSessionId,
    string? PaymentIntentId,
    Guid CartId,
    Guid? CustomerProfileId,
    string? CustomerEmail,
    OrderAddressSnapshot ShippingAddress,
    OrderAddressSnapshot BillingAddress);
