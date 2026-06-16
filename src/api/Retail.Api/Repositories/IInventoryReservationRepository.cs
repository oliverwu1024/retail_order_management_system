using Retail.Api.Domain.Entities;

namespace Retail.Api.Repositories;

/// <summary>
/// Persistence for inventory reservations (Story 2.3). Pure data access — the
/// reserve/release orchestration and the transaction live in <c>InventoryReservationService</c>.
/// </summary>
public interface IInventoryReservationRepository
{
    /// <summary>An OPEN cart with its items (variant id + quantity), tracked. Null if missing or not open.</summary>
    Task<Cart?> GetOpenCartWithItemsAsync(Guid cartId, CancellationToken ct);

    /// <summary>
    /// A read-only stock snapshot for a variant — its <c>InventoryItem</c> Id, OnHand, Reserved,
    /// and current RowVersion (read so the guarded update can compare against it). Null if the
    /// variant has no stock row.
    /// </summary>
    Task<InventoryItem?> GetStockByVariantAsync(Guid productVariantId, CancellationToken ct);

    /// <summary>
    /// Atomically bumps <c>Reserved</c> by <paramref name="quantity"/> IFF the row still carries
    /// <paramref name="rowVersion"/> (optimistic concurrency). Returns the affected-row count —
    /// 0 means another writer changed the row first (lost the race). A set-based
    /// <c>ExecuteUpdate</c>, so it stamps the audit fields itself (the interceptor doesn't run).
    /// </summary>
    Task<int> TryReserveAsync(Guid inventoryItemId, byte[] rowVersion, int quantity, DateTimeOffset now, string? actor, CancellationToken ct);

    /// <summary>Decrements <c>Reserved</c> by <paramref name="quantity"/> when releasing a hold.</summary>
    Task ReleaseReservedAsync(Guid inventoryItemId, int quantity, DateTimeOffset now, string? actor, CancellationToken ct);

    /// <summary>
    /// Commits a hold on payment: decrements BOTH <c>OnHand</c> and <c>Reserved</c> by
    /// <paramref name="quantity"/> — the units leave the warehouse for good.
    /// </summary>
    Task CommitReservedAsync(Guid inventoryItemId, int quantity, DateTimeOffset now, string? actor, CancellationToken ct);

    /// <summary>Returns <paramref name="quantity"/> units to <c>OnHand</c> for a variant (e.g. on refund).</summary>
    Task RestockByVariantAsync(Guid productVariantId, int quantity, DateTimeOffset now, string? actor, CancellationToken ct);

    /// <summary>Stages a new reservation for insert.</summary>
    void AddReservation(InventoryReservation reservation);

    /// <summary>A cart's <c>Active</c> reservations (tracked), for release/commit.</summary>
    Task<IReadOnlyList<InventoryReservation>> GetActiveCartReservationsAsync(Guid cartId, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
