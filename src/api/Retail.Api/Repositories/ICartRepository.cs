using Retail.Api.Domain.Entities;

namespace Retail.Api.Repositories;

/// <summary>
/// Persistence for carts (Phase 2, Story 2.1). Pure data access — the owner-resolution,
/// price-snapshot, dedup-merge, and login-merge rules all live in <c>CartService</c>.
/// </summary>
public interface ICartRepository
{
    /// <summary>
    /// The member's OPEN cart, tracked, with its items + each item's variant → product and
    /// inventory loaded (for mapping + stock hints). Null if the member has no open cart.
    /// </summary>
    Task<Cart?> GetOpenCartByProfileAsync(Guid customerProfileId, CancellationToken ct);

    /// <summary>The guest's OPEN cart by anonymous key, tracked, with the same Include graph. Null if none.</summary>
    Task<Cart?> GetOpenCartByAnonymousKeyAsync(string anonymousKey, CancellationToken ct);

    /// <summary>Stages a new cart for insert.</summary>
    Task AddCartAsync(Cart cart, CancellationToken ct);

    /// <summary>
    /// A sellable variant by id — active, with a published, non-deleted product — and its
    /// inventory row, for the add-to-cart price snapshot + stock check. Returns null when the
    /// variant doesn't exist or isn't sellable (the service turns that into a 404).
    /// </summary>
    Task<ProductVariant?> GetSellableVariantAsync(Guid productVariantId, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
