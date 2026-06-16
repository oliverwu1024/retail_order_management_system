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

    public OrderCancellationService(
        IOrderRepository orders,
        ICustomerProfileService profiles,
        IStripeRefundGateway refundGateway,
        IOrderRefundService orderRefund)
    {
        _orders = orders;
        _profiles = profiles;
        _refundGateway = refundGateway;
        _orderRefund = orderRefund;
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

        // Refund the money at Stripe, then apply the reversal locally (Order → Refunded + restock).
        // The local apply is idempotent, so Stripe's follow-up charge.refunded webhook is a no-op.
        await _refundGateway.RefundAsync(paymentIntentId, ct);
        await _orderRefund.RefundByPaymentIntentAsync(paymentIntentId, ct);

        Order refreshed = await _orders.GetOwnedByIdAsync(orderId, profileId, ct)
            ?? throw new NotFoundException($"Order '{orderId}' was not found.");
        return refreshed.ToDetailDto();
    }
}
