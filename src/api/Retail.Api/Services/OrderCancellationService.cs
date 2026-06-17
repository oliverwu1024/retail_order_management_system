using Retail.Api.Common.Enums;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Responses;
using Retail.Api.Exceptions;
using Retail.Api.Mappers;
using Retail.Api.Payments;
using Retail.Api.Repositories;

namespace Retail.Api.Services;

/// <summary>Customer-initiated cancellation (Story 2.4) — refund at Stripe, then reverse locally.</summary>
public sealed class OrderCancellationService : IOrderCancellationService
{
    private readonly IOrderRepository _orders;
    private readonly ICustomerProfileService _profiles;
    private readonly IStripeRefundGateway _refundGateway;
    private readonly IOrderRefundService _orderRefund;
    private readonly TimeProvider _timeProvider;

    public OrderCancellationService(
        IOrderRepository orders,
        ICustomerProfileService profiles,
        IStripeRefundGateway refundGateway,
        IOrderRefundService orderRefund,
        TimeProvider timeProvider)
    {
        _orders = orders;
        _profiles = profiles;
        _refundGateway = refundGateway;
        _orderRefund = orderRefund;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<OrderDetailDto> CancelMyOrderAsync(string appUserId, Guid orderId, CancellationToken ct)
    {
        Guid profileId = (await _profiles.GetMyProfileAsync(appUserId, ct)).Id;
        Order order = await _orders.GetOwnedByIdAsync(orderId, profileId, ct)
            ?? throw new NotFoundException($"Order '{orderId}' was not found.");

        // Only a paid order can be cancelled. (Pending isn't reachable in the webhook-driven flow;
        // Fulfilled/Cancelled/Refunded are terminal for the customer.)
        if (order.Status != OrderStatus.Paid)
        {
            throw new ConflictException($"Order #{order.OrderNumber} can't be cancelled in its current state.");
        }

        string paymentIntentId = await _orders.GetChargePaymentIntentIdAsync(orderId, ct)
            ?? throw new ConflictException($"Order #{order.OrderNumber} has no captured payment to refund.");

        // TOCTOU guard: atomically claim the Paid order (Paid → Refunding) BEFORE touching Stripe.
        // Exactly one of two concurrent cancels wins the claim; the loser sees a non-Paid status and
        // gets a clean 409, so the refund API is hit at most once for this order.
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (!await _orders.TryClaimForRefundAsync(orderId, profileId, now, appUserId, ct))
        {
            throw new ConflictException($"Order #{order.OrderNumber} can't be cancelled in its current state.");
        }

        try
        {
            // We won the claim → we're the sole caller reaching Stripe. The gateway also passes a
            // deterministic idempotency key as belt-and-suspenders against any redelivered refund.
            await _refundGateway.RefundAsync(paymentIntentId, ct);
        }
        catch
        {
            // Stripe failed before any money moved — roll the claim back to Paid so the order stays
            // cancellable, then surface the original failure.
            await _orders.ReleaseRefundClaimAsync(orderId, _timeProvider.GetUtcNow(), appUserId, ct);
            throw;
        }

        // Money is back; apply the reversal locally (Refunding → Refunded + restock + negative
        // Payment). Idempotent, so Stripe's follow-up charge.refunded webhook is a no-op.
        await _orderRefund.RefundByPaymentIntentAsync(paymentIntentId, ct);

        Order refreshed = await _orders.GetOwnedByIdAsync(orderId, profileId, ct)
            ?? throw new NotFoundException($"Order '{orderId}' was not found.");
        return refreshed.ToDetailDto();
    }
}
