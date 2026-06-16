using Microsoft.EntityFrameworkCore;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Repositories;

/// <summary>EF Core implementation of <see cref="IInventoryReservationRepository"/>.</summary>
public sealed class InventoryReservationRepository : IInventoryReservationRepository
{
    private readonly RetailDbContext _db;

    public InventoryReservationRepository(RetailDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<Cart?> GetOpenCartWithItemsAsync(Guid cartId, CancellationToken ct) =>
        await _db.Carts
            .Where(c => c.Status == CartStatus.Open)
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == cartId, ct);

    /// <inheritdoc />
    public async Task<InventoryItem?> GetStockByVariantAsync(Guid productVariantId, CancellationToken ct) =>
        // No tracking — we update set-based via ExecuteUpdate, so we only need the values
        // (Id, OnHand, Reserved, RowVersion) to decide and to guard the write.
        await _db.InventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.ProductVariantId == productVariantId, ct);

    /// <inheritdoc />
    public async Task<int> TryReserveAsync(
        Guid inventoryItemId, byte[] rowVersion, int quantity, DateTimeOffset now, string? actor, CancellationToken ct) =>
        await _db.InventoryItems
            // Optimistic-concurrency guard: only the writer whose RowVersion still matches the
            // one we read can win. SQL Server stamps a new rowversion on the UPDATE, so a racing
            // writer's predicate no longer matches → it affects 0 rows.
            .Where(i => i.Id == inventoryItemId && i.RowVersion == rowVersion)
            // Set-based UPDATE — bypasses the AuditingInterceptor, so we stamp the audit fields
            // here ourselves (matching what the interceptor would record on a tracked save).
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(i => i.Reserved, i => i.Reserved + quantity)
                    .SetProperty(i => i.UpdatedAt, now)
                    .SetProperty(i => i.UpdatedBy, actor),
                ct);

    /// <inheritdoc />
    public async Task ReleaseReservedAsync(
        Guid inventoryItemId, int quantity, DateTimeOffset now, string? actor, CancellationToken ct) =>
        // Releasing is driven by the reservation rows (each Active hold is released exactly
        // once, in the same transaction that flips it to Released), so no RowVersion guard is
        // needed — it can't double-release.
        await _db.InventoryItems
            .Where(i => i.Id == inventoryItemId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(i => i.Reserved, i => i.Reserved - quantity)
                    .SetProperty(i => i.UpdatedAt, now)
                    .SetProperty(i => i.UpdatedBy, actor),
                ct);

    /// <inheritdoc />
    public void AddReservation(InventoryReservation reservation) =>
        _db.InventoryReservations.Add(reservation);

    /// <inheritdoc />
    public async Task<IReadOnlyList<InventoryReservation>> GetActiveCartReservationsAsync(Guid cartId, CancellationToken ct) =>
        await _db.InventoryReservations
            .Where(r => r.CartId == cartId && r.Status == ReservationStatus.Active)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken ct) =>
        await _db.SaveChangesAsync(ct);
}
