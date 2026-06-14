namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Admin payload to update a variant. Stock (OnHand/Reserved) is NOT changed here —
/// inventory has its own controlled adjustment paths (Phase 2/3).
/// </summary>
public sealed record UpdateVariantRequest(
    Dictionary<string, string>? Options,
    int PriceCents,
    int? CompareAtPriceCents,
    bool IsActive);
