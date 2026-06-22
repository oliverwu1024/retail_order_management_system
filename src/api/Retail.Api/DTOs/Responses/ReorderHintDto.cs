namespace Retail.Api.DTOs.Responses;

/// <summary>An active reorder recommendation shown in the admin Reorder list (Phase 5B §8).</summary>
public sealed record ReorderHintDto(
    Guid Id,
    Guid ProductVariantId,
    string Sku,
    string ProductName,
    int RecommendedOrderQty,
    string Reasoning,
    DateTimeOffset GeneratedAt);
