namespace Retail.Api.DTOs.Responses;

/// <summary>
/// One image in a product's gallery (PRODUCT_IMAGES_SCOPE). <see cref="ProductVariantId"/> null =
/// a general image shown for the whole product; set = specific to that variant.
/// </summary>
public sealed record ProductImageDto(
    Guid Id,
    string BlobKey,
    string? AltText,
    int SortOrder,
    bool IsPrimary,
    Guid? ProductVariantId);
