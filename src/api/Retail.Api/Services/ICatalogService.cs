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

    // ── Admin writes ──────────────────────────────────────────────────────────
    Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct);
    Task<ProductDetailDto> CreateProductAsync(CreateProductRequest request, CancellationToken ct);
    Task<ProductDetailDto> UpdateProductAsync(Guid id, UpdateProductRequest request, CancellationToken ct);
    Task SoftDeleteProductAsync(Guid id, CancellationToken ct);
    Task<ProductVariantDto> AddVariantAsync(Guid productId, CreateVariantRequest request, CancellationToken ct);
    Task<ProductVariantDto> UpdateVariantAsync(Guid productId, Guid variantId, UpdateVariantRequest request, CancellationToken ct);
    Task DeleteVariantAsync(Guid productId, Guid variantId, CancellationToken ct);
}
