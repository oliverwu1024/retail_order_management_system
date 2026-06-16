using Microsoft.Extensions.Logging;
using Retail.Api.Common.Abstractions;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.Exceptions;
using Retail.Api.Repositories;

namespace Retail.Api.Services;

/// <summary>
/// Inventory reservation business logic (Story 2.3) — the heart of "two shoppers can't both
/// buy the last unit".
/// </summary>
/// <remarks>
/// <para>
/// RESERVE is the contended path. For each cart line we (1) read the stock row's current
/// Available + RowVersion, (2) fail fast with <c>OutOfStockException</c> if there's plainly not
/// enough, then (3) bump <c>Reserved</c> with a write GUARDED by that RowVersion. If a
/// concurrent reserver slipped in between our read and our write, the row's RowVersion has
/// moved on, our UPDATE matches 0 rows, and we raise <c>ConcurrencyException</c> (→409). So the
/// last-unit race resolves to exactly one winner; the loser gets a clean 409 to retry.
/// </para>
/// <para>
/// The whole cart is reserved in ONE transaction: either every line is held or none is, so a
/// half-reserved cart can never exist. Releasing reverses the hold (<c>Reserved -= qty</c>,
/// status → Released) and is what the expiry sweeper calls on abandoned carts.
/// </para>
/// <para>
/// COMMIT (turning holds into a real <c>OnHand</c> decrement on payment) lands in Chunk 3 with
/// the checkout webhook, where an order exists to attach the committed reservations to.
/// </para>
/// </remarks>
public sealed class InventoryReservationService : IInventoryReservationService
{
    // A checkout hold lives 15 minutes before the sweeper may release it (DATABASE_DESIGN §3.10)
    // — deliberately shorter than the 30-minute cart lifetime.
    private static readonly TimeSpan ReservationTtl = TimeSpan.FromMinutes(15);

    private readonly IInventoryReservationRepository _repo;
    private readonly RetailDbContext _db; // for transactions only (CODING_STANDARDS-sanctioned)
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly ILogger<InventoryReservationService> _logger;

    public InventoryReservationService(
        IInventoryReservationRepository repo,
        RetailDbContext db,
        TimeProvider timeProvider,
        ICurrentUserAccessor currentUser,
        ILogger<InventoryReservationService> logger)
    {
        _repo = repo;
        _db = db;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ReserveCartAsync(Guid cartId, CancellationToken ct)
    {
        Cart? cart = await _repo.GetOpenCartWithItemsAsync(cartId, ct);
        if (cart is null || cart.Items.Count == 0)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string? actor = _currentUser.UserId; // null for a guest checkout — fine
        DateTimeOffset expiresAt = now.Add(ReservationTtl);

        // All-or-nothing across the cart: every bump + reservation insert commits together.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        foreach (CartItem item in cart.Items)
        {
            int quantity = item.Quantity;

            InventoryItem stock = await _repo.GetStockByVariantAsync(item.ProductVariantId, ct)
                ?? throw new OutOfStockException($"Variant '{item.ProductVariantId}' has no stock record.");

            // Fast path: plainly insufficient stock → the right 409 without touching the row.
            if (stock.Available < quantity)
            {
                throw new OutOfStockException(
                    $"Only {stock.Available} unit(s) available for variant '{item.ProductVariantId}'.");
            }

            // Contended path: bump Reserved only if nobody changed the row since our read.
            int affected = await _repo.TryReserveAsync(stock.Id, stock.RowVersion, quantity, now, actor, ct);
            if (affected == 0)
            {
                throw new ConcurrencyException(
                    $"Stock for variant '{item.ProductVariantId}' changed during checkout. Please try again.");
            }

            _repo.AddReservation(new InventoryReservation
            {
                InventoryItemId = stock.Id,
                CartId = cartId,
                Quantity = quantity,
                Status = ReservationStatus.Active,
                ExpiresAt = expiresAt,
            });
        }

        await _repo.SaveChangesAsync(ct); // inserts the reservation rows (audit-stamped by the interceptor)
        await tx.CommitAsync(ct);
        _logger.LogInformation("Reserved {LineCount} line(s) for cart {CartId}.", cart.Items.Count, cartId);
    }

    /// <inheritdoc />
    public async Task ReleaseCartReservationsAsync(Guid cartId, CancellationToken ct)
    {
        IReadOnlyList<InventoryReservation> active = await _repo.GetActiveCartReservationsAsync(cartId, ct);
        if (active.Count == 0)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string? actor = _currentUser.UserId;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        foreach (InventoryReservation reservation in active)
        {
            await _repo.ReleaseReservedAsync(reservation.InventoryItemId, reservation.Quantity, now, actor, ct);
            reservation.Status = ReservationStatus.Released;
        }

        await _repo.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _logger.LogInformation("Released {Count} reservation(s) for cart {CartId}.", active.Count, cartId);
    }
}
