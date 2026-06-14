namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Admin payload to add a variant to a product. Creates the variant AND its 1:1
/// inventory row seeded with <paramref name="InitialStock"/> on hand.
/// </summary>
public sealed record CreateVariantRequest(
    string Sku,
    Dictionary<string, string>? Options,
    int PriceCents,
    int? CompareAtPriceCents,
    int InitialStock);
