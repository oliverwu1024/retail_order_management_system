using Microsoft.EntityFrameworkCore;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IProductRepository"/>. The soft-delete global
/// query filter (RetailDbContext) keeps deleted rows out of every query automatically.
/// </summary>
public sealed class ProductRepository : IProductRepository
{
    private readonly RetailDbContext _db;

    public ProductRepository(RetailDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Product> Items, int TotalCount)> ListPublishedAsync(
        Guid? categoryId, string? search, int page, int pageSize, CancellationToken ct)
    {
        // AsNoTracking: this is a read path, no change tracking needed.
        IQueryable<Product> query = _db.Products.AsNoTracking().Where(p => p.IsPublished);

        if (categoryId is Guid id)
        {
            query = query.Where(p => p.CategoryId == id);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Contains → LIKE; case-insensitive under SQL Server's default collation.
            query = query.Where(p =>
                p.Name.Contains(search)
                || (p.Description != null && p.Description.Contains(search)));
        }

        int total = await query.CountAsync(ct);

        List<Product> items = await query
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(p => p.Variants)
            .ToListAsync(ct);

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<Product?> GetPublishedDetailBySlugAsync(string slug, CancellationToken ct) =>
        await _db.Products
            .AsNoTracking()
            .Where(p => p.IsPublished && p.Slug == slug)
            .Include(p => p.Category)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Inventory)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<Product?> GetByIdForWriteAsync(Guid id, CancellationToken ct) =>
        await _db.Products
            .Where(p => p.Id == id)
            .Include(p => p.Category)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Inventory)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<bool> SkuExistsAsync(string sku, CancellationToken ct) =>
        await _db.Products.AnyAsync(p => p.Sku == sku, ct);

    /// <inheritdoc />
    public async Task<bool> SlugExistsAsync(string slug, CancellationToken ct) =>
        await _db.Products.AnyAsync(p => p.Slug == slug, ct);

    /// <inheritdoc />
    public async Task<bool> VariantSkuExistsAsync(string sku, CancellationToken ct) =>
        await _db.ProductVariants.AnyAsync(v => v.Sku == sku, ct);

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Product> Items, int TotalCount)> ListForAdminAsync(
        Guid? categoryId, string? search, int page, int pageSize, CancellationToken ct)
    {
        // Same shape as ListPublishedAsync but WITHOUT the IsPublished filter — admins
        // manage drafts too. The soft-delete global filter still hides deleted rows.
        IQueryable<Product> query = _db.Products.AsNoTracking();

        if (categoryId is Guid id)
        {
            query = query.Where(p => p.CategoryId == id);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p =>
                p.Name.Contains(search)
                || (p.Description != null && p.Description.Contains(search)));
        }

        int total = await query.CountAsync(ct);

        List<Product> items = await query
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(p => p.Variants)
            .ToListAsync(ct);

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<Product?> GetDetailByIdAsync(Guid id, CancellationToken ct) =>
        await _db.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Include(p => p.Category)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Inventory)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<bool> ExistsByIdAsync(Guid id, CancellationToken ct) =>
        await _db.Products.AnyAsync(p => p.Id == id, ct);

    /// <inheritdoc />
    public async Task AddAsync(Product product, CancellationToken ct) =>
        await _db.Products.AddAsync(product, ct);

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken ct) =>
        await _db.SaveChangesAsync(ct);
}
