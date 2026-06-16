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
    public void AddOrder(Order order) =>
        _db.Orders.Add(order);

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken ct) =>
        await _db.SaveChangesAsync(ct);
}
