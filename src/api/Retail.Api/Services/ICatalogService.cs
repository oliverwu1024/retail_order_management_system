using Retail.Api.Common.Models;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>
/// Catalogue business logic: storefront reads (paged/filtered/searched) and admin
/// writes (product + variant CRUD, category create). Throws <c>NotFoundException</c> /
/// <c>ConflictException</c> for expected failures — mapped to 404/409 by the middleware.
/// </summary>
public interface ICatalogService
{
    // ── Public reads ──────────────────────────────────────────────────────────
    Task<PagedResult<ProductSummaryDto>> ListProductsAsync(ProductListQuery query, CancellationToken ct);
    Task<ProductDetailDto> GetProductBySlugAsync(string slug, CancellationToken ct);
    Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct);

    // ── Admin reads (all non-deleted, INCLUDING unpublished) ──────────────────
    Task<PagedResult<ProductSummaryDto>> ListProductsForAdminAsync(ProductListQuery query, CancellationToken ct);
    Task<ProductDetailDto> GetProductForAdminAsync(Guid id, CancellationToken ct);

    // ── Admin writes ──────────────────────────────────────────────────────────
    Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct);
    Task<ProductDetailDto> CreateProductAsync(CreateProductRequest request, CancellationToken ct);
    Task<ProductDetailDto> UpdateProductAsync(Guid id, UpdateProductRequest request, CancellationToken ct);
    Task SoftDeleteProductAsync(Guid id, CancellationToken ct);

    // ── Product image gallery (PRODUCT_IMAGES_SCOPE) ──────────────────────────
    /// <summary>Adds an image to the gallery (becomes primary if the product has none yet). Optional variant scoping + alt text. Returns the updated product detail.</summary>
    Task<ProductDetailDto> AddProductImageAsync(Guid productId, Stream content, string contentType, Guid? variantId, string? altText, CancellationToken ct);
    /// <summary>Deletes a gallery image (promotes the next image to primary if the deleted one was primary).</summary>
    Task<ProductDetailDto> DeleteProductImageAsync(Guid productId, Guid imageId, CancellationToken ct);
    /// <summary>Reassigns SortOrder to the product's images from the supplied full ordering.</summary>
    Task<ProductDetailDto> ReorderProductImagesAsync(Guid productId, IReadOnlyList<Guid> imageIds, CancellationToken ct);
    /// <summary>Edits a gallery image (alt text, variant association, promote-to-primary).</summary>
    Task<ProductDetailDto> UpdateProductImageAsync(Guid productId, Guid imageId, UpdateProductImageRequest request, CancellationToken ct);

    Task<ProductVariantDto> AddVariantAsync(Guid productId, CreateVariantRequest request, CancellationToken ct);
    Task<ProductVariantDto> UpdateVariantAsync(Guid productId, Guid variantId, UpdateVariantRequest request, CancellationToken ct);
    Task DeleteVariantAsync(Guid productId, Guid variantId, CancellationToken ct);
}
