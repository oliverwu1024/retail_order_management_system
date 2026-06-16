namespace Retail.Api.DTOs.Responses;

/// <summary>
/// The result of starting checkout: the Stripe-hosted Checkout URL the SPA redirects to.
/// </summary>
public sealed record CheckoutSessionResponse(string Url);
