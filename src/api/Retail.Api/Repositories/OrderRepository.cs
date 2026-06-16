using Microsoft.EntityFrameworkCore;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Repositories;

/// <summary>EF Core implementation of <see cref="IOrderRepository"/>.</summary>
public sealed class OrderRepository : IOrderRepository
{
    private readonly RetailDbContext _db;

    public OrderRepository(RetailDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<Order?> GetByStripeSessionIdAsync(string stripeSessionId, CancellationToken ct) =>
        await _db.Payments
            .Where(p => p.StripeSessionId == stripeSessionId)
            .Select(p => p.Order!)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<Order?> GetByPaymentIntentIdAsync(string paymentIntentId, CancellationToken ct) =>
        await _db.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Payments.Any(p => p.StripePaymentIntentId == paymentIntentId), ct);

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Order> Items, int Total)> GetPagedByProfileAsync(
        Guid customerProfileId, int page, int pageSize, CancellationToken ct)
    {
        IQueryable<Order> query = _db.Orders.AsNoTracking().Where(o => o.CustomerProfileId == customerProfileId);
        int total = await query.CountAsync(ct);
        List<Order> items = await query
            .Include(o => o.Lines)
            .OrderByDescending(o => o.PlacedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    /// <inheritdoc />
    public async Task<Order?> GetOwnedByIdAsync(Guid orderId, Guid customerProfileId, CancellationToken ct) =>
        await _db.Orders.AsNoTracking()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerProfileId == customerProfileId, ct);

    /// <inheritdoc />
    public async Task<Order?> GetDetailByStripeSessionIdAsync(string stripeSessionId, CancellationToken ct) =>
        await _db.Orders.AsNoTracking()
            .Include(o => o.Lines)
            // Guest orders only. This backs the [AllowAnonymous] by-session lookup, where the Stripe
            // session id IS the bearer. Member orders ALSO carry a Payment with this session id, so
            // without the CustomerProfileId == null guard a member's PII-bearing order would be
            // readable by any unauthenticated caller holding the id — members must instead use the
            // account-scoped path (GetOwnedByIdAsync).
            .FirstOrDefaultAsync(
                o => o.CustomerProfileId == null && o.Payments.Any(p => p.StripeSessionId == stripeSessionId), ct);

    /// <inheritdoc />
    public async Task<string?> GetChargePaymentIntentIdAsync(Guid orderId, CancellationToken ct) =>
        await _db.Payments.AsNoTracking()
            .Where(p => p.OrderId == orderId && p.AmountCents > 0 && p.StripePaymentIntentId != null)
            .Select(p => p.StripePaymentIntentId)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public void AddOrder(Order order) =>
        _db.Orders.Add(order);

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken ct) =>
        await _db.SaveChangesAsync(ct);
}
