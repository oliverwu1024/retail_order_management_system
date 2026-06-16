namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Payload to add a variant to the cart (<c>POST /api/v1/cart/items</c>). Adding a variant
/// that is already in the cart bumps the existing line's quantity rather than duplicating it.
/// </summary>
public sealed record AddCartItemRequest(
    Guid ProductVariantId,
    int Quantity);
