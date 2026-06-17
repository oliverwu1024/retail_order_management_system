using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Retail.Api.Common.Helpers;
using Retail.Api.Common.Models;
using Retail.Api.Data;
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
    // Same scoped instance the repositories use — only for the image-primary swap transaction
    // (clear-then-set, so the filtered-unique primary index is never transiently violated).
    private readonly RetailDbContext _db;
    private readonly ILogger<CatalogService> _logger;

    public CatalogService(
        IProductRepository products,
        ICategoryRepository categories,
        IBlobStorageClient blob,
        IOptions<BlobStorageOptions> storage,
        RetailDbContext db,
        ILogger<CatalogService> logger)
    {
        _products = products;
        _categories = categories;
        _blob = blob;
        _storage = storage.Value;
        _db = db;
        _logger = logger;
    }

    // ── Public reads ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<PagedResult<ProductSummaryDto>> ListProductsAsync(ProductListQuery query, CancellationToken ct)
    {
        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        (IReadOnlyList<Product> items, int total) =
            await _products.ListPublishedAsync(query.CategoryId, NormalizeSearch(query.Search), page, pageSize, ct);

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
            await _products.ListForAdminAsync(query.CategoryId, NormalizeSearch(query.Search), page, pageSize, ct);

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
    public async Task<ProductDetailDto> AddProductImageAsync(
        Guid productId, Stream content, string contentType, Guid? variantId, string? altText, CancellationToken ct)
    {
        Product product = await _products.GetByIdForWriteAsync(productId, ct)
            ?? throw new NotFoundException($"Product '{productId}' was not found.");

        EnsureVariantOnProduct(product, variantId);

        // Unique key per upload (no overwrite). Upload before the row so a failed upload never
        // leaves a dangling DB pointer.
        string extension = ImageFormat.ExtensionFor(contentType);
        string blobKey = $"products/{product.Id:N}/{Guid.NewGuid():N}.{extension}";
        await _blob.UploadAsync(_storage.ProductImagesContainer, blobKey, content, contentType, ct);

        // First image (or a gallery with no current primary) becomes the hero — no swap, so a
        // single SaveChanges can't violate the one-primary index.
        bool isPrimary = product.Images.All(i => !i.IsPrimary);
        int nextSort = product.Images.Count == 0 ? 0 : product.Images.Max(i => i.SortOrder) + 1;

        var image = new ProductImage
        {
            ProductId = product.Id,
            ProductVariantId = variantId,
            BlobKey = blobKey,
            AltText = altText,
            SortOrder = nextSort,
            IsPrimary = isPrimary,
        };
        product.Images.Add(image);
        if (isPrimary)
        {
            product.PrimaryImageBlobKey = blobKey;
        }

        try
        {
            await SavePrimaryChangeAsync(ct);
        }
        catch
        {
            // The blob was uploaded but the row never persisted (e.g. the primary-index race → 409,
            // or any save failure) — best-effort delete the just-uploaded blob so it isn't orphaned,
            // then re-throw the original failure (mirrors DeleteProductImageAsync's cleanup).
            try
            {
                await _blob.DeleteAsync(_storage.ProductImagesContainer, blobKey, ct);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(
                    cleanupEx, "Failed to clean up orphan image blob {BlobKey} for product {ProductId} after a failed insert", blobKey, product.Id);
            }

            throw;
        }

        _logger.LogInformation("Added image {ImageId} to product {ProductId} (primary={Primary})", image.Id, product.Id, isPrimary);
        return product.ToDetailDto();
    }

    /// <inheritdoc />
    public async Task<ProductDetailDto> DeleteProductImageAsync(Guid productId, Guid imageId, CancellationToken ct)
    {
        Product product = await _products.GetByIdForWriteAsync(productId, ct)
            ?? throw new NotFoundException($"Product '{productId}' was not found.");

        ProductImage image = product.Images.FirstOrDefault(i => i.Id == imageId)
            ?? throw new NotFoundException($"Image '{imageId}' was not found on product '{productId}'.");

        string blobKey = image.BlobKey;
        bool wasPrimary = image.IsPrimary;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Delete the row FIRST (so the old primary is gone) before promoting a replacement —
        // otherwise two IsPrimary rows would transiently violate UX_ProductImage_Primary.
        product.Images.Remove(image);
        await _products.SaveChangesAsync(ct);

        if (wasPrimary)
        {
            ProductImage? next = product.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).FirstOrDefault();
            product.PrimaryImageBlobKey = next?.BlobKey;
            if (next is not null)
            {
                next.IsPrimary = true;
            }
            await SavePrimaryChangeAsync(ct);
        }

        await tx.CommitAsync(ct);

        // Best-effort blob cleanup AFTER commit, so a delete failure never leaves a dangling row.
        try
        {
            await _blob.DeleteAsync(_storage.ProductImagesContainer, blobKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete image blob {BlobKey} for product {ProductId}", blobKey, product.Id);
        }

        _logger.LogInformation("Deleted image {ImageId} from product {ProductId}", imageId, product.Id);
        return product.ToDetailDto();
    }

    /// <inheritdoc />
    public async Task<ProductDetailDto> ReorderProductImagesAsync(Guid productId, IReadOnlyList<Guid> imageIds, CancellationToken ct)
    {
        Product product = await _products.GetByIdForWriteAsync(productId, ct)
            ?? throw new NotFoundException($"Product '{productId}' was not found.");

        // Require the full current set so SortOrder stays a dense, unambiguous 0..n-1.
        HashSet<Guid> current = product.Images.Select(i => i.Id).ToHashSet();
        if (imageIds.Count != current.Count || !imageIds.All(current.Contains))
        {
            throw new ConflictException("The reorder list must contain exactly the product's current image ids.");
        }

        for (int order = 0; order < imageIds.Count; order++)
        {
            product.Images.First(i => i.Id == imageIds[order]).SortOrder = order;
        }

        await _products.SaveChangesAsync(ct);
        _logger.LogInformation("Reordered {ImageCount} image(s) for product {ProductId}", imageIds.Count, product.Id);
        return product.ToDetailDto();
    }

    /// <inheritdoc />
    public async Task<ProductDetailDto> UpdateProductImageAsync(
        Guid productId, Guid imageId, UpdateProductImageRequest request, CancellationToken ct)
    {
        Product product = await _products.GetByIdForWriteAsync(productId, ct)
            ?? throw new NotFoundException($"Product '{productId}' was not found.");

        ProductImage image = product.Images.FirstOrDefault(i => i.Id == imageId)
            ?? throw new NotFoundException($"Image '{imageId}' was not found on product '{productId}'.");

        EnsureVariantOnProduct(product, request.ProductVariantId);

        // "Replace" semantics for the editable fields (null clears / makes general).
        image.AltText = request.AltText;
        image.ProductVariantId = request.ProductVariantId;

        if (request.IsPrimary == true && !image.IsPrimary)
        {
            // Promote: clear the old primary FIRST, then set the new one — two saves in one
            // transaction so the filtered-unique primary index is never transiently violated.
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            foreach (ProductImage existing in product.Images.Where(i => i.IsPrimary))
            {
                existing.IsPrimary = false;
            }
            await _products.SaveChangesAsync(ct);

            image.IsPrimary = true;
            product.PrimaryImageBlobKey = image.BlobKey;
            await SavePrimaryChangeAsync(ct);
            await tx.CommitAsync(ct);
        }
        else
        {
            await _products.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Updated image {ImageId} on product {ProductId} (primary={Primary})", image.Id, product.Id, image.IsPrimary);
        return product.ToDetailDto();
    }

    // Saves changes that set an image as primary. A concurrent writer setting the same product's
    // primary loses the race on the filtered-unique index UX_ProductImage_Primary (SQL 2601/2627);
    // surface that as a 409 to retry, not the catch-all 500 (mirrors OrderCreationService).
    private async Task SavePrimaryChangeAsync(CancellationToken ct)
    {
        try
        {
            await _products.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException("Another image was set as the primary at the same time; please retry.");
        }
    }

    // Rejects a variant id that doesn't belong to the product (or accepts null = general image).
    private static void EnsureVariantOnProduct(Product product, Guid? variantId)
    {
        if (variantId is Guid vid && product.Variants.All(v => v.Id != vid))
        {
            throw new NotFoundException($"Variant '{vid}' was not found on product '{product.Id}'.");
        }
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

        // Deactivate, never hard-delete. Since Phase 2, a variant is referenced by OrderLine and
        // CartItem (both RESTRICT FKs), so hard-deleting a variant that has ever been ordered — or
        // is sitting in a live cart — would raise a SQL FK violation, and an order must keep
        // pointing at the exact variant it was placed against (history is immutable). Flipping
        // IsActive=false hides it from the storefront and from add-to-cart (GetSellableVariantAsync
        // filters on IsActive) while preserving every reference. Reactivate via the variant update
        // endpoint (UpdateVariantAsync with IsActive=true). Idempotent: deactivating an already
        // inactive variant is a no-op success.
        if (variant.IsActive)
        {
            variant.IsActive = false;
            await _products.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Deactivated variant {VariantId} on product {ProductId}", variantId, productId);
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

    // Search runs as a non-sargable LIKE '%term%' (full scan) on a public endpoint, so
    // drop 1-char terms to avoid scanning the whole table on a near-empty query. For a
    // large catalogue this should move to SQL Server full-text search (CONTAINS).
    private const int MinSearchLength = 2;

    private static string? NormalizeSearch(string? search)
    {
        string trimmed = (search ?? string.Empty).Trim();
        return trimmed.Length >= MinSearchLength ? trimmed : null;
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
