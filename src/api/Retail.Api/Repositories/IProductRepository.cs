using Retail.Api.Domain.Entities;

namespace Retail.Api.Repositories;

/// <summary>Data access for <see cref="Product"/> + its variants. Pure persistence — no business rules.</summary>
public interface IProductRepository
{
    /// <summary>Read-only page of PUBLISHED, non-deleted products (variants included for "from" pricing).</summary>
    Task<(IReadOnlyList<Product> Items, int TotalCount)> ListPublishedAsync(
        Guid? categoryId, string? search, int page, int pageSize, CancellationToken ct);

    /// <summary>Read-only published product by slug, with category + variants + stock. Null if missing/unpublished.</summary>
    Task<Product?> GetPublishedDetailBySlugAsync(string slug, CancellationToken ct);

    /// <summary>TRACKED product by id (any publish state, non-deleted) with category + variants + stock — for admin writes.</summary>
    Task<Product?> GetByIdForWriteAsync(Guid id, CancellationToken ct);

    /// <summary>Whether a (non-deleted) product already uses this SKU.</summary>
    Task<bool> SkuExistsAsync(string sku, CancellationToken ct);

    /// <summary>Whether a (non-deleted) product already uses this slug.</summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);

    /// <summary>Whether any variant already uses this (globally-unique) SKU.</summary>
    Task<bool> VariantSkuExistsAsync(string sku, CancellationToken ct);

    /// <summary>Stages a new product for insert.</summary>
    Task AddAsync(Product product, CancellationToken ct);

    /// <summary>Persists tracked changes.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}
