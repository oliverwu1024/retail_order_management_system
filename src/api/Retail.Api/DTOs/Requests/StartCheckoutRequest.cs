namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Payload to begin checkout (<c>POST /api/v1/orders/checkout-session</c>). The cart is
/// identified by the caller (auth cookie or anon-cart cookie), not the body.
/// </summary>
/// <param name="ReturnBaseUrl">
/// The SPA's origin (e.g. <c>https://shop.example.com</c>), used to build the Stripe
/// success/cancel return URLs. Validated as an absolute http(s) URL.
/// </param>
public sealed record StartCheckoutRequest(string ReturnBaseUrl);
