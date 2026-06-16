using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>Customer-initiated order cancellation (Story 2.4).</summary>
public interface IOrderCancellationService
{
    /// <summary>
    /// Cancels the caller's paid order: refunds it at Stripe, then reverses it locally
    /// (Order → Refunded + restock). Throws <c>NotFoundException</c> (not the caller's) or
    /// <c>ConflictException</c> (not in a cancellable state / no captured payment).
    /// </summary>
    Task<OrderDetailDto> CancelMyOrderAsync(string appUserId, Guid orderId, CancellationToken ct);
}
