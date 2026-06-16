using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Retail.Api.Common.Enums;
using Retail.Api.Data;

namespace Retail.Api.Services;

/// <summary>Default <see cref="ICartSweepService"/> — one pass over the expired carts.</summary>
public sealed class CartSweepService : ICartSweepService
{
    // Bound the work per pass so a backlog can't turn one sweep into a long-running scan.
    private const int BatchLimit = 200;

    private readonly RetailDbContext _db;
    private readonly IInventoryReservationService _reservations;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CartSweepService> _logger;

    public CartSweepService(
        RetailDbContext db,
        IInventoryReservationService reservations,
        TimeProvider timeProvider,
        ILogger<CartSweepService> logger)
    {
        _db = db;
        _reservations = reservations;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> SweepExpiredCartsAsync(CancellationToken ct)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        List<Guid> expired = await _db.Carts
            .Where(c => c.Status == CartStatus.Open && c.ExpiresAt < now)
            .OrderBy(c => c.ExpiresAt)
            .Take(BatchLimit)
            .Select(c => c.Id)
            .ToListAsync(ct);

        foreach (Guid cartId in expired)
        {
            // Hand the held stock back first (reservation service runs its own transaction),
            // then tombstone the cart. The abandon is guarded on still-Open, so a cart that
            // converted (checked out) since the scan is left untouched.
            await _reservations.ReleaseCartReservationsAsync(cartId, ct);
            await _db.Carts
                .Where(c => c.Id == cartId && c.Status == CartStatus.Open)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(c => c.Status, CartStatus.Abandoned)
                        .SetProperty(c => c.UpdatedAt, now),
                    ct);
        }

        if (expired.Count > 0)
        {
            _logger.LogInformation("Cart sweep abandoned {Count} expired cart(s).", expired.Count);
        }

        return expired.Count;
    }
}
