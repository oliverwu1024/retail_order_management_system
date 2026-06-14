namespace Retail.Api.DTOs.Responses;

/// <summary>Full product shape for the detail page (<c>GET /products/{slug}</c>) and admin write responses.</summary>
public sealed record ProductDetailDto(
    Guid Id,
    string Sku,
    string Slug,
    string Name,
    string? Description,
    string? SeoTitle,
    string? SeoDescription,
    string? BrandName,
    CategoryDto Category,
    bool IsPublished,
    string? PrimaryImageBlobKey,
    IReadOnlyList<ProductVariantDto> Variants);
