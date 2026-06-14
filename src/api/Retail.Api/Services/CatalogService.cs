using Microsoft.Extensions.Options;
using Retail.Api.Common.Helpers;
using Retail.Api.Common.Models;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Exceptions;
using Retail.Api.Mappers;
using Retail.Api.Repositories;
using Retail.Api.Storage;

namespace Retail.Api.Services;

/// <summary>Default <see cref="ICatalogService"/>. Orchestrates the product + category repositories.</summary>
public sealed class CatalogService : ICatalogService
{
    private const int MaxPageSize = 100;
    private const int MaxCategoryDepth = 3;

    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;
    private readonly IBlobStorageClient _blob;
    private readonly BlobStorageOptions _storage;
    private readonly ILogger<CatalogService> _logger;

    public CatalogService(
        IProductRepository products,
        ICategoryRepository categories,
        IBlobStorageClient blob,
        IOptions<BlobStorageOptions> storage,
        ILogger<CatalogService> logger)
    {
        _products = products;
        _categories = categories;
        _blob = blob;
        _storage = storage.Value;
        _logger = logger;
    }

    // ── Public reads ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<PagedResult<ProductSummaryDto>> ListProductsAsync(ProductListQuery query, CancellationToken ct)
    {
        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        (IReadOnlyList<Product> items, int total) =
            await _products.ListPublishedAsync(query.CategoryId, query.Search, page, pageSize, ct);

        List<ProductSummaryDto> dtos = items.Select(p => p.ToSummaryDto()).ToList();
        return new PagedResult<ProductSummaryDto>(dtos, total, page, pageSize);
    }

    /// <inheritdoc />
    public async Task<ProductDetailDto> GetProductBySlugAsync(string slug, CancellationToken ct)
    {
        Product product = await _products.GetPublishedDetailBySlugAsync(slug, ct)
            ?? throw new NotFoundException($"Product '{slug}' was not found.");
        return product.ToDetailDto();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct)
    {
        IReadOnlyList<Category> categories = await _categories.ListAsync(ct);
        return categories.Select(c => c.ToDto()).ToList();
    }

    /// <inheritdoc />
    public async Task<PagedResult<ProductSummaryDto>> ListProductsForAdminAsync(ProductListQuery query, CancellationToken ct)
    {
        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        (IReadOnlyList<Product> items, int total) =
            await _products.ListForAdminAsync(query.CategoryId, query.Search, page, pageSize, ct);

        List<ProductSummaryDto> dtos = items.Select(p => p.ToSummaryDto()).ToList();
        return new PagedResult<ProductSummaryDto>(dtos, total, page, pageSize);
    }

    /// <inheritdoc />
    public async Task<ProductDetailDto> GetProductForAdminAsync(Guid id, CancellationToken ct)
    {
        Product product = await _products.GetDetailByIdAsync(id, ct)
            ?? throw new NotFoundException($"Product '{id}' was not found.");
        return product.ToDetailDto();
    }

    // ── Admin writes ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        string slug = ResolveSlug(request.Slug, request.Name);
        if (await _categories.SlugExistsAsync(slug, ct))
        {
            throw new ConflictException($"A category with slug '{slug}' already exists.");
        }

        if (request.ParentId is Guid parentId)
        {
            await EnsureParentDepthAsync(parentId, ct);
        }

        var category = new Category
        {
            Slug = slug,
            Name = request.Name,
            ParentId = request.ParentId,
        };

        await _categories.AddAsync(category, ct);
        await _categories.SaveChangesAsync(ct);

