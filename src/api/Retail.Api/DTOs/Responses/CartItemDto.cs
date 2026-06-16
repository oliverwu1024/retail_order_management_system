namespace Retail.Api.DTOs.Responses;

/// <summary>
/// A single cart line, enriched with the display fields the storefront needs (name, slug,
/// image, options) so the cart page renders without N extra catalogue calls.
/// </summary>
/// <remarks>
/// <paramref name="UnitPriceCents"/> is the price <em>snapshotted when the line was added</em>
/// (what the cart currently charges), not necessarily the live catalogue price — checkout
/// re-validates against the live price in a later chunk. <paramref name="InStock"/> is a
/// cheap "is there enough on hand for this quantity" hint for the UI, not a hard reservation.
/// </remarks>
public sealed record CartItemDto(
    Guid ProductVariantId,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    string Sku,
    IReadOnlyDictionary<string, string> Options,
    int UnitPriceCents,
    int Quantity,
    int LineTotalCents,
    string? PrimaryImageBlobKey,
    bool InStock);
