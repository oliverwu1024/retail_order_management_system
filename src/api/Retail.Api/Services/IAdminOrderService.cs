using Retail.Api.Common.Models;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>The admin order workbench (Phase 3 §8) — read the all-orders list/detail, and the
/// fulfilment (ship/deliver) + admin refund write paths.</summary>
public interface IAdminOrderService
{
    /// <summary>Lists ALL orders (not owner-scoped) with optional status/date/email filters, paged.</summary>
    Task<PagedResult<AdminOrderSummaryDto>> ListAsync(AdminOrderListQuery query, CancellationToken ct);

    /// <summary>Full admin detail for one order (payments + shipment). 404 if it doesn't exist.</summary>
    Task<AdminOrderDetailDto> GetAsync(Guid orderId, CancellationToken ct);

    /// <summary>Marks a Paid order as shipped (creates the shipment, flips it to Fulfilled). 409 if not Paid / already shipped.</summary>
    Task<AdminOrderDetailDto> MarkShippedAsync(Guid orderId, MarkShippedRequest request, CancellationToken ct);

    /// <summary>Marks a shipped order's shipment as delivered. 409 if not shipped.</summary>
    Task<AdminOrderDetailDto> MarkDeliveredAsync(Guid orderId, CancellationToken ct);

    /// <summary>Admin-initiated full refund of a Paid/Fulfilled order (Stripe refund + local reversal). 409 otherwise.</summary>
    Task<AdminOrderDetailDto> RefundAsync(Guid orderId, CancellationToken ct);
}
