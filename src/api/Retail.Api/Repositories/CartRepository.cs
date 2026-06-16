using Microsoft.EntityFrameworkCore;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Repositories;

/// <summary>EF Core implementation of <see cref="ICartRepository"/>.</summary>
public sealed class CartRepository : ICartRepository
{
    private readonly RetailDbContext _db;

    public CartRepository(RetailDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<Cart?> GetOpenCartByProfileAsync(Guid customerProfileId, CancellationToken ct) =>
        await OpenCartsWithGraph()
            .FirstOrDefaultAsync(c => c.CustomerProfileId == customerProfileId, ct);

    /// <inheritdoc />
    public async Task<Cart?> GetOpenCartByAnonymousKeyAsync(string anonymousKey, CancellationToken ct) =>
        await OpenCartsWithGraph()
            .FirstOrDefaultAsync(c => c.AnonymousKey == anonymousKey, ct);

    /// <inheritdoc />
    public async Task AddCartAsync(Cart cart, CancellationToken ct) =>
        await _db.Carts.AddAsync(cart, ct);

    /// <inheritdoc />
    public async Task<ProductVariant?> GetSellableVariantAsync(Guid productVariantId, CancellationToken ct) =>
        await _db.ProductVariants
            .Include(v => v.Product)
            .Include(v => v.Inventory)
            // Product's global soft-delete filter means a deleted product yields a null
            // Product here, so `v.Product != null` also screens out soft-deleted products.
            .FirstOrDefaultAsync(
                v => v.Id == productVariantId
                     && v.IsActive
                     && v.Product != null
                     && v.Product.IsPublished,
                ct);

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken ct) =>
        await _db.SaveChangesAsync(ct);

    // Tracked OPEN carts with the full mapping graph: items → variant → (product + inventory).
    // The `!` on ProductVariant keeps the ThenInclude chain non-null so the nested nav
    // accesses don't trip the nullable-reference analyzer (warnings-as-errors).
    private IQueryable<Cart> OpenCartsWithGraph() =>
        _db.Carts
            .Where(c => c.Status == CartStatus.Open)
            .Include(c => c.Items).ThenInclude(i => i.ProductVariant!).ThenInclude(v => v.Product)
            .Include(c => c.Items).ThenInclude(i => i.ProductVariant!).ThenInclude(v => v.Inventory);
}
