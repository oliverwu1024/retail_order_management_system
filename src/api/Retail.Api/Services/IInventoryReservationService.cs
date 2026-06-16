namespace Retail.Api.Services;

/// <summary>
/// Reserves and releases stock for carts (Story 2.3). A reservation is the soft hold that
/// stops two shoppers buying the same last unit: an active hold inflates
/// <c>InventoryItem.Reserved</c>, and the bump is guarded by the row's <c>RowVersion</c> so two
/// concurrent reservers can't both win.
/// </summary>
public interface IInventoryReservationService
{
    /// <summary>
    /// Reserves stock for every line of an open cart, atomically (one transaction). Throws
    /// <c>OutOfStockException</c> (→409 INVENTORY_INSUFFICIENT) when a line can't be satisfied,
    /// or <c>ConcurrencyException</c> (→409 CONCURRENCY_CONFLICT) when another writer changed
    /// the stock row mid-reserve. No-op when the cart is missing or empty.
    /// </summary>
    Task ReserveCartAsync(Guid cartId, CancellationToken ct);

    /// <summary>
    /// Releases a cart's active reservations (<c>Reserved -= qty</c>, status → Released).
    /// Idempotent — a cart with no active holds is a no-op. Used by the expiry sweeper and on
    /// explicit cart abandonment.
    /// </summary>
    Task ReleaseCartReservationsAsync(Guid cartId, CancellationToken ct);
}
