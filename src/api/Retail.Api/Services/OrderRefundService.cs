using Microsoft.EntityFrameworkCore;
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
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly ILogger<OrderRefundService> _logger;

    public OrderRefundService(
        RetailDbContext db,
        IOrderRepository orders,
        IInventoryReservationRepository inventory,
        IAuditWriter audit,
        TimeProvider timeProvider,
        ICurrentUserAccessor currentUser,
        ILogger<OrderRefundService> logger)
    {
        _db = db;
        _orders = orders;
        _inventory = inventory;
        _audit = audit;
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
            return; // idempotent — already reversed (e.g. cancel applied it before this webhook)
        }

        // Reaches here from Paid (refund initiated at Stripe) or Refunding (customer-cancel claim);
        // both advance to Refunded below. Concurrent applies are serialized by Order.RowVersion on
        // SaveChanges — the stale writer affects 0 rows and surfaces as a 409, so we never
        // double-restock.

        OrderStatus priorStatus = order.Status; // Paid (Stripe refund) or Refunding (cancel claim)
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string? actor = _currentUser.UserId; // null in the webhook

        // The InventoryItem id for each restocked variant, so the restock audit row keys on the same
        // entity id a manual InventoryAdjusted row does (one query, not per-line).
        var variantIds = order.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        Dictionary<Guid, Guid> itemIdByVariant = await _db.InventoryItems.AsNoTracking()
            .Where(i => variantIds.Contains(i.ProductVariantId))
            .ToDictionaryAsync(i => i.ProductVariantId, i => i.Id, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Put the sold units back on the shelf.
        foreach (OrderLine line in order.Lines)
        {
            await _inventory.RestockByVariantAsync(line.ProductVariantId, line.Quantity, now, actor, ct);

            // RestockByVariantAsync is a set-based ExecuteUpdate → bypasses the audit interceptor, so
            // emit an explicit row, keeping a refund restock as traceable as a manual stock adjustment.
            string entityId = itemIdByVariant.TryGetValue(line.ProductVariantId, out Guid itemId)
                ? itemId.ToString()
                : line.ProductVariantId.ToString();
            _audit.Record(
                "Restocked",
                nameof(InventoryItem),
                entityId,
                before: null,
                after: new
                {
                    line.ProductVariantId,
                    RestockedQuantity = line.Quantity,
                    OrderId = order.Id,
                    Reason = "OrderRefunded",
                });
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

        // The named "Refund" business-event row, recorded HERE (at the actual idempotent transition)
        // exactly once and with the TRUE prior status — so a recovery re-drive can't duplicate it and
        // it doesn't misreport a Refunding→Refunded transition as Paid→Refunded.
        _audit.Record(
            "Refund",
            nameof(Order),
            order.Id.ToString(),
            before: new { Status = priorStatus.ToString() },
            after: new { Status = nameof(OrderStatus.Refunded) });

        await _orders.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Order {OrderId} refunded; restocked {LineCount} line(s).", order.Id, order.Lines.Count);
    }
}
