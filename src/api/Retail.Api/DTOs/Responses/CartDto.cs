namespace Retail.Api.DTOs.Responses;

/// <summary>
/// The current cart as returned to the storefront (<c>GET /api/v1/cart</c> and every
/// mutation). <paramref name="TotalQuantity"/> backs the header badge; the anonymous cart
/// key is NOT included here — it travels in an HttpOnly cookie, never the response body.
/// </summary>
public sealed record CartDto(
    Guid Id,
    IReadOnlyList<CartItemDto> Items,
    int SubtotalCents,
    int TotalQuantity,
    DateTimeOffset ExpiresAt);
