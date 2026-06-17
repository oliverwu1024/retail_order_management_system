using Retail.Api.Common.Models;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>The admin order workbench (Phase 3 §8) — read the all-orders list/detail; fulfilment and
/// refund write paths are added alongside these.</summary>
public interface IAdminOrderService
{
    /// <summary>Lists ALL orders (not owner-scoped) with optional status/date/email filters, paged.</summary>
    Task<PagedResult<AdminOrderSummaryDto>> ListAsync(AdminOrderListQuery query, CancellationToken ct);

    /// <summary>Full admin detail for one order (payments + shipment). 404 if it doesn't exist.</summary>
    Task<AdminOrderDetailDto> GetAsync(Guid orderId, CancellationToken ct);
}
