namespace Retail.Api.DTOs.Responses;

/// <summary>
/// A product variant as returned to clients. <see cref="StockStatus"/> is the
/// storefront indicator ("InStock" / "LowStock" / "OutOfStock", REQUIREMENTS §2.1);
/// <see cref="Available"/> is the raw sellable count (OnHand − Reserved).
/// </summary>
public sealed record ProductVariantDto(
    Guid Id,
    string Sku,
    IReadOnlyDictionary<string, string> Options,
    int PriceCents,
    int? CompareAtPriceCents,
    bool IsActive,
    int Available,
    string StockStatus);
