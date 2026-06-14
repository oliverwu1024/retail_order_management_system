namespace Retail.Api.DTOs.Responses;

/// <summary>
/// Compact product shape for the catalogue grid. <see cref="FromPriceCents"/> is the
/// lowest active-variant price ("from $X"), or null if the product has no active variant.
/// </summary>
public sealed record ProductSummaryDto(
    Guid Id,
    string Sku,
    string Slug,
    string Name,
    string? BrandName,
    Guid CategoryId,
    bool IsPublished,
    string? PrimaryImageBlobKey,
    int? FromPriceCents);
