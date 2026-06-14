namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Admin payload to update a product. SKU is intentionally NOT updatable (it's the
/// stable identifier); slug may be changed but stays unique among non-deleted products.
/// </summary>
public sealed record UpdateProductRequest(
    string Name,
    string? Slug,
    string? Description,
    string? SeoTitle,
    string? SeoDescription,
    string? BrandName,
    Guid CategoryId,
    bool IsPublished);
