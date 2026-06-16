using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Mappers;

/// <summary>
/// Explicit Cart/CartItem → DTO mapping (no AutoMapper — CODING_STANDARDS). Assumes the
/// cart was loaded with its items, each item's variant, that variant's product, and the
/// inventory row (the repository's <c>Include</c> chain guarantees this); navigation fields
/// are null-guarded so a mapping never throws on a partially-loaded graph.
/// </summary>
public static class CartMappers
{
    public static CartItemDto ToDto(this CartItem item)
    {
        ProductVariant? variant = item.ProductVariant;
        Product? product = variant?.Product;

        // Line total uses the SNAPSHOT price (what the cart locked in at add-time), so the
        // displayed total can't silently shift if the catalogue is repriced mid-session.
        int lineTotalCents = item.UnitPriceCentsSnapshot * item.Quantity;

        // "Enough on hand for this line?" — Available = OnHand − Reserved. A cheap UI hint;
        // the binding stock check happens at checkout (reservation), not here.
        int available = variant?.Inventory?.Available ?? 0;

        return new CartItemDto(
            item.ProductVariantId,
            product?.Id ?? Guid.Empty,
            product?.Name ?? string.Empty,
            product?.Slug ?? string.Empty,
            variant?.Sku ?? string.Empty,
            variant?.Options ?? new Dictionary<string, string>(),
            item.UnitPriceCentsSnapshot,
            item.Quantity,
            lineTotalCents,
            product?.PrimaryImageBlobKey,
            available >= item.Quantity);
    }

    public static CartDto ToDto(this Cart cart)
    {
        // Stable display order: oldest line first (the order they were added).
        List<CartItemDto> items = cart.Items
            .OrderBy(i => i.CreatedAt)
            .Select(i => i.ToDto())
            .ToList();

        return new CartDto(
            cart.Id,
            items,
            items.Sum(i => i.LineTotalCents),
            items.Sum(i => i.Quantity),
            cart.ExpiresAt);
    }
}
