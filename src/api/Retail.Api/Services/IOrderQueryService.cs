using Retail.Api.Common.Models;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>
/// Read-side for customer orders (Story 2.4). Member methods are scoped to the caller's profile;
/// the by-session method is the guest bearer lookup (the Stripe session id from the success URL).
/// </summary>
public interface IOrderQueryService
{
    Task<PagedResult<OrderSummaryDto>> GetMyOrdersAsync(string appUserId, int page, int pageSize, CancellationToken ct);

    /// <summary>The caller's order by id; throws <c>NotFoundException</c> (→404) if it isn't theirs.</summary>
    Task<OrderDetailDto> GetMyOrderAsync(string appUserId, Guid orderId, CancellationToken ct);

    /// <summary>An order by Stripe session id (guest bearer); throws <c>NotFoundException</c> (→404) if none.</summary>
    Task<OrderDetailDto> GetOrderBySessionAsync(string stripeSessionId, CancellationToken ct);
}
