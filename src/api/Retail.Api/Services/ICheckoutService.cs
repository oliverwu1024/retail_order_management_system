using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>
/// Orchestrates checkout (Story 2.2). Starting checkout reserves the cart's stock and creates a
/// Stripe hosted Checkout Session; completing it (driven by the webhook, added in 3c) turns the
/// holds into a real order.
/// </summary>
public interface ICheckoutService
{
    /// <summary>
    /// Reserves the caller's cart and creates a Stripe Checkout Session, returning the URL to
    /// redirect to. Throws <c>ConflictException</c> (cart empty) or <c>OutOfStock</c>/
    /// <c>ConcurrencyException</c> (→409) if the stock can't be held.
    /// </summary>
    Task<CheckoutSessionResponse> StartCheckoutAsync(CartCaller caller, StartCheckoutRequest request, CancellationToken ct);
}