        _logger.LogInformation("Created category {CategoryId} (slug {Slug})", category.Id, category.Slug);
        return category.ToDto();
    }

    /// <inheritdoc />
    public async Task<ProductDetailDto> CreateProductAsync(CreateProductRequest request, CancellationToken ct)
    {
        Category category = await _categories.GetByIdAsync(request.CategoryId, ct)
            ?? throw new NotFoundException($"Category '{request.CategoryId}' was not found.");

        string slug = ResolveSlug(request.Slug, request.Name);
        await EnsureProductSkuAndSlugFreeAsync(request.Sku, slug, ct);

        var product = new Product
        {
            Sku = request.Sku,
            Slug = slug,
            Name = request.Name,
            Description = request.Description,
            SeoTitle = request.SeoTitle,
            SeoDescription = request.SeoDescription,
            BrandName = request.BrandName,
            CategoryId = category.Id,
            Category = category,
            IsPublished = request.IsPublished,
        };

        await _products.AddAsync(product, ct);
        await _products.SaveChangesAsync(ct);

        _logger.LogInformation("Created product {ProductId} (sku {Sku})", product.Id, product.Sku);
        return product.ToDetailDto();
    }

    /// <inheritdoc />
    public async Task<ProductDetailDto> UpdateProductAsync(Guid id, UpdateProductRequest request, CancellationToken ct)
    {
        Product product = await _products.GetByIdForWriteAsync(id, ct)
            ?? throw new NotFoundException($"Product '{id}' was not found.");

        Category category = await _categories.GetByIdAsync(request.CategoryId, ct)
            ?? throw new NotFoundException($"Category '{request.CategoryId}' was not found.");

        string slug = ResolveSlug(request.Slug, request.Name);
        if (!string.Equals(slug, product.Slug, StringComparison.Ordinal) && await _products.SlugExistsAsync(slug, ct))
        {
            throw new ConflictException($"A product with slug '{slug}' already exists.");
        }

        product.Name = request.Name;
        product.Slug = slug;
        product.Description = request.Description;
        product.SeoTitle = request.SeoTitle;
        product.SeoDescription = request.SeoDescription;
        product.BrandName = request.BrandName;
        product.CategoryId = category.Id;
        product.Category = category; // keep the nav in sync so the returned DTO reflects the new category
        product.IsPublished = request.IsPublished;

        await _products.SaveChangesAsync(ct);

        _logger.LogInformation("Updated product {ProductId}", product.Id);
        return product.ToDetailDto();
    }

    /// <inheritdoc />
    public async Task SoftDeleteProductAsync(Guid id, CancellationToken ct)
    {
        Product product = await _products.GetByIdForWriteAsync(id, ct)
            ?? throw new NotFoundException($"Product '{id}' was not found.");

        product.IsDeleted = true;
        await _products.SaveChangesAsync(ct);

        _logger.LogInformation("Soft-deleted product {ProductId}", product.Id);
    }

    /// <inheritdoc />
    public async Task<ProductDetailDto> SetProductPrimaryImageAsync(Guid id, Stream content, string contentType, CancellationToken ct)
    {
        Product product = await _products.GetByIdForWriteAsync(id, ct)
            ?? throw new NotFoundException($"Product '{id}' was not found.");

        // Unique key per upload (no overwrite); old blobs are left for a later cleanup job.
        string extension = ProductImage.ExtensionFor(contentType);
        string blobKey = $"products/{product.Id:N}/{Guid.NewGuid():N}.{extension}";
        await _blob.UploadAsync(_storage.ProductImagesContainer, blobKey, content, contentType, ct);

        product.PrimaryImageBlobKey = blobKey;
        await _products.SaveChangesAsync(ct);

        _logger.LogInformation("Set primary image for product {ProductId} -> {BlobKey}", product.Id, blobKey);
        return product.ToDetailDto();
    }

    /// <inheritdoc />
    public async Task<ProductVariantDto> AddVariantAsync(Guid productId, CreateVariantRequest request, CancellationToken ct)
    {
        Product product = await _products.GetByIdForWriteAsync(productId, ct)
            ?? throw new NotFoundException($"Product '{productId}' was not found.");

        if (await _products.VariantSkuExistsAsync(request.Sku, ct))
        {
            throw new ConflictException($"A variant with SKU '{request.Sku}' already exists.");
        }

        var variant = new ProductVariant
        {
            Sku = request.Sku,
            Options = request.Options ?? new Dictionary<string, string>(),
            PriceCents = request.PriceCents,
            CompareAtPriceCents = request.CompareAtPriceCents,
            IsActive = true,
            // 1:1 stock created with the variant, seeded with the initial on-hand count.
            Inventory = new InventoryItem { OnHand = request.InitialStock, Reserved = 0 },
        };

        product.Variants.Add(variant);
        await _products.SaveChangesAsync(ct);

        _logger.LogInformation("Added variant {VariantId} to product {ProductId}", variant.Id, product.Id);
        return variant.ToDto();
    }

    /// <inheritdoc />
    public async Task<ProductVariantDto> UpdateVariantAsync(Guid productId, Guid variantId, UpdateVariantRequest request, CancellationToken ct)
    {
        ProductVariant variant = await GetVariantForWriteAsync(productId, variantId, ct);

        if (request.Options is not null)
        {
            variant.Options = request.Options;
        }

        variant.PriceCents = request.PriceCents;
        variant.CompareAtPriceCents = request.CompareAtPriceCents;
        variant.IsActive = request.IsActive;

        await _products.SaveChangesAsync(ct);
        return variant.ToDto();
    }

    /// <inheritdoc />
    public async Task DeleteVariantAsync(Guid productId, Guid variantId, CancellationToken ct)
    {
        ProductVariant variant = await GetVariantForWriteAsync(productId, variantId, ct);

        // Hard delete (its 1:1 inventory cascades). Safe in Phase 1 — no orders reference
        // variants yet; Phase 2 should switch to deactivation (IsActive=false) once they do.
        Product product = variant.Product!;
        product.Variants.Remove(variant);
        await _products.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted variant {VariantId} from product {ProductId}", variantId, productId);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private async Task<ProductVariant> GetVariantForWriteAsync(Guid productId, Guid variantId, CancellationToken ct)
    {
        Product product = await _products.GetByIdForWriteAsync(productId, ct)
            ?? throw new NotFoundException($"Product '{productId}' was not found.");

        ProductVariant variant = product.Variants.FirstOrDefault(v => v.Id == variantId)
            ?? throw new NotFoundException($"Variant '{variantId}' was not found on product '{productId}'.");

        // Ensure the back-reference is set for callers that need the parent product.
        variant.Product = product;
        return variant;
    }

    private async Task EnsureProductSkuAndSlugFreeAsync(string sku, string slug, CancellationToken ct)
    {
        if (await _products.SkuExistsAsync(sku, ct))
        {
            throw new ConflictException($"A product with SKU '{sku}' already exists.");
        }

        if (await _products.SlugExistsAsync(slug, ct))
        {
            throw new ConflictException($"A product with slug '{slug}' already exists.");
        }
    }

    // Walks the parent chain; rejects if attaching here would exceed MaxCategoryDepth levels.
    private async Task EnsureParentDepthAsync(Guid parentId, CancellationToken ct)
    {
        int ancestorCount = 0;
        Guid? cursor = parentId;

        while (cursor is Guid current)
        {
            Category node = await _categories.GetByIdAsync(current, ct)
                ?? throw new NotFoundException($"Parent category '{parentId}' was not found.");

            ancestorCount++;
            if (ancestorCount >= MaxCategoryDepth)
            {
                throw new ConflictException($"Category nesting cannot exceed {MaxCategoryDepth} levels.");
            }

            cursor = node.ParentId;
        }
    }

    private static string ResolveSlug(string? requested, string fallbackSource)
    {
        string slug = Slug.From(string.IsNullOrWhiteSpace(requested) ? fallbackSource : requested);
        if (string.IsNullOrEmpty(slug))
        {
            throw new ConflictException("Could not derive a URL slug from the name; please supply one explicitly.");
        }

        return slug;
    }
}
