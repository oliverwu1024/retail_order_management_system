namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Payload to set the absolute quantity of a cart line (<c>PUT /api/v1/cart/items/{variantId}</c>).
/// To remove a line, call <c>DELETE</c> instead — quantity here must be ≥ 1.
/// </summary>
public sealed record UpdateCartItemRequest(
    int Quantity);
