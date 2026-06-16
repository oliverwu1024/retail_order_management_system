using Microsoft.Extensions.Logging;
using Retail.Api.Common.Abstractions;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.Repositories;

namespace Retail.Api.Services;

/// <summary>
/// Reverses a paid order on refund (Story 2.2): Order → Refunded, restock the lines, record a
/// negative payment. All in one transaction; idempotent.
/// </summary>
public sealed class OrderRefundService : IOrderRefundService
{
    private readonly RetailDbContext _db; // for the transaction
    private readonly IOrderRepository _orders;
    private readonly IInventoryReservationRepository _inventory;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly ILogger<OrderRefundService> _logger;

    public OrderRefundService(
        RetailDbContext db,
        IOrderRepository orders,
        IInventoryReservationRepository inventory,
        TimeProvider timeProvider,
        ICurrentUserAccessor currentUser,
        ILogger<OrderRefundService> logger)
    {
        _db = db;
        _orders = orders;
        _inventory = inventory;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RefundByPaymentIntentAsync(string paymentIntentId, CancellationToken ct)
    {
        Order? order = await _orders.GetByPaymentIntentIdAsync(paymentIntentId, ct);
        if (order is null)
        {
            _logger.LogWarning("Refund webhook for unknown PaymentIntent {PaymentIntentId}; ignoring.", paymentIntentId);
            return;
        }

        if (order.Status == OrderStatus.Refunded)
        {
            return; // idempotent — already reversed
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string? actor = _currentUser.UserId; // null in the webhook

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Put the sold units back on the shelf.
        foreach (OrderLine line in order.Lines)
        {
            await _inventory.RestockByVariantAsync(line.ProductVariantId, line.Quantity, now, actor, ct);
        }

        order.Status = OrderStatus.Refunded;
        order.Payments.Add(new Payment
        {
            OrderId = order.Id,
            Provider = "stripe",
            StripePaymentIntentId = paymentIntentId,
            AmountCents = -order.TotalCents, // negative = refund
            Currency = "AUD",
            Status = PaymentStatus.Refunded,
        });

        await _orders.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Order {OrderId} refunded; restocked {LineCount} line(s).", order.Id, order.Lines.Count);
    }
}
