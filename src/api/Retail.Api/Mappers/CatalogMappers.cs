using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Mappers;

/// <summary>
/// Explicit entity → DTO mapping for the catalogue (no AutoMapper — CODING_STANDARDS).
/// Extension methods so call sites read as <c>product.ToDetailDto()</c>.
/// </summary>
public static class CatalogMappers
{
    // REQUIREMENTS §2.1: "Low Stock (< 10)".
    private const int LowStockThreshold = 10;

    public static CategoryDto ToDto(this Category category) =>
        new(category.Id, category.Slug, category.Name, category.ParentId);

    public static ProductVariantDto ToDto(this ProductVariant variant)
    {
        // Inventory may be null if not loaded; treat as 0 available rather than throw.
        int available = variant.Inventory?.Available ?? 0;
        return new ProductVariantDto(
            variant.Id,
            variant.Sku,
            variant.Options,
            variant.PriceCents,
            variant.CompareAtPriceCents,
            variant.IsActive,
            available,
            StockStatusFor(available));
    }

    public static ProductSummaryDto ToSummaryDto(this Product product)
    {
        // "From $X" = cheapest active variant; null when there are no active variants.
        int? fromPriceCents = product.Variants
            .Where(v => v.IsActive)
            .Select(v => (int?)v.PriceCents)
            .Min();

        return new ProductSummaryDto(
            product.Id,
            product.Sku,
            product.Slug,
            product.Name,
            product.BrandName,
            product.CategoryId,
            product.IsPublished,
            product.PrimaryImageBlobKey,
            fromPriceCents);
    }

    public static ProductImageDto ToImageDto(this ProductImage image) =>
        new(image.Id, image.BlobKey, image.AltText, image.SortOrder, image.IsPrimary, image.ProductVariantId);

    public static ProductDetailDto ToDetailDto(this Product product)
    {
        CategoryDto category = product.Category is not null
            ? product.Category.ToDto()
            : new CategoryDto(product.CategoryId, string.Empty, string.Empty, null);

        IReadOnlyList<ProductVariantDto> variants = product.Variants
            .OrderBy(v => v.Sku, StringComparer.Ordinal)
            .Select(v => v.ToDto())
            .ToList();

        // Gallery in display order (SortOrder, then Id as a stable tiebreaker).
        IReadOnlyList<ProductImageDto> images = product.Images
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .Select(i => i.ToImageDto())
            .ToList();

        return new ProductDetailDto(
            product.Id,
            product.Sku,
            product.Slug,
            product.Name,
            product.Description,
            product.SeoTitle,
            product.SeoDescription,
            product.BrandName,
            category,
            product.IsPublished,
            product.PrimaryImageBlobKey,
            variants,
            images);
    }

    private static string StockStatusFor(int available) =>
        available <= 0 ? "OutOfStock"
        : available < LowStockThreshold ? "LowStock"
        : "InStock";
}
