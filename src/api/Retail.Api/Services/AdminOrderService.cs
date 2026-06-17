using Retail.Api.Common.Abstractions;
using Retail.Api.Common.Enums;
using Retail.Api.Common.Models;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Exceptions;
using Retail.Api.Mappers;
using Retail.Api.Payments;
using Retail.Api.Repositories;

namespace Retail.Api.Services;

/// <summary>
/// Admin order workbench (Phase 3 §8): the all-orders read paths plus fulfilment (ship/deliver) and
/// admin refund. Reads aren't owner-scoped (staff see every order); authorization is enforced at the
/// controller via the Orders.* policies.
/// </summary>
public sealed class AdminOrderService : IAdminOrderService
{
    private readonly IOrderRepository _orders;
    private readonly IAuditWriter _audit;
    private readonly IStripeRefundGateway _refundGateway;
    private readonly IOrderRefundService _orderRefund;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly TimeProvider _timeProvider;

    public AdminOrderService(
        IOrderRepository orders,
        IAuditWriter audit,
        IStripeRefundGateway refundGateway,
        IOrderRefundService orderRefund,
        ICurrentUserAccessor currentUser,
        TimeProvider timeProvider)
    {
        _orders = orders;
        _audit = audit;
        _refundGateway = refundGateway;
        _orderRefund = orderRefund;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<PagedResult<AdminOrderSummaryDto>> ListAsync(AdminOrderListQuery query, CancellationToken ct)
    {
        int safePage = query.Page < 1 ? 1 : query.Page;
        int safeSize = Math.Clamp(query.PageSize, 1, 100);

        (IReadOnlyList<Order> items, int total) = await _orders.GetPagedForAdminAsync(
            ParseStatus(query.Status), query.From, query.To, query.CustomerEmail, safePage, safeSize, ct);

        return new PagedResult<AdminOrderSummaryDto>(
            items.Select(order => order.ToAdminSummaryDto()).ToList(), total, safePage, safeSize);
    }

    /// <inheritdoc />
    public async Task<AdminOrderDetailDto> GetAsync(Guid orderId, CancellationToken ct)
    {
        Order order = await _orders.GetDetailForAdminAsync(orderId, ct)
            ?? throw new NotFoundException($"Order '{orderId}' was not found.");
        return order.ToAdminDetailDto();
    }

    /// <inheritdoc />
    public async Task<AdminOrderDetailDto> MarkShippedAsync(Guid orderId, MarkShippedRequest request, CancellationToken ct)
    {
        Order order = await _orders.GetTrackedWithShipmentAsync(orderId, ct)
            ?? throw new NotFoundException($"Order '{orderId}' was not found.");
        if (order.Status != OrderStatus.Paid)
        {
            throw new ConflictException($"Order #{order.OrderNumber} must be Paid to ship (it is {order.Status}).");
        }
        if (order.Shipment is not null)
        {
            throw new ConflictException($"Order #{order.OrderNumber} already has a shipment.");
        }

        // Tracked write: creating the shipment + flipping Paid → Fulfilled in one SaveChanges. The
        // AuditTrailInterceptor auto-records the Order update + Shipment insert; we add a named
        // "Shipped" row for the legible business event. Order.RowVersion makes the status flip
        // concurrency-safe (a stale write → 409).
        DateTimeOffset now = _timeProvider.GetUtcNow();
        order.Shipment = new Shipment
        {
            OrderId = order.Id,
            Carrier = request.Carrier,
            TrackingNumber = request.TrackingNumber,
            Status = ShipmentStatus.Shipped,
            ShippedAt = now,
        };
        order.Status = OrderStatus.Fulfilled;

        _audit.Record(
            "Shipped",
            nameof(Order),
            order.Id.ToString(),
            before: new { Status = nameof(OrderStatus.Paid) },
            after: new { Status = nameof(OrderStatus.Fulfilled), request.Carrier, request.TrackingNumber });

        await _orders.SaveChangesAsync(ct);
        return await GetAsync(orderId, ct);
    }

    /// <inheritdoc />
    public async Task<AdminOrderDetailDto> MarkDeliveredAsync(Guid orderId, CancellationToken ct)
    {
        Order order = await _orders.GetTrackedWithShipmentAsync(orderId, ct)
            ?? throw new NotFoundException($"Order '{orderId}' was not found.");
        if (order.Shipment is null)
        {
            throw new ConflictException($"Order #{order.OrderNumber} has not been shipped.");
        }
        if (order.Shipment.Status != ShipmentStatus.Shipped)
        {
            throw new ConflictException(
                $"Order #{order.OrderNumber}'s shipment can't be marked delivered (it is {order.Shipment.Status}).");
        }

        // Delivery advances the shipment only; the order stays Fulfilled. Tracked write → auto-audited.
        DateTimeOffset now = _timeProvider.GetUtcNow();
        order.Shipment.Status = ShipmentStatus.Delivered;
        order.Shipment.DeliveredAt = now;

        _audit.Record(
            "Delivered",
            nameof(Shipment),
            order.Shipment.Id.ToString(),
            before: new { Status = nameof(ShipmentStatus.Shipped) },
            after: new { Status = nameof(ShipmentStatus.Delivered) });

        await _orders.SaveChangesAsync(ct);
        return await GetAsync(orderId, ct);
    }

    /// <inheritdoc />
    public async Task<AdminOrderDetailDto> RefundAsync(Guid orderId, CancellationToken ct)
    {
        Order order = await _orders.GetDetailForAdminAsync(orderId, ct)
            ?? throw new NotFoundException($"Order '{orderId}' was not found.");

        // Refundable in Phase 3: a PAID (not-yet-shipped) order. A Fulfilled/shipped order is a
        // return/RMA case (deferred — refunding it would restock goods already with the customer and
        // strand the shipment). Refunding is also accepted as a RECOVERY state: a prior attempt that
        // refunded at Stripe but failed to finish the local reversal is safely re-drivable here,
        // because every step below is idempotent (Stripe idempotency key + idempotent reversal).
        if (order.Status is not (OrderStatus.Paid or OrderStatus.Refunding))
        {
            throw new ConflictException(
                $"Order #{order.OrderNumber} can't be refunded in its current state ({order.Status}).");
        }

        string paymentIntentId = await _orders.GetChargePaymentIntentIdAsync(orderId, ct)
            ?? throw new ConflictException($"Order #{order.OrderNumber} has no captured payment to refund.");

        string actor = _currentUser.UserId ?? "system";
        DateTimeOffset now = _timeProvider.GetUtcNow();

        // For a Paid order, atomically claim Paid → Refunding BEFORE touching Stripe (TOCTOU guard:
        // exactly one refund/cancel reaches the payment provider). A Refunding order is already
        // claimed (the recovery path), so it skips this.
        bool justClaimed = false;
        if (order.Status == OrderStatus.Paid)
        {
            if (!await _orders.TryClaimForRefundByIdAsync(orderId, now, actor, ct))
            {
                throw new ConflictException($"Order #{order.OrderNumber} can't be refunded in its current state.");
            }
            justClaimed = true;
        }

        try
        {
            // Idempotent at Stripe via the gateway's deterministic key (refund:{pi}) — safe to (re)call.
            await _refundGateway.RefundAsync(paymentIntentId, ct);
        }
        catch
        {
            // Stripe failed before money moved. If WE just claimed, revert Refunding → Paid so the
            // order stays refundable; a recovery attempt leaves it Refunding (still re-drivable).
            if (justClaimed)
            {
                await _orders.ReleaseRefundClaimToAsync(orderId, OrderStatus.Paid, _timeProvider.GetUtcNow(), actor, ct);
            }
            throw;
        }

        // Stage the named "Refund" business-event row BEFORE the reversal so it commits in the SAME
        // transaction as Refunded/restock/negative-Payment (the reversal's own SaveChanges flushes
        // it). The reversal also auto-audits the Order + Payment changes via the interceptor; the
        // restock is ExecuteUpdate-based (bypasses it), so this named row records the event.
        _audit.Record(
            "Refund",
            nameof(Order),
            orderId.ToString(),
            before: new { Status = nameof(OrderStatus.Paid) },
            after: new { Status = nameof(OrderStatus.Refunded) });

        // Money's back — apply the idempotent local reversal (Refunding → Refunded + restock +
        // negative Payment). Idempotent, so a duplicate (e.g. the charge.refunded webhook) is a no-op.
        await _orderRefund.RefundByPaymentIntentAsync(paymentIntentId, ct);

        // Flush the named row if the reversal short-circuited as a no-op (already Refunded).
        await _orders.SaveChangesAsync(ct);

        return await GetAsync(orderId, ct);
    }

    // A blank or unrecognised status filter means "all" (lenient — it's a filter, not a command).
    // Enum.IsDefined rejects in-range-but-undefined inputs (e.g. "99" or a flags-style combo) that
    // TryParse would otherwise accept and silently turn into an always-empty page.
    private static OrderStatus? ParseStatus(string? status) =>
        Enum.TryParse(status, ignoreCase: true, out OrderStatus parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;
}
